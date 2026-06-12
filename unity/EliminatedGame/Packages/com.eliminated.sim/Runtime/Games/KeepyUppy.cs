using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Powerups;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Keepy Uppy — keep YOUR balloon off the floor while SPIKE-ing rivals' balloons
    /// to pop them. Balloons fall with a realistic falling-leaf flutter, ramping
    /// gravity/wind. Floor or pop = the owner is out. Ported from
    /// lib/server/games/KeepyUppy.ts. Finale-capable.
    /// </summary>
    public sealed class KeepyUppy : ArenaGame
    {
        private const float TimeCap = 38f;
        private const float BalloonR = 30f;
        private const float BatVy = 420f;
        private const float BatPush = 70f;
        private const float BatScatter = 42f;
        private const float G0 = 95f, G1 = 330f;
        private const float DragY = 1.0f, DragX = 0.95f;
        private const float Wind0 = 18f, Wind1 = 105f;
        private const float Flutter0 = 80f, Flutter1 = 210f;
        private const float SpikeDur = 0.32f, SpikeCd = 1.3f, SpikeLunge = 1.5f;
        private const float MaxBSpeed = 620f;

        private sealed class Balloon
        {
            public string Owner;
            public float X, Y, Vx, Vy;
            public string Color;
            public bool Popped;
            public float FlutterPhase, FlutterFreq;
        }

        private readonly Dictionary<string, Balloon> _balloons = new Dictionary<string, Balloon>();
        private readonly PowerupField _powerups;
        private float _windPhase, _gravity = G0, _windX, _windY, _flutterAmp = Flutter0, _turb = 24f;
        private bool _done;

        public KeepyUppy(GameContext ctx) : base(ctx)
        {
            _powerups = new PowerupField(ctx.Rng, every: 2.8f, max: 5, goodWeight: 0.7f, margin: 140f, emit: Emit);
        }

        public override GameId Id => GameId.KeepyUppy;
        public override bool IsDone => _done;

        public override void Start()
        {
            Elapsed = 0f;
            int n = Actors.Count;
            for (int i = 0; i < n; i++)
            {
                var a = Actors[i];
                float x = Constants.ArenaW * ((i + 0.5f) / n);
                a.Pos = new Vec2(x, Constants.ArenaH * 0.66f);
                if (a.IsBot)
                {
                    a.Set("skill", 0.2f + Rng.NextFloat() * 0.75f);
                    a.Set("aggro", Rng.NextFloat() < 0.4f ? Rng.NextFloat() : 0f);
                    a.Set("homeY", Constants.ArenaH * (0.58f + Rng.NextFloat() * 0.08f));
                }
                _balloons[a.Id] = new Balloon
                {
                    Owner = a.Id, X = x, Y = Constants.ArenaH * 0.32f,
                    Vx = (Rng.NextFloat() - 0.5f) * 30f, Vy = 0f,
                    Color = a.CharacterId, FlutterPhase = Rng.NextFloat() * 6.2831853f,
                    FlutterFreq = 2.0f + Rng.NextFloat() * 1.7f
                };
            }
        }

        public override void OnInput(string actorId, GameInput input)
        {
            var a = Find(actorId);
            if (a == null || !a.Alive) return;
            switch (input.Kind)
            {
                case InputKind.Move: a.InDx = input.Dx; a.InDy = input.Dy; break;
                case InputKind.Aim: a.AimAngle = input.Angle; a.HasAim = true; break;
                case InputKind.Tap: TrySpike(a); break;
                case InputKind.Action:
                    if (input.Name == "dash") TryDash(a);
                    else if (input.Name == "spike" || input.Name == "throw") TrySpike(a);
                    break;
            }
        }

        // Test/debug hooks
        public void DebugSetBalloon(string owner, float x, float y) { if (_balloons.TryGetValue(owner, out var b)) { b.X = x; b.Y = y; } }
        public bool BalloonPopped(string owner) => _balloons.TryGetValue(owner, out var b) && b.Popped;

        private void TrySpike(Actor a)
        {
            if (!a.Alive || a.Get("spikeCd") > 0f || a.Get("spikeT") > 0f) return;
            a.Set("spikeT", SpikeDur);
            a.Set("spikeCd", SpikeCd);
            Emit(new Effect(EffectKind.Spark, a.Pos.X, a.Pos.Y));
        }

        public override void Forfeit(string actorId)
        {
            base.Forfeit(actorId);
            if (_balloons.TryGetValue(actorId, out var b)) b.Popped = true;
        }

        public override void Tick(float dt)
        {
            if (_done) return;
            Elapsed += dt;
            float p = Math.Min(1f, Elapsed / TimeCap);
            _gravity = (G0 + (G1 - G0) * p) * (0.85f + 0.3f * Ctx.Intensity);
            float windAmp = (Wind0 + (Wind1 - Wind0) * p) * (0.7f + 0.6f * Ctx.Intensity);
            _windPhase += dt * (0.6f + 1.6f * p);
            float gust = 0.7f + 0.3f * (float)Math.Sin(_windPhase * 3.3f);
            _windX = (float)Math.Cos(_windPhase) * windAmp * gust;
            _windY = (float)Math.Sin(_windPhase * 1.7f) * windAmp * 0.3f;
            _flutterAmp = (Flutter0 + (Flutter1 - Flutter0) * p) * (0.8f + 0.4f * Ctx.Intensity);
            _turb = (14f + 55f * p) * (0.7f + 0.6f * Ctx.Intensity);

            _powerups.Tick(dt);

            foreach (var a in Actors)
            {
                if (!a.Alive) continue;
                UpdateStatus(a, dt);
                if (a.Get("spikeT") > 0f) a.Set("spikeT", Math.Max(0f, a.Get("spikeT") - dt));
                if (a.Get("spikeCd") > 0f) a.Set("spikeCd", Math.Max(0f, a.Get("spikeCd") - dt));
                a.Progress = a.Get("spikeT") > 0f ? MathUtil.Clamp01(a.Get("spikeT") / SpikeDur) : 0f; // drives the spike-pin view
                if (a.IsBot) BotThink(a, dt);

                if (a.Get("spikeT") > 0f && a.DashT <= 0f) MoveAt(a, dt, SpikeLunge);
                else MoveActor(a, dt);
                _powerups.Collect(a);
            }

            UpdateBalloons(dt);
            ResolveContacts();
            CollideBalloons();
            CheckFloors();

            if (AliveCount <= 1 || Elapsed >= TimeCap) _done = true;
        }

        private void UpdateBalloons(float dt)
        {
            foreach (var b in _balloons.Values)
            {
                if (b.Popped) continue;
                b.Vy += _gravity * dt + _windY * dt + (Rng.NextFloat() - 0.5f) * _turb * 0.25f * dt;
                b.Vx += _windX * dt + (Rng.NextFloat() - 0.5f) * _turb * dt;
                b.FlutterPhase += b.FlutterFreq * dt;
                float sink = Math.Max(0f, b.Vy);
                b.Vx += (float)Math.Sin(b.FlutterPhase) * _flutterAmp * (0.3f + 0.7f * Math.Min(1f, sink / 140f)) * dt;
                b.Vy *= Math.Max(0f, 1f - DragY * dt);
                b.Vx *= Math.Max(0f, 1f - DragX * dt);
                b.Vx = MathUtil.Clamp(b.Vx, -MaxBSpeed, MaxBSpeed);
                b.Vy = MathUtil.Clamp(b.Vy, -MaxBSpeed, MaxBSpeed);
                b.X += b.Vx * dt; b.Y += b.Vy * dt;
                if (b.X < BalloonR) { b.X = BalloonR; b.Vx = Math.Abs(b.Vx) * 0.6f; }
                else if (b.X > Constants.ArenaW - BalloonR) { b.X = Constants.ArenaW - BalloonR; b.Vx = -Math.Abs(b.Vx) * 0.6f; }
                if (b.Y < BalloonR) { b.Y = BalloonR; b.Vy = Math.Abs(b.Vy) * 0.5f; }
            }
        }

        private void ResolveContacts()
        {
            foreach (var a in AliveActors)
            {
                bool spiking = a.Get("spikeT") > 0f;
                float reach = Constants.PlayerRadius * a.Scale + BalloonR;
                foreach (var b in _balloons.Values)
                {
                    if (b.Popped) continue;
                    float dx = b.X - a.Pos.X, dy = b.Y - a.Pos.Y;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d > reach) continue;
                    if (spiking && b.Owner != a.Id) { Pop(b, "Popped!"); continue; }
                    float nx = d > 0.01f ? dx / d : 0f;
                    float ny = d > 0.01f ? dy / d : -1f;
                    b.X = MathUtil.Clamp(a.Pos.X + nx * (reach + 1f), BalloonR, Constants.ArenaW - BalloonR);
                    b.Y = MathUtil.Clamp(a.Pos.Y + ny * (reach + 1f), BalloonR, Constants.ArenaH - BalloonR - 1f);
                    b.Vy = -BatVy * (0.9f + Rng.NextFloat() * 0.28f);
                    b.Vx += nx * BatPush + a.Vel.X * 0.3f + (Rng.NextFloat() - 0.5f) * BatScatter;
                    b.FlutterPhase = Rng.NextFloat() * 6.2831853f;
                }
            }
        }

        private void CollideBalloons()
        {
            var list = _balloons.Values.Where(b => !b.Popped).ToList();
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i]; var b = list[j];
                    float dx = b.X - a.X, dy = b.Y - a.Y;
                    float d = Math.Max(0.01f, (float)Math.Sqrt(dx * dx + dy * dy));
                    float rr = BalloonR * 2f;
                    if (d >= rr) continue;
                    float nx = dx / d, ny = dy / d, overlap = (rr - d) / 2f;
                    a.X -= nx * overlap; a.Y -= ny * overlap;
                    b.X += nx * overlap; b.Y += ny * overlap;
                    float va = a.Vx * nx + a.Vy * ny, vb = b.Vx * nx + b.Vy * ny;
                    float diff = (vb - va) * 0.45f;
                    a.Vx += nx * diff; a.Vy += ny * diff;
                    b.Vx -= nx * diff; b.Vy -= ny * diff;
                }
        }

        private void CheckFloors()
        {
            foreach (var b in _balloons.Values)
            {
                if (b.Popped) continue;
                if (b.Y + BalloonR >= Constants.ArenaH) { b.Y = Constants.ArenaH - BalloonR; Pop(b, "Hit the floor!"); }
            }
        }

        private void Rescue(Balloon b)
        {
            b.Y = Math.Min(b.Y, Constants.ArenaH * 0.5f);
            b.Vy = -BatVy; b.Vx *= 0.3f;
            Emit(new Effect(EffectKind.Ring, b.X, b.Y));
        }

        private void Pop(Balloon b, string note)
        {
            if (b.Popped) return;
            var owner = Find(b.Owner);
            if (owner != null && owner.Alive && owner.Shield) { owner.Shield = false; Rescue(b); return; }
            if (owner != null && owner.Alive && AliveCount <= 1) { Rescue(b); return; }
            b.Popped = true;
            Emit(new Effect(EffectKind.Shatter, b.X, b.Y));
            Emit(new Effect(EffectKind.Spark, b.X, b.Y, 1.4f));   // bold colored burst on the pop
            if (owner != null && owner.Alive)
            {
                owner.InDx = 0f; owner.InDy = 0f;
                Emit(new Effect(EffectKind.Splat, owner.Pos.X, owner.Pos.Y)); // satisfying splat on elimination
                Eliminate(owner, note);
            }
        }

        private void BotThink(Actor a, float dt)
        {
            if (!_balloons.TryGetValue(a.Id, out var me)) { a.InDx = 0f; a.InDy = 0f; return; }
            float skill = a.Get("skill", 0.6f);
            a.Set("react", a.Get("react") - dt);
            a.Set("huntCd", a.Get("huntCd") - dt);
            if (a.Get("react") <= 0f)
            {
                float slop = (1f - skill) * (1f - skill);
                a.Set("react", 0.1f + slop * 1.4f);
                float lead = 0.22f + (1f - skill) * 0.3f;
                float homeY = a.Get("homeY", Constants.ArenaH * 0.6f);
                a.Set("tx", MathUtil.Clamp(me.X + me.Vx * lead + (Rng.NextFloat() - 0.5f) * slop * 330f, 40f, Constants.ArenaW - 40f));
                a.Set("ty", MathUtil.Clamp(Math.Max(homeY, me.Y + BalloonR + Constants.PlayerRadius * 0.6f) + (Rng.NextFloat() - 0.5f) * slop * 90f, 40f, Constants.ArenaH - 40f));
                a.Set("hunt", 0f);

                bool safe = me.Y < Constants.ArenaH * 0.38f && Math.Abs(me.Vy) < 40f;
                float aggro = a.Get("aggro");
                if (safe && aggro > 0f && a.Get("huntCd") <= 0f && Rng.NextFloat() < aggro * 0.04f)
                {
                    Balloon foe = null; float fd = 190f;
                    foreach (var b in _balloons.Values)
                    {
                        if (b.Popped || b.Owner == a.Id) continue;
                        float bd = Vec2.Distance(a.Pos, new Vec2(b.X, b.Y));
                        if (bd < fd) { fd = bd; foe = b; }
                    }
                    if (foe != null) { a.Set("tx", foe.X); a.Set("ty", foe.Y); a.Set("hunt", 1f); a.Set("huntCd", 6f + Rng.NextFloat() * 5f); }
                }
            }

            float tx = a.Get("tx", me.X), ty = a.Get("ty", me.Y);
            var dir = (new Vec2(tx, ty) - a.Pos).Normalized;
            a.InDx = dir.X; a.InDy = dir.Y;
            a.Facing = (float)Math.Atan2(ty - a.Pos.Y, tx - a.Pos.X);

            if (a.Get("hunt") > 0f && Vec2.Distance(a.Pos, new Vec2(tx, ty)) < Constants.PlayerRadius + BalloonR + 10f)
                TrySpike(a);
        }

        public override RoundResult Result()
        {
            var survivors = AliveActors
                .OrderBy(a => _balloons.TryGetValue(a.Id, out var b) ? b.Y : Constants.ArenaH)
                .ToList();
            return CrownResult(survivors, Ctx.ForceSingleSurvivor, "Lowest balloon at the buzzer");
        }

        protected override object BuildData() => new KeepyData
        {
            TimeLeft = Math.Max(0f, TimeCap - Elapsed),
            Alive = AliveCount,
            Balloons = _balloons.Values.Where(b => !b.Popped).Select(b => new BalloonView { Owner = b.Owner, X = b.X, Y = b.Y, Vx = b.Vx, Color = b.Color }).ToList(),
            Pickups = _powerups.Snapshot(),
            Night = Ctx.Night
        };

        public sealed class KeepyData
        {
            public float TimeLeft;
            public int Alive;
            public List<BalloonView> Balloons;
            public List<PickupView> Pickups;
            public bool Night;
        }
        public struct BalloonView { public string Owner; public float X, Y, Vx; public string Color; }
    }
}
