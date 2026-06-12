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

        private const float FallDur = 1.5f; // the losing team is hauled over the edge before the round ends

        private float _ropePos;     // −1 (team1 wins) .. +1 (team0 wins)
        private float _elapsed;
        private float _secondTimer;
        private bool _done;
        private int _loserTeam = -1;
        private bool _falling;      // result decided; playing the haul-into-the-pit beat
        private float _fallT;

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
        /// <summary>0→1 progress of the "haul the losers into the pit" beat once the result is locked.</summary>
        public float FallProgress => _falling ? MathUtil.Clamp01(_fallT / FallDur) : (_done ? 1f : 0f);
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

            // Result locked: hold the rope at the winner's side and play the haul-into-the-pit
            // beat. Only after the losers are over the edge does the round actually end, so the
            // plunge is visible in the live arena rather than hidden under the results overlay.
            if (_falling)
            {
                _fallT += dt;
                if (_fallT >= FallDur) _done = true;
                return;
            }

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

            if (_ropePos >= Win) BeginFall(1);
            else if (_ropePos <= -Win) BeginFall(0);
            else if (_elapsed >= TimeLimit) BeginFall(_ropePos >= 0 ? 1 : 0);
        }

        // Lock the result and start the pit-fall beat: snap the rope fully to the winner's
        // side (so the knot finishes its slide) and flag the loser. _done follows in Tick
        // once FallDur elapses.
        private void BeginFall(int loserTeam)
        {
            if (_falling) return;
            _loserTeam = loserTeam;
            _falling = true;
            _fallT = 0f;
            _ropePos = loserTeam == 1 ? Win : -Win;
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

        // Arrange the two teams either side of the rope so the top-down view
        // shows a real tug: team 0 on the left, team 1 on the right, the whole
        // knot sliding toward the winning side as the rope is dragged over.
        private void Layout()
        {
            // The rope slides toward the WINNING team, dragging the loser to the
            // central pit (as in the web: the knot is pulled to the winner's side).
            // +rope = team 0 (left) winning → mid slides LEFT toward team 0, so
            // team 1 on the right gets hauled in. The view rope + HUD knob use the
            // same sign so player formation, rope prop, and knob all agree.
            float mid = Stage.CenterX - MathUtil.Clamp(_ropePos, -1f, 1f) * 220f;
            var team0 = new List<Actor>();
            var team1 = new List<Actor>();
            foreach (var a in _ctx.Actors)
            {
                if (!_pullers.TryGetValue(a.Id, out var p)) continue;
                (p.Team == 0 ? team0 : team1).Add(a);
            }
            LayoutTeam(team0, mid, -1);
            LayoutTeam(team1, mid, +1);
        }

        // dir −1 = the team standing left of the rope, +1 = right of it.
        private static void LayoutTeam(List<Actor> team, float mid, int dir)
        {
            const float laneGap = 96f;   // spacing across the rope
            const float rankGap = 86f;   // spacing back from the rope
            const int lanes = 4;
            for (int i = 0; i < team.Count; i++)
            {
                int lane = i % lanes;
                int rank = i / lanes;
                int used = Math.Min(lanes, team.Count - rank * lanes);
                float y = Stage.CenterY + (lane - (used - 1) * 0.5f) * laneGap;
                float x = mid + dir * (74f + rank * rankGap);
                team[i].Pos = Stage.Clamp(x, y);
                team[i].Facing = dir > 0 ? (float)Math.PI : 0f; // lean into the rope
            }
        }

        public Snapshot BuildSnapshot()
        {
            Layout();
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
                    LoserTeam = _loserTeam,
                    FallT = FallProgress,
                    Team0Count = TeamCount(0),
                    Team1Count = TeamCount(1)
                }
            };
        }

        /// <summary>Per-tick payload the client renders (rope position, clock).</summary>
        public sealed class TugData
        {
            public float RopePos;
            public float TimeLeft;
            public int LoserTeam;
            public float FallT; // 0→1 haul-into-the-pit progress (the view drags + drops the losers)
            public int Team0Count, Team1Count; // current puller headcount per side (HUD labels)
        }
    }
}
