using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Secret Santa Sabotage — lights out, hidden "givers" each slip a gift to a
    /// player of their choosing; lights on, every receiver must guess who gave it.
    /// Right → the giver is caught (out); wrong → the receiver takes the fall.
    /// Givers/receivers are disjoint so the cull is exactly bounded. Ported from
    /// lib/server/games/PresentSwap.ts.
    /// </summary>
    public sealed class PresentSwap : IMinigame
    {
        private const float Gift = 8f;
        private const float Guess = 11f;
        private const float Reveal = 4.2f;
        private const float RingR = 250f;
        private const int Slate = 4;

        private sealed class Seat
        {
            public string Id;
            public bool IsBot;
            public bool Alive = true;
            public float X, Y;
        }

        private sealed class GiftEvent
        {
            public string GiverId;
            public List<string> TargetSlate;
            public string TargetId;
            public float BotGiftAt;
            public string ReceiverId = "";
            public List<string> CandidateIds = new List<string>();
            public string GuessId;
            public float BotGuessAt;
            public string ResultKind; // "caught" / "fooled"
            public bool Correct;
        }

        private readonly GameContext _ctx;
        private readonly Rng _rng;
        private readonly Dictionary<string, Seat> _seats = new Dictionary<string, Seat>();
        private readonly List<string> _order = new List<string>();
        private List<GiftEvent> _events = new List<GiftEvent>();
        private readonly List<(string id, string note)> _elim = new List<(string, string)>();
        private readonly List<Effect> _fx = new List<Effect>();

        private string _phase = "gift";
        private float _timer = Gift;
        private float _elapsed;
        private int _round;
        private int _maxRounds = 1;
        private int _targetSurvivors = 2;
        private int _startCount;
        private bool _done;

        public PresentSwap(GameContext ctx) { _ctx = ctx; _rng = ctx.Rng; }

        public GameId Id => GameId.PresentSwap;
        public bool IsDone => _done;

        // Inspection (view + tests)
        public string Phase => _phase;
        public IReadOnlyList<string> GiverIds => _events.Select(e => e.GiverId).ToList();
        public string ReceiverOf(string giverId) => _events.FirstOrDefault(e => e.GiverId == giverId)?.ReceiverId;
        public string GiverOf(string receiverId) => _events.FirstOrDefault(e => e.ReceiverId == receiverId)?.GiverId;
        public IReadOnlyList<string> CandidatesFor(string receiverId) =>
            _events.FirstOrDefault(e => e.ReceiverId == receiverId)?.CandidateIds ?? new List<string>();

        public void Start()
        {
            _startCount = _ctx.Actors.Count;
            for (int i = 0; i < _ctx.Actors.Count; i++)
            {
                var a = _ctx.Actors[i];
                _order.Add(a.Id);
                float ang = (i / (float)_ctx.Actors.Count) * 6.2831853f - (float)Math.PI / 2f;
                _seats[a.Id] = new Seat
                {
                    Id = a.Id, IsBot = a.IsBot,
                    X = Constants.ArenaW / 2f + (float)Math.Cos(ang) * RingR * 1.6f,
                    Y = Constants.ArenaH / 2f + (float)Math.Sin(ang) * RingR
                };
            }
            _maxRounds = _ctx.Intensity < 0.5f ? 1 : 2;
            _targetSurvivors = Math.Max(2, (int)Math.Ceiling(_startCount * (1f - 0.45f * _ctx.Intensity)));
            BeginRound();
        }

        private List<string> AliveIds() => _order.Where(id => _seats[id].Alive).ToList();

        private void BeginRound()
        {
            _round++;
            var alive = _rng.Shuffle(AliveIds());
            int k = MathUtil.Clamp((int)Math.Ceiling(alive.Count * 0.25f * (0.6f + _ctx.Intensity)), 1, alive.Count / 2);
            var givers = alive.Take(k).ToList();
            var pool = alive.Skip(k).ToList();
            _events = givers.Select(giverId => new GiftEvent
            {
                GiverId = giverId,
                TargetSlate = _rng.Shuffle(pool).Take(Math.Min(Slate, pool.Count)).ToList(),
                BotGiftAt = 1.2f + _rng.NextFloat() * (Gift - 3f),
                BotGuessAt = 1.5f + _rng.NextFloat() * (Guess - 4f)
            }).ToList();
            _phase = "gift";
            _timer = Gift;
            _elapsed = 0f;
        }

        private List<string> Candidates(string giverId, string receiverId)
        {
            var others = AliveIds().Where(id => id != receiverId).ToList();
            var decoyPool = others.Where(id => id != giverId).ToList();
            int count = Math.Min(4, others.Count);
            var decoys = _rng.Shuffle(decoyPool).Take(Math.Max(0, count - 1)).ToList();
            var list = new List<string> { giverId };
            list.AddRange(decoys);
            return _rng.Shuffle(list);
        }

        private void FinalizeGifts()
        {
            var claimed = new HashSet<string>();
            var poolAll = _rng.Shuffle(_events.SelectMany(e => e.TargetSlate).Distinct().ToList());
            var live = new List<GiftEvent>();
            foreach (var ev in _events)
            {
                string target = ev.TargetId;
                if (target == null || claimed.Contains(target))
                    target = ev.TargetSlate.FirstOrDefault(id => !claimed.Contains(id))
                             ?? poolAll.FirstOrDefault(id => !claimed.Contains(id));
                if (target == null) continue;
                ev.TargetId = target;
                ev.ReceiverId = target;
                claimed.Add(target);
                ev.CandidateIds = Candidates(ev.GiverId, ev.ReceiverId);
                live.Add(ev);
            }
            _events = live;
            _phase = "guess";
            _timer = Guess;
            _elapsed = 0f;
        }

        public void OnInput(string playerId, GameInput input)
        {
            if (input.Kind != InputKind.Choose) return;
            if (_phase == "gift")
            {
                var ev = _events.FirstOrDefault(e => e.GiverId == playerId);
                if (ev == null || ev.TargetId != null) return;
                if (ev.TargetSlate.Contains(input.Value)) ev.TargetId = input.Value;
            }
            else if (_phase == "guess")
            {
                var ev = _events.FirstOrDefault(e => e.ReceiverId == playerId);
                if (ev == null || ev.GuessId != null) return;
                if (ev.CandidateIds.Contains(input.Value)) ev.GuessId = input.Value;
            }
        }

        public void Tick(float dt)
        {
            if (_done) return;
            _timer -= dt;
            _elapsed += dt;

            if (_phase == "gift")
            {
                foreach (var ev in _events)
                {
                    if (ev.TargetId != null) continue;
                    if (_seats[ev.GiverId].IsBot && _elapsed >= ev.BotGiftAt && ev.TargetSlate.Count > 0)
                        ev.TargetId = ev.TargetSlate[_rng.NextInt(ev.TargetSlate.Count)];
                }
                if (_timer <= 0f || _events.All(e => e.TargetId != null)) FinalizeGifts();
                return;
            }
            if (_phase == "guess")
            {
                foreach (var ev in _events)
                {
                    if (ev.GuessId != null) continue;
                    if (_seats[ev.ReceiverId].IsBot && _elapsed >= ev.BotGuessAt)
                        ev.GuessId = ev.CandidateIds[_rng.NextInt(ev.CandidateIds.Count)];
                }
                if (_timer <= 0f || _events.All(e => e.GuessId != null)) Resolve();
                return;
            }
            if (_phase == "reveal" && _timer <= 0f) AfterReveal();
        }

        private void Resolve()
        {
            foreach (var ev in _events)
            {
                bool correct = ev.GuessId != null && ev.GuessId == ev.GiverId;
                ev.Correct = correct;
                ev.ResultKind = correct ? "caught" : "fooled";
                string victimId = correct ? ev.GiverId : ev.ReceiverId;
                var victim = _seats[victimId];
                victim.Alive = false;
                SyncActor(victimId, false);
                _elim.Add((victimId, correct ? "Caught gifting!" : "Fooled by the gift!"));
                _fx.Add(new Effect(EffectKind.Death, victim.X, victim.Y));
            }
            _phase = "reveal";
            _timer = Reveal;
            _elapsed = 0f;
        }

        private void AfterReveal()
        {
            int alive = AliveIds().Count;
            if (alive <= _targetSurvivors || alive <= 2 || _round >= _maxRounds) _done = true;
            else BeginRound();
        }

        public void Forfeit(string playerId)
        {
            if (!_seats.TryGetValue(playerId, out var s) || !s.Alive) return;
            s.Alive = false;
            SyncActor(playerId, false);
            _events = _events.Where(e => e.GiverId != playerId && e.ReceiverId != playerId).ToList();
            if (_phase == "gift")
                foreach (var e in _events)
                {
                    e.TargetSlate = e.TargetSlate.Where(id => id != playerId).ToList();
                    if (e.TargetId == playerId) e.TargetId = null;
                }
            _elim.Add((playerId, "Left the party early"));
        }

        private void SyncActor(string id, bool alive)
        {
            var a = _ctx.Actors.FirstOrDefault(x => x.Id == id);
            if (a != null) a.Alive = alive;
        }

        public RoundResult Result()
        {
            var survivors = AliveIds();
            return RankingUtil.Build(Id, survivors, _elim);
        }

        // Seat every player at its assigned spot in the party ring (computed in
        // Start). Without this the top-down view would pile them all at the
        // origin; the ring is also where the gift/guess FX are anchored.
        private void Layout()
        {
            foreach (var a in _ctx.Actors)
                if (_seats.TryGetValue(a.Id, out var s))
                    a.Pos = Stage.Clamp(s.X, s.Y);
        }

        public Snapshot BuildSnapshot()
        {
            Layout();
            var fx = _fx.Count > 0 ? new List<Effect>(_fx) : null;
            _fx.Clear();

            // Per-giver secret info, folded per player by the room/transport.
            Dictionary<string, object> secrets = null;
            if (_phase == "gift" || _phase == "guess")
            {
                secrets = new Dictionary<string, object>();
                foreach (var e in _events)
                    secrets[e.GiverId] = _phase == "gift"
                        ? (object)new { role = "giver", targetSlate = e.TargetSlate, targetId = e.TargetId }
                        : new { role = "giver", gaveToId = e.ReceiverId };
            }

            return new Snapshot
            {
                Game = Id,
                T = _elapsed * 1000.0,
                Actors = _ctx.Actors,
                Fx = fx,
                Secrets = secrets,
                Data = new PresentData
                {
                    Phase = _phase,
                    Round = _round,
                    TimeLeft = Math.Max(0f, _timer),
                    DarkProgress = _phase == "gift" ? MathUtil.Clamp01(_elapsed / Gift) : 0f,
                    Gifts = _events.Count,
                    Placed = _events.Count(e => e.TargetId != null),
                    Events = _phase == "reveal"
                        ? _events.Select(e => new EventView { ReceiverId = e.ReceiverId, GiverId = e.GiverId, GuessId = e.GuessId, Result = e.ResultKind, Correct = e.Correct }).ToList()
                        : (_phase == "guess"
                            // receivers get their suspect slate; givers get who they gave to (GiverId+ReceiverId)
                            ? _events.Select(e => new EventView { ReceiverId = e.ReceiverId, GiverId = e.GiverId, CandidateIds = e.CandidateIds, Guessed = e.GuessId != null }).ToList()
                            // GIFT phase: hand each giver their target slate so a HUMAN can pick a victim
                            : (_phase == "gift"
                                ? _events.Select(e => new EventView { GiverId = e.GiverId, TargetSlate = e.TargetSlate, Gifted = e.TargetId != null }).ToList()
                                : new List<EventView>()))
                }
            };
        }

        public sealed class PresentData
        {
            public string Phase;
            public int Round;
            public float TimeLeft;
            public float DarkProgress;
            public int Gifts, Placed;
            public List<EventView> Events;
        }
        public sealed class EventView
        {
            public string ReceiverId, GiverId, GuessId, Result;
            public List<string> CandidateIds;
            public List<string> TargetSlate; // gift phase: the giver's pickable victims
            public bool Guessed, Correct, Gifted;
        }
    }
}
