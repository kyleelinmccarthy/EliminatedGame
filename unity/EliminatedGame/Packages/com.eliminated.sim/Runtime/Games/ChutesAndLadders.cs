using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Chutes &amp; Ladders — race up a 64-square board by tapping to roll. Ladders
    /// lift you automatically; a chute is a fork (pick LEFT/RIGHT) — one side resets
    /// you to start, the other is the abyss. Reach the top to be safe. Stragglers at
    /// the buzzer are culled worst-first. Ported from
    /// lib/server/games/ChutesAndLadders.ts.
    /// </summary>
    public sealed class ChutesAndLadders : IMinigame
    {
        private const int Goal = 64;
        private const int Cols = 8;
        private const int NLadders = 4;
        private const int NChutes = 3;
        private const float RollCd = 0.7f;
        private const float DieShow = 0.45f;
        private const int MaxLadderSpan = 30;

        private sealed class Climber
        {
            public string Id;
            public bool IsBot;
            public bool Alive = true;
            public int Square;
            public bool Finished;
            public int FinishRank;
            public int Rolls;
            public float RollCdT;
            public float DieShowT;
            public int Die;
            public int Choosing = -1;
            public float BotThink;
            public float BotCadence;
        }

        private struct Ladder { public int From, To; }
        private sealed class Chute { public int Id; public int Square; public int DeathSide; public bool[] Revealed = new bool[2]; }

        private readonly GameContext _ctx;
        private readonly Rng _rng;
        private readonly Dictionary<string, Climber> _climbers = new Dictionary<string, Climber>();
        private readonly List<Ladder> _ladders = new List<Ladder>();
        private readonly List<Chute> _chutes = new List<Chute>();
        private readonly List<(string id, string note)> _elim = new List<(string, string)>();
        private readonly List<Effect> _fx = new List<Effect>();

        private float _timeLeft = 35f;
        private float _duration = 35f;
        private int _finishCount;
        private bool _done;

        public ChutesAndLadders(GameContext ctx) { _ctx = ctx; _rng = ctx.Rng; }

        public GameId Id => GameId.ChutesAndLadders;
        public bool IsDone => _done;
        public int SquareOf(string id) => _climbers.TryGetValue(id, out var c) ? c.Square : 0;
        public bool Finished(string id) => _climbers.TryGetValue(id, out var c) && c.Finished;

        public void Start()
        {
            foreach (var a in _ctx.Actors)
                _climbers[a.Id] = new Climber
                {
                    Id = a.Id, IsBot = a.IsBot,
                    RollCdT = _rng.NextFloat() * 0.4f,
                    BotCadence = a.IsBot ? RollCd + (0.15f + _rng.NextFloat() * 0.65f) : RollCd
                };
            BuildBoard();
            _duration = (float)Math.Round(40f - _ctx.Intensity * 10f);
            _timeLeft = _duration;
        }

        private void BuildBoard()
        {
            var used = new HashSet<int> { 1, Goal };
            int RowOf(int s) => (s - 1) / Cols;
            int FreeCell()
            {
                for (int guard = 0; guard < 200; guard++)
                {
                    int s = 2 + _rng.NextInt(Goal - 2);
                    if (!used.Contains(s)) { used.Add(s); return s; }
                }
                return -1;
            }

            for (int i = 0; i < NLadders; i++)
            {
                int a = FreeCell(), b = FreeCell();
                if (a < 0 || b < 0) break;
                int lo = Math.Min(a, b), hi = Math.Max(a, b);
                if (RowOf(hi) - RowOf(lo) < 2 || hi - lo > MaxLadderSpan) { used.Remove(a); used.Remove(b); i--; continue; }
                _ladders.Add(new Ladder { From = lo, To = hi });
            }
            for (int i = 0; i < NChutes; i++)
            {
                int square = FreeCell();
                if (square < 0) break;
                _chutes.Add(new Chute { Id = i, Square = square, DeathSide = _rng.NextFloat() < 0.5f ? 0 : 1 });
            }
        }

        public void OnInput(string playerId, GameInput input)
        {
            if (input.Kind == InputKind.Choose) { Choose(playerId, input.Value == "R" ? 1 : 0); return; }
            if (input.Kind == InputKind.Tap || (input.Kind == InputKind.Action && input.Name == "roll")) Roll(playerId);
        }

        private void Roll(string id)
        {
            if (!_climbers.TryGetValue(id, out var c) || !c.Alive || c.Finished) return;
            if (c.Choosing >= 0 || c.RollCdT > 0f) return;
            int die = 1 + _rng.NextInt(6);
            c.Die = die; c.DieShowT = DieShow; c.Rolls++; c.RollCdT = RollCd;

            int next = c.Square + die;
            if (next >= Goal)
            {
                c.Square = Goal; c.Finished = true; c.FinishRank = ++_finishCount;
                _fx.Add(new Effect(EffectKind.Confetti));
                return;
            }
            c.Square = next;
            var ladder = _ladders.FirstOrDefault(l => l.From == c.Square);
            if (ladder.From == c.Square && ladder.To != 0) { c.Square = ladder.To; _fx.Add(new Effect(EffectKind.Pickup)); return; }
            var chute = _chutes.FirstOrDefault(s => s.Square == c.Square);
            if (chute != null)
            {
                c.Choosing = chute.Id;
                if (c.IsBot) c.BotThink = 0.4f + _rng.NextFloat() * 1.1f;
            }
        }

        private void Choose(string id, int side)
        {
            if (!_climbers.TryGetValue(id, out var c) || !c.Alive || c.Finished || c.Choosing < 0) return;
            var chute = _chutes.FirstOrDefault(s => s.Id == c.Choosing);
            c.Choosing = -1;
            if (chute == null) return;
            chute.Revealed[side] = true;
            if (side == chute.DeathSide)
            {
                bool othersAlive = _climbers.Values.Any(x => x.Id != id && x.Alive);
                if (!othersAlive) { c.Square = 0; return; } // last-blob lucky catch
                c.Alive = false; SyncActor(id, false);
                _elim.Add((id, "Took the wrong fork — into the abyss!"));
                _fx.Add(new Effect(EffectKind.Death));
                MaybeFinishEarly();
                return;
            }
            c.Square = 0; // kinder fork — back to start
        }

        private int PickSide(int chuteId)
        {
            var ch = _chutes.FirstOrDefault(s => s.Id == chuteId);
            if (ch == null) return 0;
            int safe = ch.DeathSide == 0 ? 1 : 0;
            if (ch.Revealed[0] || ch.Revealed[1]) return safe;
            return _rng.NextFloat() < 0.5f ? 0 : 1;
        }

        private void MaybeFinishEarly()
        {
            if (!_climbers.Values.Any(c => c.Alive && !c.Finished)) Finish();
        }

        public void Tick(float dt)
        {
            if (_done) return;
            _timeLeft = Math.Max(0f, _timeLeft - dt);

            foreach (var c in _climbers.Values)
            {
                if (c.RollCdT > 0f) c.RollCdT = Math.Max(0f, c.RollCdT - dt);
                if (c.DieShowT > 0f) c.DieShowT = Math.Max(0f, c.DieShowT - dt);
                if (!c.Alive || c.Finished) continue;
                if (c.Choosing >= 0)
                {
                    if (!c.IsBot) continue;
                    c.BotThink -= dt;
                    if (c.BotThink <= 0f) Choose(c.Id, PickSide(c.Choosing));
                    continue;
                }
                if (c.IsBot && c.RollCdT <= 0f) { Roll(c.Id); c.RollCdT = c.BotCadence; }
            }

            if (_timeLeft <= 0f || !_climbers.Values.Any(c => c.Alive && !c.Finished)) Finish();
        }

        private void Finish()
        {
            if (_done) return;
            var finishers = _climbers.Values.Where(c => c.Alive && c.Finished).ToList();
            var strag = _climbers.Values.Where(c => c.Alive && !c.Finished).OrderBy(Standing).ToList();
            int spare = (int)Math.Round(strag.Count * (1f - _ctx.Intensity) * 0.5f);
            if (finishers.Count == 0) spare = Math.Max(spare, 1);
            int cut = Math.Max(0, strag.Count - spare);
            for (int i = 0; i < cut; i++)
            {
                var c = strag[i];
                c.Alive = false; c.Choosing = -1; SyncActor(c.Id, false);
                string note = c.Square <= 0 ? "Never left the start!" : $"Didn't reach safety — stuck at {c.Square}";
                _elim.Add((c.Id, note));
            }
            _done = true;
        }

        private float Standing(Climber c) => c.Finished ? 1_000_000 - c.FinishRank : c.Square * 1000 - c.Rolls;

        public void Forfeit(string playerId)
        {
            if (!_climbers.TryGetValue(playerId, out var c) || !c.Alive) return;
            c.Alive = false; c.Finished = false; c.Choosing = -1; SyncActor(playerId, false);
            _elim.Add((playerId, "Rage-quit the board"));
            MaybeFinishEarly();
        }

        private void SyncActor(string id, bool alive)
        {
            var a = _ctx.Actors.FirstOrDefault(x => x.Id == id);
            if (a != null) a.Alive = alive;
        }

        public RoundResult Result()
        {
            var survivorsSorted = _climbers.Values.Where(c => c.Alive).OrderByDescending(Standing).ToList();
            var res = new RoundResult { Game = Id };
            int place = 1;
            foreach (var c in survivorsSorted)
            {
                res.Ranking.Add(new RankEntry(c.Id, place++, true, c.Finished ? "Reached the top!" : $"Hung on at square {c.Square}"));
                res.SurvivorIds.Add(c.Id);
            }
            for (int i = _elim.Count - 1; i >= 0; i--)
                res.Ranking.Add(new RankEntry(_elim[i].id, place++, false, _elim[i].note));
            return res;
        }

        public Snapshot BuildSnapshot()
        {
            var fx = _fx.Count > 0 ? new List<Effect>(_fx) : null;
            _fx.Clear();
            return new Snapshot
            {
                Game = Id, T = (_duration - _timeLeft) * 1000.0, Actors = _ctx.Actors, Fx = fx,
                Data = new ChutesData
                {
                    Goal = Goal, Cols = Cols,
                    Ladders = _ladders.Select(l => new int[] { l.From, l.To }).ToList(),
                    Chutes = _chutes.Select(c => new ChuteView
                    {
                        Id = c.Id, Square = c.Square,
                        Left = c.Revealed[0] ? (c.DeathSide == 0 ? 1 : 0) : -1,
                        Right = c.Revealed[1] ? (c.DeathSide == 1 ? 1 : 0) : -1
                    }).ToList(),
                    TimeLeft = _timeLeft, Duration = _duration,
                    Climbers = _climbers.Values.Select(c => new ClimberView
                    {
                        Id = c.Id, Square = c.Square, Alive = c.Alive, Finished = c.Finished,
                        Die = c.DieShowT > 0f ? c.Die : 0, Choosing = c.Choosing
                    }).ToList()
                }
            };
        }

        public sealed class ChutesData
        {
            public int Goal, Cols;
            public List<int[]> Ladders;
            public List<ChuteView> Chutes;
            public float TimeLeft, Duration;
            public List<ClimberView> Climbers;
        }
        public struct ChuteView { public int Id, Square, Left, Right; }
        public struct ClimberView { public string Id; public int Square; public bool Alive, Finished; public int Die, Choosing; }
    }
}
