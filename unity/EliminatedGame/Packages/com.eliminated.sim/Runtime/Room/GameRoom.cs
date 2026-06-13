using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Economy;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Room
{
    /// <summary>
    /// One game room: lobby → intro → playing → roundResult → seriesResult.
    /// Authoritative and deterministic — driven by a fixed-dt <see cref="Tick"/>,
    /// with all phase timers advanced by dt (no wall-clock) so the whole series
    /// is reproducible from a seed. Ported from lib/server/GameRoom.ts (betting,
    /// persistence, and chat are client/Phase-3 concerns and omitted here).
    /// </summary>
    public sealed class GameRoom
    {
        // Phase durations (seconds). Match the reference *_MS constants.
        private const float IntroSec = Constants.IntroMs / 1000f;
        private const float GoSec = Constants.GoMs / 1000f;
        private const float ResultSec = Constants.ResultMs / 1000f;
        private const float SeriesResultSec = Constants.SeriesResultMs / 1000f;

        private const int RecentGamesWindow = 3;
        private const int FinaleOvertimeCap = 5;
        private const int FinaleFieldTarget = 3;
        private const float CullCoeff = 0.5f;
        private const int MaxPlayerNumber = 456;

        public string Code { get; }
        public RoomConfig Config { get; private set; } = new RoomConfig();
        public RoomPhase Phase { get; private set; } = RoomPhase.Lobby;
        public string HostId { get; private set; } = "";

        public int RoundIndex { get; private set; }     // completed rounds
        public int TotalRounds { get; private set; }
        public bool TotalRoundsKnown { get; private set; }
        // True when the current/upcoming round is the last scheduled game (or a
        // Hardcore overtime round). Safe to expose even in Mystery mode: it reveals
        // only "this is the finale", never the hidden total. The authoritative
        // source for the finale music + announcer cue, online and offline.
        public bool IsFinalGame => IsFinalRound();
        public GameId? CurrentGame { get; private set; }
        public bool CurrentNight { get; private set; }
        public bool PlayStarted { get; private set; }

        public RoundReport LastRoundReport { get; private set; }
        public SeriesResult SeriesResult { get; private set; }

        private readonly List<Player> _players = new List<Player>();
        public IReadOnlyList<Player> Players => _players;

        private readonly Rng _rng;
        private IMinigame _game;
        private List<Actor> _actors = new List<Actor>();
        private List<string> _participants = new List<string>();

        private readonly List<GameId> _playedGames = new List<GameId>();
        private readonly List<GameId> _recentGames = new List<GameId>();
        private GameId? _lastGame;

        private float _phaseTimer; // seconds remaining in intro/result/seriesResult
        private float _goTimer;    // seconds remaining in the GO hold
        private int _botSeq;

        public GameRoom(string code, int seed)
        {
            Code = code;
            _rng = new Rng(seed);
        }

        // ── Membership ───────────────────────────────────────────────────
        public void AddPlayer(Player p)
        {
            if (_players.Count == 0) HostId = p.Id;
            if (p.Number <= 0) p.Number = AssignNumber();
            _players.Add(p);
        }

        /// <summary>Add an AI competitor. Refuses (returns null) once the room is
        /// at <see cref="RoomConfig.MaxPlayers"/> so neither manual "add bot"
        /// requests nor bot-fill can ever overflow the lobby cap.</summary>
        public Player AddBot()
        {
            if (Competitors().Count >= Config.MaxPlayers) return null;
            var bot = new Player("bot_" + (++_botSeq), BotNames.Random(_rng), BotNames.RandomCharacter(_rng), isBot: true)
            {
                Number = AssignNumber()
            };
            _players.Add(bot);
            return bot;
        }

        public void RemovePlayer(string id)
        {
            var p = _players.FirstOrDefault(x => x.Id == id);
            if (p == null) return;
            if (Phase == RoomPhase.Playing && _game != null && _participants.Contains(id))
                _game.Forfeit(id);
            _players.Remove(p);
            if (HostId == id) HostId = _players.FirstOrDefault()?.Id ?? "";
        }

        public void SetReady(string id, bool ready) { var p = Find(id); if (p != null) p.Ready = ready; }
        public void SetSpectator(string id, bool on) { var p = Find(id); if (p != null) p.Spectator = on; }

        public void UpdateConfig(RoomConfig config)
        {
            if (Phase == RoomPhase.Lobby) Config = config.Clone();
        }

        private Player Find(string id) => _players.FirstOrDefault(p => p.Id == id);
        private int AssignNumber()
        {
            var used = new HashSet<int>(_players.Select(p => p.Number));
            for (int guard = 0; guard < 4000; guard++)
            {
                int n = 1 + _rng.NextInt(MaxPlayerNumber);
                if (!used.Contains(n)) return n;
            }
            return _players.Count + 1;
        }

        private List<Player> Competitors() => _players.Where(p => !p.Spectator).ToList();
        private List<Player> AlivePlayers() => _players.Where(p => p.AliveInSeries && !p.Spectator).ToList();

        // ── Series lifecycle ─────────────────────────────────────────────
        /// <summary>Host action: begin the series. Returns false if it can't start.</summary>
        public bool StartSeries()
        {
            if (Phase != RoomPhase.Lobby) return false;

            if (Config.BotFill)
            {
                while (Competitors().Count < Constants.BotFillTarget && _players.Count < Config.MaxPlayers)
                    AddBot();
            }
            if (Competitors().Count < Constants.MinToStart) return false;

            foreach (var p in _players)
            {
                p.AliveInSeries = !p.Spectator;
                p.MarblesEarned = 0;
                p.Score = 0;
                p.RoundsSurvived = 0;
                p.Title = null;
            }

            RoundIndex = 0;
            _playedGames.Clear();
            _lastGame = null;

            if (Config.Rounds.Mystery)
            {
                TotalRounds = 3 + _rng.NextInt(4); // 3..6, hidden
                TotalRoundsKnown = false;
            }
            else
            {
                TotalRounds = MathUtil.Clamp(Config.Rounds.Count, 1, 12);
                TotalRoundsKnown = true;
            }

            BeginIntro();
            return true;
        }

        public void Tick(float dt)
        {
            switch (Phase)
            {
                case RoomPhase.Intro:
                    _phaseTimer -= dt;
                    if (_phaseTimer <= 0f) BeginPlaying();
                    break;

                case RoomPhase.Playing:
                    if (!PlayStarted)
                    {
                        _goTimer -= dt;
                        if (_goTimer <= 0f) PlayStarted = true;
                    }
                    else
                    {
                        _game.Tick(dt);
                        if (_game.IsDone) EndRound();
                    }
                    break;

                case RoomPhase.RoundResult:
                    _phaseTimer -= dt;
                    if (_phaseTimer <= 0f) AdvanceAfterResult();
                    break;

                case RoomPhase.SeriesResult:
                    _phaseTimer -= dt;
                    if (_phaseTimer <= 0f) Phase = RoomPhase.Lobby;
                    break;
            }
        }

        /// <summary>Forward a player's input to the active game (gated by the GO hold).</summary>
        public void HandleInput(string playerId, GameInput input)
        {
            if (Phase == RoomPhase.Playing && PlayStarted && _game != null)
                _game.OnInput(playerId, input);
        }

        public Snapshot BuildSnapshot()
        {
            if (_game == null) return null;
            var snap = _game.BuildSnapshot();
            snap.StartAt = PlayStarted ? (double?)null : _goTimer * 1000.0;
            return snap;
        }

        private bool IsFinalRound() => TotalRounds > 0 && RoundIndex >= TotalRounds - 1;

        private float ComputeIntensity(int aliveNow)
        {
            if (IsFinalRound()) return 0.9f;
            if (Config.Mode != SeriesMode.Hardcore)
            {
                if (TotalRounds <= 1) return 0.7f;
                return MathUtil.Clamp(0.18f + 0.7f * (RoundIndex / (float)(TotalRounds - 1)), 0.12f, 0.85f);
            }
            if (aliveNow <= FinaleFieldTarget) return 0.22f;
            int roundsToCull = Math.Max(1, TotalRounds - 1 - RoundIndex);
            float perRoundRatio = (float)Math.Pow(FinaleFieldTarget / (float)aliveNow, 1f / roundsToCull);
            return MathUtil.Clamp((1f - perRoundRatio) / CullCoeff, 0.12f, 0.9f);
        }

        private List<Player> GetParticipants()
            => Config.Mode == SeriesMode.Hardcore ? AlivePlayers() : Competitors();

        private GameId ChooseGame(int aliveCount)
        {
            var allowList = Config.AllowedGames.Count > 0
                ? Config.AllowedGames.Where(GameCatalog.IsRegistered).ToList()
                : GameCatalog.Registered.ToList();
            if (allowList.Count == 0) allowList = GameCatalog.Registered.ToList();

            bool Playable(GameId g)
            {
                var m = GameCatalog.Of(g);
                return m.MinPlayers <= aliveCount && (!m.RequiresEven || aliveCount % 2 == 0);
            }

            if (IsFinalRound())
            {
                bool CanFinale(GameId g)
                {
                    var m = GameCatalog.Of(g);
                    return (m.Finale || m.FinaleCapable) && Playable(g);
                }
                var finales = allowList.Where(CanFinale).ToList();
                if (finales.Count == 0) finales = GameCatalog.Registered.Where(CanFinale).ToList();
                var unseen = finales.Where(g => !_recentGames.Contains(g)).ToList();
                var notLast = finales.Where(g => g != _lastGame).ToList();
                var pool0 = unseen.Count > 0 ? unseen : (notLast.Count > 0 ? notLast : finales);
                if (pool0.Count > 0) return _rng.Pick(pool0);
            }

            var pool = allowList.Where(g => !GameCatalog.Of(g).Finale && Playable(g)).ToList();
            if (pool.Count == 0) pool = GameCatalog.Registered.Where(g => !GameCatalog.Of(g).Finale && Playable(g)).ToList();
            if (pool.Count == 0) return GameId.RedLight;

            if (RoundIndex == 0)
            {
                var gentle = pool.Where(g => GameCatalog.Of(g).Cull != CullStrength.High).ToList();
                if (gentle.Count > 0) pool = gentle;
            }

            var freshUnseen = pool.Where(g => !_playedGames.Contains(g) && !_recentGames.Contains(g)).ToList();
            var fresh = pool.Where(g => !_playedGames.Contains(g)).ToList();
            var finalPool = freshUnseen.Count > 0 ? freshUnseen : (fresh.Count > 0 ? fresh : pool);
            var noRepeat = finalPool.Where(g => g != _lastGame).ToList();
            if (noRepeat.Count > 0) finalPool = noRepeat;
            return _rng.Pick(finalPool);
        }

        private void BeginIntro()
        {
            // casual respawn: everyone (non-spectator) re-enters each round
            if (Config.Mode == SeriesMode.Casual)
                foreach (var p in _players) p.AliveInSeries = !p.Spectator;

            var participants = GetParticipants();
            var game = ChooseGame(participants.Count);
            CurrentGame = game;
            if (!_playedGames.Contains(game)) _playedGames.Add(game);
            _recentGames.RemoveAll(g => g == game);
            _recentGames.Add(game);
            while (_recentGames.Count > RecentGamesWindow) _recentGames.RemoveAt(0);

            var meta = GameCatalog.Of(game);
            CurrentNight = Config.NightMode && Config.Mode == SeriesMode.Hardcore && meta.Nightable && _rng.Chance(0.5);

            Phase = RoomPhase.Intro;
            _phaseTimer = IntroSec;
        }

        private void BeginPlaying()
        {
            var participants = GetParticipants();
            _participants = participants.Select(p => p.Id).ToList();
            _actors = participants.Select(p => new Actor
            {
                Id = p.Id,
                Name = p.Name,
                CharacterId = p.CharacterId,
                Accessories = p.Accessories,
                Number = p.Number,
                IsBot = p.IsBot || !p.Connected // disconnected humans idle (no AI)
            }).ToList();

            var ctx = new GameContext
            {
                Actors = _actors,
                Rng = _rng,
                FriendlyFire = Config.FriendlyFire,
                RoundIndex = RoundIndex,
                TotalRounds = TotalRounds,
                IsFinale = IsFinalRound(),
                Intensity = ComputeIntensity(participants.Count),
                Night = CurrentNight,
                ForceSingleSurvivor = Config.Mode == SeriesMode.Hardcore && IsFinalRound()
            };

            _game = GameCatalog.Create(CurrentGame.Value, ctx);
            _game.Start();
            Phase = RoomPhase.Playing;
            PlayStarted = false;
            _goTimer = GoSec;
        }

        private void EndRound()
        {
            var result = _game.Result();
            var survivors = new HashSet<string>(result.SurvivorIds);
            var rankByPlayer = result.Ranking.ToDictionary(r => r.PlayerId, r => r);
            int bestPlacement = result.Ranking.Count > 0 ? result.Ranking.Min(r => r.Placement) : 1;

            var report = new RoundReport
            {
                Game = CurrentGame.Value,
                RoundNumber = RoundIndex + 1,
                SurvivorIds = survivors.ToList()
            };

            foreach (var pid in _participants)
            {
                var p = Find(pid);
                if (p == null) continue;
                var rk = rankByPlayer.TryGetValue(pid, out var r) ? r : null;
                int placement = rk?.Placement ?? 999;
                bool survived = survivors.Contains(pid);
                int marbles;
                if (survived)
                {
                    marbles = Marbles.SurvivePerRound + (placement == bestPlacement ? Marbles.RoundWinBonus : 0);
                    p.RoundsSurvived += 1;
                    p.Score += (_participants.Count - placement + 1) * 10 + 50;
                }
                else
                {
                    // Hardcore: elimination pays nothing — the dead leave broke
                    // (a Dead Pool wager is their only way back into the black).
                    // Casual still hands out a small consolation for the round.
                    marbles = Config.Mode == SeriesMode.Hardcore ? 0 : Marbles.ElimParticipation;
                    p.Score += Math.Max(0, _participants.Count - placement) * 4;
                    if (Config.Mode == SeriesMode.Hardcore) p.AliveInSeries = false;
                }
                p.MarblesEarned += marbles;
                report.Entries.Add(new RankEntry(pid, placement, survived, rk?.Note) { MarblesEarned = marbles });
            }
            report.Entries.Sort((a, b) => a.Placement.CompareTo(b.Placement));

            LastRoundReport = report;
            _lastGame = CurrentGame;
            _game = null;
            Phase = RoomPhase.RoundResult;
            _phaseTimer = ResultSec;
        }

        private void AdvanceAfterResult()
        {
            RoundIndex += 1;
            int aliveCount = AlivePlayers().Count;
            bool seriesOver;
            if (Config.Mode == SeriesMode.Hardcore)
                seriesOver = aliveCount <= 1 || RoundIndex >= TotalRounds + FinaleOvertimeCap;
            else
                seriesOver = RoundIndex >= TotalRounds;

            if (seriesOver) EndSeries();
            else BeginIntro();
        }

        private void EndSeries()
        {
            var all = Competitors();
            all.Sort((a, b) =>
            {
                if (Config.Mode == SeriesMode.Hardcore && a.AliveInSeries != b.AliveInSeries)
                    return a.AliveInSeries ? -1 : 1;
                if (a.RoundsSurvived != b.RoundsSurvived) return b.RoundsSurvived - a.RoundsSurvived;
                if (a.Score != b.Score) return b.Score - a.Score;
                return b.MarblesEarned - a.MarblesEarned;
            });

            string championId = Config.Mode == SeriesMode.Hardcore
                ? (all.Count > 0 && all[0].AliveInSeries ? all[0].Id : null)
                : (all.Count > 0 ? all[0].Id : null);

            var standings = new List<SeriesStanding>();
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                int placement = i + 1;
                bool isChampion = placement == 1 && (Config.Mode != SeriesMode.Hardcore || p.AliveInSeries);

                if (Config.Mode == SeriesMode.Hardcore && !isChampion)
                {
                    // Hardcore forfeit: anyone who was eliminated banks nothing,
                    // wiping the running tally they built up while still alive.
                    // Only the last player standing cashes out the run.
                    p.MarblesEarned = 0;
                }
                else
                {
                    int bonus = Marbles.PlacementBonus(placement);
                    if (isChampion) bonus += Marbles.ChampionBonus;
                    p.MarblesEarned += bonus;
                }

                p.Title = Marbles.PlacementTitle(placement);
                standings.Add(new SeriesStanding
                {
                    PlayerId = p.Id,
                    Placement = placement,
                    Marbles = p.MarblesEarned,
                    RoundsSurvived = p.RoundsSurvived,
                    Title = p.Title
                });
            }

            SeriesResult = new SeriesResult { Standings = standings, ChampionId = championId };
            Phase = RoomPhase.SeriesResult;
            _phaseTimer = SeriesResultSec;
        }
    }
}
