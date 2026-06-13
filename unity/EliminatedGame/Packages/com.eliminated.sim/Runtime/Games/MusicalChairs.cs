using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Powerups;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Musical Chairs — keep moving while the music plays (freeze and the floor
    /// claims you), then scramble for a scatter of chairs the instant it stops.
    /// The DJ throws fake-out "STOP!"s. Chairs vanish each round. Ported from
    /// lib/server/games/MusicalChairs.ts.
    /// </summary>
    public sealed class MusicalChairs : ArenaGame
    {
        public enum McPhase { Music, Scramble, Eval }

        private const float ChairR = 46f;
        private const float ChairGap = 96f;
        private const float ChairMargin = 150f;
        private const float StillGrace = 1.4f;
        private const float StillWarn = 0.5f;
        private const float StillSpeed = 40f;
        private const float FakeShow = 0.45f;

        private sealed class Chair { public float X, Y; public string By; }

        private readonly List<Chair> _chairs = new List<Chair>();
        private readonly PowerupField _powerups;
        private McPhase _phase = McPhase.Music;
        private float _timer;
        private int _round;
        private int _maxRounds = 2;
        private float _fakeT, _fakeCd;
        private bool _done;

        public MusicalChairs(GameContext ctx) : base(ctx)
        {
            _powerups = new PowerupField(ctx.Rng, every: 2.2f, max: 5, goodWeight: 0.55f, margin: 120f, emit: Emit);
        }

        public override GameId Id => GameId.MusicalChairs;
        public override bool IsDone => _done;
        protected override float DashCooldown => 1.8f;
        public McPhase CurrentPhase => _phase;
        public int ChairCount => _chairs.Count;
        public bool Fake => _phase == McPhase.Music && _fakeT > 0f;

        public override void Start()
        {
            Elapsed = 0f;
            int n = Actors.Count;
            for (int i = 0; i < n; i++)
            {
                float ang = (i / (float)n) * 6.2831853f;
                Actors[i].Pos = new Vec2(
                    Constants.ArenaW / 2f + (float)Math.Cos(ang) * 330f,
                    Constants.ArenaH / 2f + (float)Math.Sin(ang) * 250f);
            }
            _maxRounds = Ctx.Intensity < 0.4f ? 1 : (Ctx.Intensity < 0.7f ? 2 : 3);
            BeginMusic();
        }

        private List<Actor> Alive => Actors.Where(a => a.Alive).ToList();

        private void BeginMusic()
        {
            _round++;
            _chairs.Clear();
            foreach (var a in Alive)
            {
                a.Set("seated", 0f);
                a.Set("stillT", -0.9f);
                if (a.IsBot)
                {
                    a.Set("wanderR", 130f + Rng.NextFloat() * 120f);
                    a.Set("spin", (0.5f + Rng.NextFloat() * 0.7f) * (Rng.NextFloat() < 0.5f ? 1f : -1f));
                    a.Set("skill", Rng.NextFloat());
                    a.Set("orbit", (float)Math.Atan2(a.Pos.Y - Constants.ArenaH / 2f, a.Pos.X - Constants.ArenaW / 2f));
                }
            }
            _phase = McPhase.Music;
            _timer = 3.5f + Rng.NextFloat() * 2.5f;
            _fakeT = 0f;
            _fakeCd = 1.1f + Rng.NextFloat();
        }

        private void ScatterChairs(int count)
        {
            _chairs.Clear();
            float x0 = ChairMargin, x1 = Constants.ArenaW - ChairMargin;
            float y0 = ChairMargin, y1 = Constants.ArenaH - ChairMargin;
            for (int i = 0; i < count; i++)
            {
                float bx = (x0 + x1) / 2f, by = (y0 + y1) / 2f, bestSep = -1f;
                for (int s = 0; s < 14; s++)
                {
                    float x = x0 + Rng.NextFloat() * (x1 - x0);
                    float y = y0 + Rng.NextFloat() * (y1 - y0);
                    float near = float.MaxValue;
                    foreach (var c in _chairs) near = Math.Min(near, Vec2.Distance(new Vec2(x, y), new Vec2(c.X, c.Y)));
                    if (_chairs.Count == 0) { bx = x; by = y; break; }
                    if (near > bestSep) { bestSep = near; bx = x; by = y; }
                    if (near >= ChairGap) break;
                }
                _chairs.Add(new Chair { X = bx, Y = by });
            }
        }

        private void BeginScramble()
        {
            _phase = McPhase.Scramble;
            _timer = 4f;
            int alive = Alive.Count;
            int remove = MathUtil.Clamp((int)Math.Ceiling(alive * 0.15f * (0.6f + Ctx.Intensity)), 1, Math.Max(1, alive / 2));
            int nChairs = Math.Max(1, alive - remove);
            ScatterChairs(nChairs);
            foreach (var a in Alive)
            {
                if (!a.IsBot) continue;
                float skill = a.Get("skill", Rng.NextFloat());
                a.Set("skill", skill);
                a.Set("reactT", 0.22f + (1f - skill) * 0.55f + Rng.NextFloat() * 0.25f);
                a.Set("targetChair", -1f);
            }
            Emit(new Effect(EffectKind.Ring, Constants.ArenaW / 2f, Constants.ArenaH / 2f, 3f));
        }

        private void Evaluate()
        {
            foreach (var a in Alive)
            {
                if (a.Get("seated") == 0f) Eliminate(a, "No chair!");
                else Emit(new Effect(EffectKind.Confetti, a.Pos.X, a.Pos.Y));
            }
            _phase = McPhase.Eval;
            _timer = 1.8f;
        }

        private void AfterEval()
        {
            if (Alive.Count <= 2 || _round >= _maxRounds) _done = true;
            else BeginMusic();
        }

        public override void Tick(float dt)
        {
            if (_done) return;
            Elapsed += dt;
            _timer -= dt;
            if (_phase == McPhase.Music) { _powerups.Tick(dt); RunFakeouts(dt); }

            foreach (var a in Actors)
            {
                if (!a.Alive) continue;
                UpdateStatus(a, dt);
                if (a.IsBot) BotThink(a, dt);
                MoveActor(a, dt); // handles dash + powerup speed/curses
                if (_phase == McPhase.Music)
                {
                    _powerups.Collect(a, Actors);
                    KeepMoving(a, dt);
                }
            }

            if (_phase == McPhase.Scramble) ClaimChairs();

            if (_phase == McPhase.Music && _timer <= 0f) BeginScramble();
            else if (_phase == McPhase.Scramble && (_timer <= 0f || AllSeated())) Evaluate();
            else if (_phase == McPhase.Eval && _timer <= 0f) AfterEval();
        }

        private void KeepMoving(Actor a, float dt)
        {
            bool moving = a.Vel.Length > StillSpeed;
            a.Set("stillT", moving ? 0f : a.Get("stillT") + dt);
            float t = a.Get("stillT");
            if (t >= StillGrace) Eliminate(a, "Stopped dancing!");
            else if (t >= StillWarn) a.Flash = 1f;
        }

        private void RunFakeouts(float dt)
        {
            if (_fakeT > 0f) _fakeT = Math.Max(0f, _fakeT - dt);
            _fakeCd -= dt;
            if (_fakeCd <= 0f && _fakeT <= 0f && _timer > 1.3f)
            {
                _fakeT = FakeShow;
                _fakeCd = 1.4f + Rng.NextFloat() * 1.6f;
                Emit(new Effect(EffectKind.Ring, Constants.ArenaW / 2f, Constants.ArenaH / 2f, 2f));
            }
        }

        private void ClaimChairs()
        {
            var order = Alive.OrderBy(a => a.IsBot ? 1 : 0).ToList(); // humans first
            foreach (var a in order)
            {
                if (a.Get("seated") != 0f) continue;
                if (a.IsBot && a.Get("reactT") > 0f) continue;
                foreach (var c in _chairs)
                {
                    if (c.By != null) continue;
                    if (Vec2.Distance(a.Pos, new Vec2(c.X, c.Y)) < ChairR)
                    {
                        c.By = a.Id;
                        a.Set("seated", 1f);
                        Emit(new Effect(EffectKind.Pickup, c.X, c.Y - 30f, 0f, "SAFE"));
                        break;
                    }
                }
            }
        }

        private bool AllSeated() => _chairs.All(c => c.By != null);

        private void BotThink(Actor a, float dt)
        {
            if (_phase == McPhase.Music)
            {
                a.Set("orbit", a.Get("orbit") + a.Get("spin", 0.6f) * dt);
                float r = a.Get("wanderR", 190f);
                float tx = Constants.ArenaW / 2f + (float)Math.Cos(a.Get("orbit")) * r;
                float ty = Constants.ArenaH / 2f + (float)Math.Sin(a.Get("orbit")) * r;
                var d = (new Vec2(tx, ty) - a.Pos).Normalized;
                a.InDx = d.X; a.InDy = d.Y;
                return;
            }
            if (a.Get("seated") != 0f) { a.InDx = 0f; a.InDy = 0f; return; }
            if (a.Get("reactT") > 0f) { a.Set("reactT", a.Get("reactT") - dt); a.InDx = 0f; a.InDy = 0f; return; }

            var target = ChairForBot(a);
            if (target != null)
            {
                var d = (new Vec2(target.X, target.Y) - a.Pos).Normalized;
                a.InDx = d.X; a.InDy = d.Y;
            }
            else { a.InDx = 0f; a.InDy = 0f; }
        }

        private Chair ChairForBot(Actor a)
        {
            int ti = (int)a.Get("targetChair", -1f);
            if (ti >= 0 && ti < _chairs.Count && _chairs[ti].By == null) return _chairs[ti];

            var open = _chairs
                .Select((c, i) => (c, i, d: Vec2.SqrDistance(a.Pos, new Vec2(c.X, c.Y))))
                .Where(o => o.c.By == null)
                .OrderBy(o => o.d)
                .ToList();
            if (open.Count == 0) { a.Set("targetChair", -1f); return null; }
            float skill = a.Get("skill", 0.7f);
            int span = skill > 0.66f ? 1 : (skill > 0.33f ? 2 : 3);
            var pick = open[Rng.NextInt(Math.Min(span, open.Count))];
            a.Set("targetChair", pick.i);
            return pick.c;
        }

        protected override object BuildData() => new McData
        {
            Phase = _phase.ToString(),
            Round = _round,
            TimeLeft = Math.Max(0f, _timer),
            Fake = Fake,
            Night = Ctx.Night,
            Chairs = _chairs.Select(c => new ChairView { X = c.X, Y = c.Y, Claimed = c.By != null }).ToList(),
            Pickups = _phase == McPhase.Music ? _powerups.Snapshot() : new List<PickupView>(),
            // Anyone who's been STILL too long during the music is in danger — the view floats a
            // "MOVE!" countdown over them (and the HUD a personal alarm) so the rule is unmissable.
            Warn = _phase == McPhase.Music
                ? Alive.Where(a => a.Get("stillT") >= StillWarn)
                       .Select(a => new WarnView { Id = a.Id, Left = Math.Max(0f, StillGrace - a.Get("stillT")) }).ToList()
                : new List<WarnView>()
        };

        public sealed class McData
        {
            public string Phase;
            public int Round;
            public float TimeLeft;
            public bool Fake;
            public bool Night;
            public List<ChairView> Chairs;
            public List<PickupView> Pickups;
            public List<WarnView> Warn; // alive players standing still too long (danger)
        }
        public struct ChairView { public float X, Y; public bool Claimed; }
        public struct WarnView { public string Id; public float Left; }
    }
}
