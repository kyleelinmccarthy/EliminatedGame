using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Killer Jump Rope — a giant rope sweeps a bridge; a clean jump on each swing
    /// carries you one plank toward the far side, a mistimed one sweeps you off.
    /// The rope only speeds up. Reach the far side to be safe. Ported from
    /// lib/server/games/JumpRope.ts. Finale-capable.
    /// </summary>
    public sealed class JumpRope : IMinigame
    {
        private const float JumpDurMs = 460f;
        private const float StartPeriod = 1.7f;
        private const float MinPeriod = 0.62f;
        private const float Speedup = 0.945f;
        private const int MaxSwingsCap = 30;

        private sealed class Jumper
        {
            public string Id;
            public bool IsBot;
            public bool Alive = true;
            public float AirborneUntil;
            public float BotLead;
            public float BotSkill;
            public int Pos;
            public bool Crossed;
            public int CrossedAt;
        }

        private readonly GameContext _ctx;
        private readonly Rng _rng;
        private readonly Dictionary<string, Jumper> _jumpers = new Dictionary<string, Jumper>();
        private readonly List<(string id, string note)> _elim = new List<(string, string)>();
        private readonly List<Effect> _fx = new List<Effect>();

        private float _phase;
        private float _period = StartPeriod;
        private int _swing;
        private float _nowMs;
        private bool _done;
        private int _graceSwings = 1;
        private int _maxSwings = MaxSwingsCap;
        private int _target = 1;
        private int _bridgeLen = 12;

        public JumpRope(GameContext ctx) { _ctx = ctx; _rng = ctx.Rng; }

        public GameId Id => GameId.JumpRope;
        public bool IsDone => _done;

        // Inspection (view + tests)
        public int Swing => _swing;
        public int BridgeLen => _bridgeLen;
        public bool Crossed(string id) => _jumpers.TryGetValue(id, out var j) && j.Crossed;
        public int PosOf(string id) => _jumpers.TryGetValue(id, out var j) ? j.Pos : 0;

        public void Start()
        {
            int n = _ctx.Actors.Count;
            foreach (var a in _ctx.Actors)
                _jumpers[a.Id] = new Jumper
                {
                    Id = a.Id, IsBot = a.IsBot,
                    BotLead = 0.2f + _rng.NextFloat() * 0.06f,
                    BotSkill = 0.02f + _rng.NextFloat() * 0.07f
                };
            _target = _ctx.ForceSingleSurvivor ? 1 : Math.Max(1, (int)Math.Ceiling(n * (1f - 0.55f * _ctx.Intensity)));
            _maxSwings = _ctx.ForceSingleSurvivor ? Math.Max(28, n * 5) : (int)Math.Round(8f + _ctx.Intensity * 22f);
            _graceSwings = _ctx.Intensity < 0.4f ? 2 : 1;
            _bridgeLen = Math.Max(8, (int)Math.Round(_maxSwings * 0.75f));

            int spread = Math.Max(2, Math.Min(Math.Min(6, _bridgeLen - 2), (int)Math.Round(_bridgeLen * 0.3f)));
            var lineup = _rng.Shuffle(_jumpers.Values.ToList());
            int denom = Math.Max(1, lineup.Count - 1);
            for (int i = 0; i < lineup.Count; i++)
                lineup[i].Pos = (int)Math.Round((i / (float)denom) * spread);
        }

        public void OnInput(string playerId, GameInput input)
        {
            bool isJump = input.Kind == InputKind.Tap || (input.Kind == InputKind.Action && input.Name == "jump");
            if (isJump) Jump(playerId);
        }

        private void Jump(string id)
        {
            if (!_jumpers.TryGetValue(id, out var j) || !j.Alive || j.Crossed) return;
            if (_nowMs < j.AirborneUntil) return;
            j.AirborneUntil = _nowMs + JumpDurMs;
        }

        public void Tick(float dt)
        {
            if (_done) return;
            _nowMs += dt * 1000f;

            foreach (var j in _jumpers.Values)
            {
                if (!j.Alive || j.Crossed || !j.IsBot) continue;
                if (_nowMs < j.AirborneUntil) continue;
                float timeToGround = (1f - _phase) * _period;
                float target = j.BotLead + (_rng.NextFloat() - 0.5f) * 2f * j.BotSkill * (StartPeriod / _period);
                if (timeToGround <= target) Jump(j.Id);
            }

            _phase += dt / _period;
            if (_phase >= 1f) { _phase -= 1f; GroundPass(); }
        }

        private void GroundPass()
        {
            _swing++;
            _period = Math.Max(MinPeriod, _period * Speedup);
            bool free = _swing <= _graceSwings;
            var onDeck = _jumpers.Values.Where(j => j.Alive && !j.Crossed).ToList();
            foreach (var j in onDeck)
            {
                bool airborne = _nowMs < j.AirborneUntil;
                if (airborne)
                {
                    j.Pos += 1;
                    if (j.Pos >= _bridgeLen)
                    {
                        j.Crossed = true;
                        j.CrossedAt = _swing;
                        _fx.Add(new Effect(EffectKind.Confetti));
                    }
                }
                else if (!free)
                {
                    j.Alive = false;
                    SyncActor(j.Id, false);
                    _elim.Add((j.Id, $"Swept off the bridge on plank {j.Pos + 1}"));
                    _fx.Add(new Effect(EffectKind.Death));
                }
            }
            _fx.Add(new Effect(EffectKind.Ring));

            var aliveAll = _jumpers.Values.Where(j => j.Alive).ToList();
            int stillCrossing = aliveAll.Count(j => !j.Crossed);
            if (aliveAll.Count <= _target || aliveAll.Count <= 1 || stillCrossing == 0 || _swing >= _maxSwings)
                _done = true;
        }

        public void Forfeit(string playerId)
        {
            if (!_jumpers.TryGetValue(playerId, out var j) || !j.Alive) return;
            j.Alive = false;
            SyncActor(playerId, false);
            _elim.Add((playerId, $"Bailed off the bridge on plank {j.Pos + 1}"));
            var aliveAll = _jumpers.Values.Where(x => x.Alive).ToList();
            int stillCrossing = aliveAll.Count(x => !x.Crossed);
            if (aliveAll.Count <= _target || aliveAll.Count <= 1 || stillCrossing == 0) _done = true;
        }

        private void SyncActor(string id, bool alive)
        {
            var a = _ctx.Actors.FirstOrDefault(x => x.Id == id);
            if (a != null) a.Alive = alive;
        }

        public RoundResult Result()
        {
            var survivors = _jumpers.Values
                .Where(j => j.Alive)
                .OrderBy(j => j.Crossed ? 0 : 1)
                .ThenBy(j => j.Crossed ? j.CrossedAt : int.MaxValue)
                .ThenByDescending(j => j.Pos)
                .Select(j => j.Id)
                .ToList();
            foreach (var j in _jumpers.Values) SyncActor(j.Id, j.Alive);
            return RankingUtil.Crown(Id, survivors, _elim, _ctx.ForceSingleSurvivor, "Still on the bridge at the buzzer");
        }

        public Snapshot BuildSnapshot()
        {
            var fx = _fx.Count > 0 ? new List<Effect>(_fx) : null;
            _fx.Clear();
            return new Snapshot
            {
                Game = Id,
                T = _nowMs,
                Actors = _ctx.Actors,
                Fx = fx,
                Data = new RopeData
                {
                    Phase = _phase,
                    Period = _period,
                    Swing = _swing,
                    BridgeLen = _bridgeLen,
                    Grace = Math.Max(0, _graceSwings - _swing),
                    Jumpers = _jumpers.Values.Select(j => new JumperView
                    {
                        Id = j.Id, Alive = j.Alive, Airborne = _nowMs < j.AirborneUntil, Pos = j.Pos, Crossed = j.Crossed
                    }).ToList()
                }
            };
        }

        public sealed class RopeData
        {
            public float Phase, Period;
            public int Swing, BridgeLen, Grace;
            public List<JumperView> Jumpers;
        }
        public struct JumperView { public string Id; public bool Alive, Airborne; public int Pos; public bool Crossed; }
    }
}
