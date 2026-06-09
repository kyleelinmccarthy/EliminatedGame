using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Two teams, one rope, a pit on each side. Mash to pull; the team dragged
    /// over the edge plummets. Headcount helps with diminishing returns (√count
    /// Lanchester scaling), and a real player heaves harder than a bot. Ported
    /// from lib/server/games/TugOfWar.ts. Not an arena game (no movement).
    /// </summary>
    public sealed class TugOfWar : IMinigame
    {
        private const float Win = 1.0f;
        private const float TimeLimit = 30f;
        private const float TapImpulse = 1.0f;   // per bot tap
        private const float HumanImpulse = 1.25f; // a human pulls harder
        private const float Decay = 0.8f;         // per tick
        private const float Speed = 0.05f;
        private const int MaxTapsPerSec = 14;     // anti-macro cap

        private sealed class Puller
        {
            public string Id;
            public bool IsBot;
            public int Team;
            public int Taps;
            public float BotAccum;
            public float BotRate; // taps/sec
            public int TapWindow; // taps used this second
        }

        private readonly GameContext _ctx;
        private readonly Rng _rng;
        private readonly Dictionary<string, Puller> _pullers = new Dictionary<string, Puller>();
        private readonly float[] _force = { 0f, 0f };
        private readonly List<Effect> _fx = new List<Effect>();

        private float _ropePos;     // −1 (team1 wins) .. +1 (team0 wins)
        private float _elapsed;
        private float _secondTimer;
        private bool _done;
        private int _loserTeam = -1;

        public TugOfWar(GameContext ctx)
        {
            _ctx = ctx;
            _rng = ctx.Rng;
        }

        public GameId Id => GameId.TugOfWar;
        public bool IsDone => _done;

        // ── Inspection (used by the view and tests) ──────────────────────
        public float RopePos => _ropePos;
        public int LoserTeam => _loserTeam;
        public int TeamOf(string id) => _pullers.TryGetValue(id, out var p) ? p.Team : -1;
        public int TapsFor(string id) => _pullers.TryGetValue(id, out var p) ? p.Taps : 0;
        public int TeamCount(int team) => _pullers.Values.Count(p => p.Team == team);

        public void Start()
        {
            var shuffled = _rng.Shuffle(_ctx.Actors);
            for (int i = 0; i < shuffled.Count; i++)
            {
                var a = shuffled[i];
                int team = i % 2;
                a.Team = team;
                _pullers[a.Id] = new Puller
                {
                    Id = a.Id,
                    IsBot = a.IsBot,
                    Team = team,
                    BotRate = 5f + _rng.NextFloat() * 2f // 5–7 taps/sec
                };
            }
        }

        public void OnInput(string playerId, GameInput input)
        {
            bool isPull = input.Kind == InputKind.Tap ||
                          (input.Kind == InputKind.Action && input.Name == "pull");
            if (!isPull) return;
            if (!_pullers.TryGetValue(playerId, out var p)) return;
            if (p.TapWindow >= MaxTapsPerSec) return; // capped this second
            p.TapWindow++;
            p.Taps++;
            _force[p.Team] += p.IsBot ? TapImpulse : HumanImpulse;
        }

        public void Tick(float dt)
        {
            if (_done) return;
            _elapsed += dt;
            _secondTimer += dt;
            if (_secondTimer >= 1f)
            {
                _secondTimer = 0f;
                foreach (var p in _pullers.Values) p.TapWindow = 0;
            }

            // Bots tap on their own.
            foreach (var p in _pullers.Values)
            {
                if (!p.IsBot) continue;
                p.BotAccum += p.BotRate * dt;
                while (p.BotAccum >= 1f)
                {
                    p.BotAccum -= 1f;
                    if (p.TapWindow < MaxTapsPerSec)
                    {
                        p.TapWindow++;
                        p.Taps++;
                        _force[p.Team] += TapImpulse;
                    }
                }
            }

            int c0 = TeamCount(0), c1 = TeamCount(1);
            float norm0 = c0 > 0 ? 1f / (float)Math.Sqrt(c0) : 0f;
            float norm1 = c1 > 0 ? 1f / (float)Math.Sqrt(c1) : 0f;
            float net = _force[0] * norm0 - _force[1] * norm1;
            _ropePos += net * Speed * dt * 20f;
            _ropePos = MathUtil.Clamp(_ropePos, -1.4f, 1.4f);
            _force[0] *= Decay;
            _force[1] *= Decay;

            if (_ropePos >= Win) Finish(1);
            else if (_ropePos <= -Win) Finish(0);
            else if (_elapsed >= TimeLimit) Finish(_ropePos >= 0 ? 1 : 0);
        }

        private void Finish(int loserTeam)
        {
            _loserTeam = loserTeam;
            _done = true;
            _fx.Add(new Effect(EffectKind.Death, loserTeam == 0 ? -1f : 1f, 0f));
        }

        public void Forfeit(string playerId)
        {
            if (!_pullers.TryGetValue(playerId, out var p)) return;
            _pullers.Remove(playerId);
            var a = _ctx.Actors.FirstOrDefault(x => x.Id == playerId);
            if (a != null) { a.Alive = false; a.Team = -1; }
            _fx.Add(new Effect(EffectKind.Death, p.Team == 0 ? -1f : 1f, 0f));
        }

        public RoundResult Result()
        {
            var all = _pullers.Values.ToList();
            var winners = all.Where(p => p.Team != _loserTeam)
                             .OrderByDescending(p => p.Taps).ToList();
            var losers = all.Where(p => p.Team == _loserTeam)
                            .OrderByDescending(p => p.Taps).ToList();

            var res = new RoundResult { Game = Id };
            int place = 1;
            foreach (var p in winners)
            {
                res.Ranking.Add(new RankEntry(p.Id, place++, true));
                SetAlive(p.Id, true);
            }
            foreach (var p in losers)
            {
                res.Ranking.Add(new RankEntry(p.Id, place++, false, "Pulled into the pit"));
                SetAlive(p.Id, false);
            }
            res.SurvivorIds = winners.Select(p => p.Id).ToList();
            return res;
        }

        private void SetAlive(string id, bool alive)
        {
            var a = _ctx.Actors.FirstOrDefault(x => x.Id == id);
            if (a != null) a.Alive = alive;
        }

        public Snapshot BuildSnapshot()
        {
            List<Effect> fx = _fx.Count > 0 ? new List<Effect>(_fx) : null;
            _fx.Clear();
            return new Snapshot
            {
                Game = Id,
                T = _elapsed * 1000.0,
                Actors = _ctx.Actors,
                Fx = fx,
                Data = new TugData
                {
                    RopePos = _ropePos,
                    TimeLeft = Math.Max(0f, TimeLimit - _elapsed),
                    LoserTeam = _loserTeam
                }
            };
        }

        /// <summary>Per-tick payload the client renders (rope position, clock).</summary>
        public sealed class TugData
        {
            public float RopePos;
            public float TimeLeft;
            public int LoserTeam;
        }
    }
}
