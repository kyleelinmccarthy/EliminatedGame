using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Free-for-all boomerang brawl. Throw a returning rang, dodge incoming ones,
    /// grab combat powerups, last blob standing. Has its own power set and dash
    /// (separate from the shared catalog). Ported from
    /// lib/server/games/Boomerang.ts. Finale-capable (forces a single survivor).
    /// </summary>
    public sealed class Boomerang : ArenaGame
    {
        /// <summary>Boomerang's own combat powerups (all "good").</summary>
        public enum BoomPower { Speed, BigRang, Multishot, Shield, Tiny, Magnet }

        private const float ThrowSpeed = 540f;
        private const float RangLife = 2.4f;
        private const float CatchR = 36f;
        private const float BaseHitR = 15f;
        private const float DashDur = 0.18f;
        private const float DashCd = 1.4f;
        private const float DashSpeed = 3.1f;
        private const float TimeLimit = 50f;
        private const float MinPlay = 12f;

        private sealed class Rang
        {
            public int Id;
            public string Owner;
            public Vec2 Pos;
            public Vec2 Vel;
            public float T;
            public bool Returning;
            public float Spin;
            public float Curve;
            public float HitR;
            public bool Magnet;
        }

        private sealed class Pickup
        {
            public int Id;
            public BoomPower Kind;
            public Vec2 Pos;
            public float Bob;
        }

        private readonly List<Rang> _rangs = new List<Rang>();
        private readonly List<Pickup> _pickups = new List<Pickup>();
        private int _nextId = 1;
        private float _spawnTimer = 1.5f;
        private int _target = 1;
        private bool _done;

        public Boomerang(GameContext ctx) : base(ctx) { }

        public override GameId Id => GameId.Boomerang;
        public override bool IsDone => _done;

        // ── Inspection (view + tests) ────────────────────────────────────
        public int RangCount => _rangs.Count;
        public int Target => _target;
        public int KillsOf(string id) { var a = Find(id); return a != null ? (int)a.Get("kills") : 0; }
        public int MaxRangsOf(string id) { var a = Find(id); return a != null ? (int)a.Get("maxRangs", 1) : 0; }
        public float InvulnOf(string id) { var a = Find(id); return a != null ? a.Get("invuln") : 0f; }

        public override void Start()
        {
            Elapsed = 0f;
            int n = Actors.Count;
            _target = Ctx.ForceSingleSurvivor
                ? 1
                : Math.Max(1, (int)Math.Round(n * (1f - 0.5f * Ctx.Intensity)));

            float cx = Constants.ArenaW / 2f, cy = Constants.ArenaH / 2f;
            const float marginX = 170f, marginY = 150f;
            float innerW = Constants.ArenaW - marginX * 2f;
            float innerH = Constants.ArenaH - marginY * 2f;
            int cols = Math.Max(1, Math.Min(n, (int)Math.Round(Math.Sqrt(n * (innerW / innerH)))));
            int rows = (int)Math.Ceiling(n / (float)cols);
            float cellW = innerW / cols, cellH = innerH / rows;

            for (int i = 0; i < n; i++)
            {
                var a = Actors[i];
                int col = i % cols, row = i / cols;
                int rowCount = Math.Min(cols, n - row * cols);
                float rowOffset = (cols - rowCount) * cellW * 0.5f;
                float x = marginX + rowOffset + (col + 0.5f) * cellW + (Rng.NextFloat() - 0.5f) * cellW * 0.4f;
                float y = marginY + (row + 0.5f) * cellH + (Rng.NextFloat() - 0.5f) * cellH * 0.4f;
                a.Pos = new Vec2(x, y);
                a.Scale = 1f;
                a.Shield = false;
                a.AimAngle = (float)Math.Atan2(y - cy, x - cx); // face outward
                a.HasAim = true;
                a.Set("maxRangs", 1f);
                a.Set("speedMul", 1f);
                a.Set("botCd", Rng.NextFloat());
                a.Set("rangs", 0f);
                a.Set("kills", 0f);
            }
        }

        // Boomerang routes input itself (its dash differs from the base dash).
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

        /// <summary>Spawn a pickup at a position (used by the spawner and tests).</summary>
        public void AddPickup(BoomPower kind, float x, float y)
        {
            _pickups.Add(new Pickup { Id = _nextId++, Kind = kind, Pos = new Vec2(x, y), Bob = Rng.NextFloat() * 6.283f });
        }

        public override void Tick(float dt)
        {
            if (_done) return;
            Elapsed += dt;

            // pickup spawns
            _spawnTimer -= dt;
            if (_spawnTimer <= 0f && _pickups.Count < 5)
            {
                _spawnTimer = 3.2f + Rng.NextFloat() * 2f;
                var kind = (BoomPower)Rng.NextInt(6);
                AddPickup(kind, 140f + Rng.NextFloat() * (Constants.ArenaW - 280f),
                                140f + Rng.NextFloat() * (Constants.ArenaH - 280f));
            }

            foreach (var a in Actors)
            {
                if (!a.Alive) continue;
                UpdateTimers(a, dt);
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
                    MoveAt(a, dt, a.Get("speedMul", 1f));
                }
                if (a.HasAim) a.Facing = a.AimAngle;

                if (a.Get("wantThrow") > 0f) { a.Set("wantThrow", 0f); DoThrow(a); }
                if (a.Get("wantDash") > 0f) { a.Set("wantDash", 0f); DoDash(a); }
                a.Ghost = a.Get("invuln") > 0f; // dashing this tick is instantly intangible

                // pickups
                for (int i = _pickups.Count - 1; i >= 0; i--)
                {
                    var pk = _pickups[i];
                    if (Vec2.Distance(a.Pos, pk.Pos) < Constants.PlayerRadius + 18f)
                    {
                        ApplyPower(a, pk.Kind);
                        _pickups.RemoveAt(i);
                        Emit(new Effect(EffectKind.Pickup, a.Pos.X, a.Pos.Y - 40f, 0f, pk.Kind.ToString()));
                    }
                }
            }

            UpdateRangs(dt);

            foreach (var pk in _pickups) pk.Bob += dt * 4f;

            int alive = AliveCount;
            if ((alive <= _target && Elapsed >= MinPlay) || alive <= 1 || Elapsed >= TimeLimit)
                _done = true;
        }

        private void DoThrow(Actor a)
        {
            if (a.Get("rangs") >= a.Get("maxRangs", 1f)) return;
            float ang = a.HasAim ? a.AimAngle : a.Facing;
            bool big = a.Get("bigT") > 0f;
            var r = new Rang
            {
                Id = _nextId++,
                Owner = a.Id,
                Pos = new Vec2(a.Pos.X + (float)Math.Cos(ang) * 24f, a.Pos.Y + (float)Math.Sin(ang) * 24f),
                Vel = new Vec2((float)Math.Cos(ang) * ThrowSpeed, (float)Math.Sin(ang) * ThrowSpeed),
                T = 0f,
                Returning = false,
                Curve = Rng.NextFloat() < 0.5f ? 1f : -1f,
                HitR = big ? BaseHitR * 2.1f : BaseHitR,
                Magnet = a.Get("magnetT") > 0f
            };
            _rangs.Add(r);
            a.Set("rangs", a.Get("rangs") + 1f);
            Emit(new Effect(EffectKind.Throw, r.Pos.X, r.Pos.Y));
        }

        private void DoDash(Actor a)
        {
            if (a.Get("dashCd") > 0f || a.Get("dashT") > 0f) return;
            float dx = a.InDx, dy = a.InDy;
            if (Math.Sqrt(dx * dx + dy * dy) < 0.1)
            {
                float ang = a.HasAim ? a.AimAngle : a.Facing;
                dx = (float)Math.Cos(ang);
                dy = (float)Math.Sin(ang);
            }
            var dir = new Vec2(dx, dy).Normalized;
            a.Set("dashDx", dir.X);
            a.Set("dashDy", dir.Y);
            a.Set("dashT", DashDur);
            a.Set("dashCd", DashCd);
            a.Set("invuln", Math.Max(a.Get("invuln"), 0.26f));
        }

        private void UpdateRangs(float dt)
        {
            for (int i = _rangs.Count - 1; i >= 0; i--)
            {
                var r = _rangs[i];
                r.T += dt;
                r.Spin += dt * 18f;
                var owner = Find(r.Owner);

                if (!r.Returning && r.T > RangLife * 0.42f) r.Returning = true;

                if (!r.Returning)
                {
                    float sp = r.Vel.Length;
                    float ang = (float)Math.Atan2(r.Vel.Y, r.Vel.X) + r.Curve * 2.4f * dt;
                    r.Vel = new Vec2((float)Math.Cos(ang) * sp, (float)Math.Sin(ang) * sp);
                    if (r.Magnet)
                    {
                        var tgt = NearestEnemy(r.Pos, r.Owner);
                        if (tgt != null)
                        {
                            float da = (float)Math.Atan2(tgt.Pos.Y - r.Pos.Y, tgt.Pos.X - r.Pos.X);
                            r.Vel = LerpAngleVec(r.Vel, da, sp, 0.06f);
                        }
                    }
                }
                else if (owner != null && owner.Alive)
                {
                    float da = (float)Math.Atan2(owner.Pos.Y - r.Pos.Y, owner.Pos.X - r.Pos.X);
                    var des = new Vec2((float)Math.Cos(da) * ThrowSpeed, (float)Math.Sin(da) * ThrowSpeed);
                    r.Vel += (des - r.Vel) * 0.14f;
                }

                r.Pos += r.Vel * dt;

                // wall bounce
                if (r.Pos.X < 12f || r.Pos.X > Constants.ArenaW - 12f)
                    r.Vel = new Vec2(-r.Vel.X, r.Vel.Y);
                if (r.Pos.Y < 12f || r.Pos.Y > Constants.ArenaH - 12f)
                    r.Vel = new Vec2(r.Vel.X, -r.Vel.Y);
                r.Pos = new Vec2(
                    MathUtil.Clamp(r.Pos.X, 12f, Constants.ArenaW - 12f),
                    MathUtil.Clamp(r.Pos.Y, 12f, Constants.ArenaH - 12f));

                // catch
                if (r.Returning && owner != null && owner.Alive &&
                    Vec2.Distance(r.Pos, owner.Pos) < CatchR)
                {
                    RemoveRang(i, r);
                    continue;
                }

                // hits
                bool hit = false;
                foreach (var a in Actors)
                {
                    if (!a.Alive || a.Id == r.Owner) continue;
                    if (a.Get("invuln") > 0f) continue;
                    float rr = r.HitR + Constants.PlayerRadius * a.Scale;
                    if (Vec2.Distance(r.Pos, a.Pos) < rr)
                    {
                        if (a.Shield)
                        {
                            a.Shield = false;
                            Emit(new Effect(EffectKind.Ring, a.Pos.X, a.Pos.Y));
                            hit = true;
                            break;
                        }
                        Kill(a, r.Owner);
                        hit = true;
                        break;
                    }
                }
                if (hit) { RemoveRang(i, r); continue; }

                if (r.T >= RangLife) RemoveRang(i, r);
            }
        }

        private void RemoveRang(int index, Rang r)
        {
            _rangs.RemoveAt(index);
            var o = Find(r.Owner);
            if (o != null) o.Set("rangs", Math.Max(0f, o.Get("rangs", 1f) - 1f));
        }

        private void Kill(Actor a, string killerId)
        {
            Eliminate(a, "Caught a boomerang");
            var killer = Find(killerId);
            if (killer != null) killer.Set("kills", killer.Get("kills") + 1f);
        }

        private void ApplyPower(Actor a, BoomPower kind)
        {
            switch (kind)
            {
                case BoomPower.Speed: a.Set("speedMul", 1.6f); a.Set("speedT", 8f); break;
                case BoomPower.BigRang: a.Set("bigT", 10f); break;
                case BoomPower.Multishot: a.Set("maxRangs", 3f); a.Set("multiT", 10f); break;
                case BoomPower.Shield: a.Shield = true; break;
                case BoomPower.Tiny: a.Scale = 0.62f; a.Set("tinyT", 10f); break;
                case BoomPower.Magnet: a.Set("magnetT", 10f); break;
            }
        }

        private void UpdateTimers(Actor a, float dt)
        {
            Dec(a, "dashCd", dt);
            Dec(a, "invuln", dt);
            Dec(a, "bigT", dt);
            Dec(a, "magnetT", dt);
            if (a.Get("speedT") > 0f && Dec(a, "speedT", dt) <= 0f) a.Set("speedMul", 1f);
            if (a.Get("multiT") > 0f && Dec(a, "multiT", dt) <= 0f) a.Set("maxRangs", 1f);
            if (a.Get("tinyT") > 0f && Dec(a, "tinyT", dt) <= 0f) a.Scale = 1f;
        }

        private static float Dec(Actor a, string key, float dt)
        {
            float v = a.Get(key);
            if (v > 0f) { v = Math.Max(0f, v - dt); a.Set(key, v); }
            return v;
        }

        private Actor NearestEnemy(Vec2 from, string ownerId)
        {
            Actor best = null;
            float bd = float.MaxValue;
            foreach (var a in Actors)
            {
                if (!a.Alive || a.Id == ownerId) continue;
                float d = Vec2.SqrDistance(from, a.Pos);
                if (d < bd) { bd = d; best = a; }
            }
            return best;
        }

        protected override object BuildData() => new BoomData
        {
            TimeLeft = Math.Max(0f, TimeLimit - Elapsed),
            Alive = AliveCount,
            Target = _target,
            Night = Ctx.Night,
            Rangs = _rangs.Select(r => new RangView
            {
                Id = r.Id, X = r.Pos.X, Y = r.Pos.Y, Spin = r.Spin,
                Big = r.HitR > BaseHitR * 1.5f, Owner = r.Owner
            }).ToList(),
            Pickups = _pickups.Select(p => new Eliminated.Sim.Powerups.PickupView
            {
                Id = p.Id, Kind = p.Kind.ToString(), X = p.Pos.X, Y = p.Pos.Y, Bob = p.Bob
            }).ToList()
        };

        public override RoundResult Result()
        {
            var survivors = AliveActors
                .OrderByDescending(a => a.Get("kills"))
                .ToList();
            return CrownResult(survivors, Ctx.ForceSingleSurvivor, "Out-brawled at the buzzer");
        }

        private void BotThink(Actor a, float dt)
        {
            var enemy = NearestEnemy(a.Pos, a.Id);

            // dodge: nearest incoming rang heading toward us
            Rang danger = null;
            float dgd = float.MaxValue;
            foreach (var r in _rangs)
            {
                if (r.Owner == a.Id) continue;
                float dd = Vec2.Distance(r.Pos, a.Pos);
                bool toward = (r.Pos.X - a.Pos.X) * r.Vel.X + (r.Pos.Y - a.Pos.Y) * r.Vel.Y < 0f;
                if (dd < 150f && toward && dd < dgd) { dgd = dd; danger = r; }
            }

            if (danger != null)
            {
                float perp = (float)Math.Atan2(danger.Vel.Y, danger.Vel.X) + (float)Math.PI / 2f;
                int side = (((int)(a.Pos.X * 13f + a.Pos.Y * 7f)) % 2 == 0) ? 1 : -1;
                a.InDx = (float)Math.Cos(perp) * side;
                a.InDy = (float)Math.Sin(perp) * side;
                if (dgd < 70f && a.Get("dashCd") <= 0f && Rng.NextFloat() < 0.6f) a.Set("wantDash", 1f);
            }
            else if (enemy != null)
            {
                Pickup pk = null;
                float pkd = 260f;
                foreach (var p in _pickups)
                {
                    float pd = Vec2.Distance(a.Pos, p.Pos);
                    if (pd < pkd) { pkd = pd; pk = p; }
                }
                if (pk != null && Rng.NextFloat() < 0.7f)
                {
                    a.InDx = Math.Sign(pk.Pos.X - a.Pos.X) * Math.Min(1f, Math.Abs(pk.Pos.X - a.Pos.X) / 50f);
                    a.InDy = Math.Sign(pk.Pos.Y - a.Pos.Y) * Math.Min(1f, Math.Abs(pk.Pos.Y - a.Pos.Y) / 50f);
                }
                else
                {
                    float de = Vec2.Distance(a.Pos, enemy.Pos);
                    int dir = de < 260f ? -1 : 1;
                    a.InDx = Math.Sign(enemy.Pos.X - a.Pos.X) * dir * 0.7f + (float)Math.Sin(Elapsed + a.Pos.Y) * 0.3f;
                    a.InDy = Math.Sign(enemy.Pos.Y - a.Pos.Y) * dir * 0.7f + (float)Math.Cos(Elapsed + a.Pos.X) * 0.3f;
                }
                // aim with lead + error
                float lead = 0.18f;
                float tx = enemy.Pos.X + enemy.Vel.X * lead;
                float ty = enemy.Pos.Y + enemy.Vel.Y * lead;
                float err = (Rng.NextFloat() - 0.5f) * 0.4f;
                a.AimAngle = (float)Math.Atan2(ty - a.Pos.Y, tx - a.Pos.X) + err;
                a.HasAim = true;

                float bot = a.Get("botCd") - dt;
                a.Set("botCd", bot);
                if (bot <= 0f && a.Get("rangs") < a.Get("maxRangs", 1f))
                {
                    a.Set("botCd", 0.7f + Rng.NextFloat() * 1.1f);
                    if (Vec2.Distance(a.Pos, enemy.Pos) < 620f) a.Set("wantThrow", 1f);
                }
            }
            else
            {
                a.InDx = (float)Math.Sin(Elapsed) * 0.4f;
                a.InDy = (float)Math.Cos(Elapsed) * 0.4f;
            }
        }

        private static Vec2 LerpAngleVec(Vec2 v, float targetAng, float sp, float t)
        {
            float cur = (float)Math.Atan2(v.Y, v.X);
            float diff = targetAng - cur;
            while (diff > Math.PI) diff -= (float)(Math.PI * 2);
            while (diff < -Math.PI) diff += (float)(Math.PI * 2);
            float na = cur + diff * t;
            return new Vec2((float)Math.Cos(na) * sp, (float)Math.Sin(na) * sp);
        }

        // ── Snapshot payload types ───────────────────────────────────────
        public sealed class BoomData
        {
            public float TimeLeft;
            public int Alive;
            public int Target;
            public bool Night;
            public List<RangView> Rangs;
            public List<Eliminated.Sim.Powerups.PickupView> Pickups;
        }
        public sealed class RangView { public int Id; public float X; public float Y; public float Spin; public bool Big; public string Owner; }
    }
}
