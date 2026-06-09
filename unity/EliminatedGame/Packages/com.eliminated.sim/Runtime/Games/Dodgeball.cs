using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Powerups;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Dodgeball — two teams split by a center line. Grab a ball, hurl it across;
    /// an enemy hit (no shield, not mid-dodge) is out. Ported from
    /// lib/server/games/Dodgeball.ts.
    /// </summary>
    public sealed class Dodgeball : ArenaGame
    {
        private const float TimeLimit = 45f;
        private const float BallSpeed = 640f;
        private const float BallHitR = 15f;
        private const float BallLife = 1.7f;
        private const float DashDur = 0.18f;
        private const float DashCd = 1.3f;
        private const float DashSpeed = 3.0f;
        private const float DivPad = 8f;

        private enum BallState { Ground, Held, Flight }

        private sealed class Ball
        {
            public int Id;
            public float X, Y, Vx, Vy;
            public BallState State;
            public string Holder;
            public int Team = -1;
            public float T;
        }

        private readonly List<Ball> _balls = new List<Ball>();
        private readonly PowerupField _powerups;
        private int _nextId = 1;
        private bool _done;

        private float Mid => Constants.ArenaW / 2f;

        public Dodgeball(GameContext ctx) : base(ctx)
        {
            _powerups = new PowerupField(ctx.Rng, every: 3f, max: 5, goodWeight: 0.58f, emit: Emit);
        }

        public override GameId Id => GameId.Dodgeball;
        public override bool IsDone => _done;
        public int BallCount => _balls.Count;

        public override void Start()
        {
            Elapsed = 0f;
            var shuffled = Rng.Shuffle(Actors);
            for (int i = 0; i < shuffled.Count; i++)
            {
                var a = shuffled[i];
                int team = i % 2;
                a.Team = team;
                float x = team == 0 ? Constants.ArenaW * 0.25f : Constants.ArenaW * 0.75f;
                a.Pos = new Vec2(x + (Rng.NextFloat() - 0.5f) * 120f, 140f + Rng.NextFloat() * (Constants.ArenaH - 280f));
                a.Facing = team == 0 ? 0f : (float)Math.PI;
                a.AimAngle = a.Facing;
                a.HasAim = true;
                a.Set("botCd", Rng.NextFloat());
            }
            int n = Math.Max(3, (int)Math.Ceiling(shuffled.Count / 2f));
            for (int i = 0; i < n; i++)
                _balls.Add(new Ball { Id = _nextId++, X = Mid, Y = Constants.ArenaH * (i + 1) / (n + 1), State = BallState.Ground });
        }

        public override void OnInput(string actorId, GameInput input)
        {
            var a = Find(actorId);
            if (a == null || !a.Alive) return;
            switch (input.Kind)
            {
                case InputKind.Move: a.InDx = input.Dx; a.InDy = input.Dy; break;
                case InputKind.Aim: a.AimAngle = input.Angle; a.HasAim = true; break;
                case InputKind.Action:
                    if (input.Name == "throw") a.Set("wantThrow", 1f);
                    else if (input.Name == "dash") a.Set("wantDash", 1f);
                    break;
            }
        }

        public override void Tick(float dt)
        {
            if (_done) return;
            Elapsed += dt;
            _powerups.Tick(dt);

            foreach (var a in Actors)
            {
                if (!a.Alive) continue;
                UpdateStatus(a, dt);
                a.Set("dashCd", Math.Max(0f, a.Get("dashCd") - dt));
                a.Set("invuln", Math.Max(0f, a.Get("invuln") - dt));
                if (a.IsBot) BotThink(a, dt);

                if (a.Get("dashT") > 0f)
                {
                    a.Set("dashT", a.Get("dashT") - dt);
                    a.InDx = a.Get("dashDx");
                    a.InDy = a.Get("dashDy");
                    MoveAt(a, dt, DashSpeed);
                }
                else
                {
                    MoveActor(a, dt); // applies powerup speed/curses
                }
                ClampSide(a);
                if (a.HasAim) a.Facing = a.AimAngle;

                if (a.Get("wantThrow") > 0f) { a.Set("wantThrow", 0f); DoThrow(a); }
                if (a.Get("wantDash") > 0f) { a.Set("wantDash", 0f); DoDash(a); }
                a.Ghost = a.Get("invuln") > 0f;

                if (a.Carrying == null)
                {
                    foreach (var b in _balls)
                    {
                        if (b.State != BallState.Ground) continue;
                        if (Vec2.Distance(a.Pos, new Vec2(b.X, b.Y)) < Constants.PlayerRadius * a.Scale + 16f)
                        {
                            b.State = BallState.Held; b.Holder = a.Id; a.Carrying = "ball";
                            break;
                        }
                    }
                }
                _powerups.Collect(a);
            }

            // held balls track their holder
            foreach (var b in _balls)
            {
                if (b.State != BallState.Held || b.Holder == null) continue;
                var h = Find(b.Holder);
                if (h == null || !h.Alive) { b.State = BallState.Ground; b.Holder = null; continue; }
                float ang = h.HasAim ? h.AimAngle : h.Facing;
                b.X = h.Pos.X + (float)Math.Cos(ang) * 26f;
                b.Y = h.Pos.Y + (float)Math.Sin(ang) * 26f;
            }

            // flight balls
            foreach (var b in _balls)
            {
                if (b.State != BallState.Flight) continue;
                b.T += dt;
                b.X += b.Vx * dt;
                b.Y += b.Vy * dt;
                if (b.X < 14f || b.X > Constants.ArenaW - 14f || b.Y < 14f || b.Y > Constants.ArenaH - 14f || b.T > BallLife)
                {
                    b.X = MathUtil.Clamp(b.X, 16f, Constants.ArenaW - 16f);
                    b.Y = MathUtil.Clamp(b.Y, 16f, Constants.ArenaH - 16f);
                    DropBall(b);
                    continue;
                }
                foreach (var a in AliveActors)
                {
                    if (a.Team == b.Team || a.Get("invuln") > 0f) continue;
                    if (Vec2.Distance(new Vec2(b.X, b.Y), a.Pos) < BallHitR + Constants.PlayerRadius * a.Scale)
                    {
                        if (a.Shield) { a.Shield = false; Emit(new Effect(EffectKind.Ring, a.Pos.X, a.Pos.Y)); }
                        else EliminatePegged(a);
                        DropBall(b);
                        break;
                    }
                }
            }

            int t0 = AliveActors.Count(a => a.Team == 0);
            int t1 = AliveActors.Count(a => a.Team == 1);
            if (t0 == 0 || t1 == 0 || Elapsed >= TimeLimit) _done = true;
        }

        private void ClampSide(Actor a)
        {
            float r = Constants.PlayerRadius * a.Scale;
            float x = a.Team == 0
                ? MathUtil.Clamp(a.Pos.X, r, Mid - DivPad - r)
                : MathUtil.Clamp(a.Pos.X, Mid + DivPad + r, Constants.ArenaW - r);
            a.Pos = new Vec2(x, a.Pos.Y);
        }

        private Ball HeldBall(string id) => _balls.FirstOrDefault(b => b.State == BallState.Held && b.Holder == id);

        private void DoThrow(Actor a)
        {
            var b = HeldBall(a.Id);
            if (b == null) return;
            float ang = a.HasAim ? a.AimAngle : a.Facing;
            b.State = BallState.Flight;
            b.Vx = (float)Math.Cos(ang) * BallSpeed;
            b.Vy = (float)Math.Sin(ang) * BallSpeed;
            b.Team = a.Team;
            b.T = 0f;
            b.Holder = null;
            a.Carrying = null;
            Emit(new Effect(EffectKind.Throw, b.X, b.Y));
        }

        private void DoDash(Actor a)
        {
            if (a.Get("dashCd") > 0f || a.Get("dashT") > 0f) return;
            float dx = a.InDx, dy = a.InDy;
            if (Math.Sqrt(dx * dx + dy * dy) < 0.1)
            {
                float ang = a.HasAim ? a.AimAngle : a.Facing;
                dx = (float)Math.Cos(ang); dy = (float)Math.Sin(ang);
            }
            var dir = new Vec2(dx, dy).Normalized;
            a.Set("dashDx", dir.X); a.Set("dashDy", dir.Y);
            a.Set("dashT", DashDur);
            a.Set("dashCd", DashCd);
            a.Set("invuln", Math.Max(a.Get("invuln"), 0.3f));
        }

        private void DropBall(Ball b)
        {
            b.State = BallState.Ground; b.Vx = 0f; b.Vy = 0f; b.Team = -1; b.Holder = null; b.T = 0f;
        }

        private void EliminatePegged(Actor a)
        {
            if (a.Carrying != null)
            {
                var held = HeldBall(a.Id);
                if (held != null) DropBall(held);
                a.Carrying = null;
            }
            Eliminate(a, "Pegged out!");
        }

        private Actor NearestEnemy(Actor a)
        {
            Actor best = null; float bd = float.MaxValue;
            foreach (var o in AliveActors)
            {
                if (o.Team == a.Team || o == a) continue;
                float d = Vec2.SqrDistance(a.Pos, o.Pos);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        private void BotThink(Actor a, float dt)
        {
            // dodge incoming enemy ball
            Ball danger = null; float dgd = float.MaxValue;
            foreach (var b in _balls)
            {
                if (b.State != BallState.Flight || b.Team == a.Team) continue;
                float dd = Vec2.Distance(new Vec2(b.X, b.Y), a.Pos);
                bool toward = (b.X - a.Pos.X) * b.Vx + (b.Y - a.Pos.Y) * b.Vy < 0f;
                if (dd < 170f && toward && dd < dgd) { dgd = dd; danger = b; }
            }
            if (danger != null)
            {
                float perp = (float)Math.Atan2(danger.Vy, danger.Vx) + (float)Math.PI / 2f;
                int side = (((int)(a.Pos.X * 13f + a.Pos.Y * 7f)) % 2 == 0) ? 1 : -1;
                a.InDx = (float)Math.Cos(perp) * side;
                a.InDy = (float)Math.Sin(perp) * side;
                if (dgd < 80f && a.Get("dashCd") <= 0f && Rng.NextFloat() < 0.6f) a.Set("wantDash", 1f);
                return;
            }
            if (a.Carrying != null)
            {
                var enemy = NearestEnemy(a);
                if (enemy != null)
                {
                    float lead = 0.16f;
                    float tx = enemy.Pos.X + enemy.Vel.X * lead, ty = enemy.Pos.Y + enemy.Vel.Y * lead;
                    a.AimAngle = (float)Math.Atan2(ty - a.Pos.Y, tx - a.Pos.X) + (Rng.NextFloat() - 0.5f) * 0.35f;
                    a.HasAim = true;
                    float bot = a.Get("botCd") - dt;
                    a.Set("botCd", bot);
                    if (bot <= 0f) { a.Set("botCd", 0.5f + Rng.NextFloat() * 0.9f); a.Set("wantThrow", 1f); }
                    a.InDx = (a.Team == 0 ? 1f : -1f) * 0.3f + (float)Math.Sin(Elapsed + a.Pos.Y) * 0.3f;
                    a.InDy = (float)Math.Cos(Elapsed + a.Pos.X) * 0.4f;
                }
                return;
            }
            // fetch nearest ground ball on our side
            Ball best = null; float bd = float.MaxValue;
            foreach (var b in _balls)
            {
                if (b.State != BallState.Ground) continue;
                bool onSide = a.Team == 0 ? b.X <= Mid : b.X >= Mid;
                if (!onSide) continue;
                float dd = Vec2.SqrDistance(a.Pos, new Vec2(b.X, b.Y));
                if (dd < bd) { bd = dd; best = b; }
            }
            if (best != null)
            {
                var dir = (new Vec2(best.X, best.Y) - a.Pos).Normalized;
                a.InDx = dir.X; a.InDy = dir.Y;
            }
            else
            {
                float tx = a.Team == 0 ? Mid - 120f : Mid + 120f;
                a.InDx = Math.Sign(tx - a.Pos.X) * 0.5f;
                a.InDy = (float)Math.Sin(Elapsed + a.Pos.X) * 0.4f;
            }
        }

        public override RoundResult Result()
        {
            var survivors = AliveActors
                .OrderByDescending(a => AliveActors.Count(x => x.Team == a.Team))
                .ToList();
            return CrownResult(survivors, false, null);
        }

        protected override object BuildData() => new DodgeData
        {
            TimeLeft = Math.Max(0f, TimeLimit - Elapsed),
            Mid = Mid,
            Team0Alive = AliveActors.Count(a => a.Team == 0),
            Team1Alive = AliveActors.Count(a => a.Team == 1),
            Balls = _balls.Select(b => new BallView { Id = b.Id, X = b.X, Y = b.Y, State = b.State.ToString() }).ToList(),
            Night = Ctx.Night,
            Pickups = _powerups.Snapshot()
        };

        public sealed class DodgeData
        {
            public float TimeLeft;
            public float Mid;
            public int Team0Alive, Team1Alive;
            public List<BallView> Balls;
            public bool Night;
            public List<PickupView> Pickups;
        }
        public struct BallView { public int Id; public float X, Y; public string State; }
    }
}
