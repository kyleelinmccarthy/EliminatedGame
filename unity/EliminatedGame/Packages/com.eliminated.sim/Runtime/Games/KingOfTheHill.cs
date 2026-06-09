using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Powerups;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// King of the Lava Islands — the series finale. The floor is lava; islands
    /// rise, hold, and sink. Hop between them, grab powerups, and SHOVE rivals
    /// into the magma. Collapses to one shrinking last-stand island. Last blob
    /// not-on-fire is champion. Ported from lib/server/games/KingOfTheHill.ts.
    /// </summary>
    public sealed class KingOfTheHill : ArenaGame
    {
        private const float TimeCap = 60f;
        private const float BurnGrace = 0.95f;
        private const float ShoveCd = 0.6f;
        private const float ShoveLungeDur = 0.12f;
        private const float ShoveLungeSpeed = 2.6f;
        private const float ShoveRange = 66f;
        private const float ShoveArcCos = 0.32f;
        private const float ShoveImpulse = 380f;
        private const float KbDecay = 4.2f;
        private const float OpeningGrace = 14f;
        private const float RSmall = 56f;
        private const float RLarge = 150f;
        private const float FinalR = 32f;
        private const float SinkRate = 45f;

        private enum Phase { Rising, Stable, Sinking }

        private sealed class Island
        {
            public int Id;
            public float X, Y, R, TargetR, MaxR;
            public Phase Phase;
            public float Timer;
            public bool Final;
        }

        private readonly float _cx = Constants.ArenaW / 2f;
        private readonly float _cy = Constants.ArenaH / 2f;
        private List<Island> _islands = new List<Island>();
        private int _islandSeq = 1;
        private float _spawnTimer = 1.5f;
        private bool _suddenDeath;
        private string _kingId;
        private readonly PowerupField _powerups;
        private bool _done;

        public KingOfTheHill(GameContext ctx) : base(ctx)
        {
            _powerups = new PowerupField(ctx.Rng, every: 2.5f, max: 6, goodWeight: 0.55f, margin: 40f,
                emit: Emit,
                spawnRegions: () => _islands.Where(i => i.Phase != Phase.Sinking && i.R > 46f)
                    .Select(i => new SpawnRegion(i.X, i.Y, i.R)).ToList());
        }

        public override GameId Id => GameId.KingOfTheHill;
        public override bool IsDone => _done;
        public bool SuddenDeath => _suddenDeath;

        public override void Start()
        {
            Elapsed = 0f;
            const int nStart = 5;
            for (int i = 0; i < nStart; i++)
            {
                float maxR = RSmall + 18f + Rng.NextFloat() * (RLarge - RSmall - 18f);
                var isl = SpawnIsland(maxR);
                isl.R = maxR;
                isl.Phase = Phase.Stable;
                isl.Timer = 7f + Rng.NextFloat() * 9f;
            }
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                var isl = _islands[i % _islands.Count];
                float ang = Rng.NextFloat() * 6.2831853f;
                float rad = Rng.NextFloat() * Math.Max(0f, isl.R - Constants.PlayerRadius - 6f);
                a.Pos = new Vec2(isl.X + (float)Math.Cos(ang) * rad, isl.Y + (float)Math.Sin(ang) * rad);
                a.Set("burnT", 0f);
                a.Set("kingT", 0f);
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
                case InputKind.Action:
                    if (input.Name == "shove") a.Set("wantShove", 1f);
                    else if (input.Name == "dash") a.Set("wantDash", 1f);
                    break;
            }
        }

        private List<Actor> Alive => Actors.Where(a => a.Alive).ToList();

        private Island IslandUnder(Vec2 p)
        {
            Island best = null; float bd = float.MaxValue;
            foreach (var isl in _islands)
            {
                float d = Vec2.Distance(p, new Vec2(isl.X, isl.Y));
                if (d <= isl.R && d < bd) { bd = d; best = isl; }
            }
            return best;
        }

        public override void Tick(float dt)
        {
            if (_done) return;
            Elapsed += dt;
            _powerups.Tick(dt);
            UpdateIslands(dt);
            _powerups.Cull(p => _islands.Any(isl => Vec2.Distance(new Vec2(p.X, p.Y), new Vec2(isl.X, isl.Y)) <= isl.R - 6f));

            foreach (var a in Actors)
            {
                if (!a.Alive) continue;
                UpdateStatus(a, dt);
                a.Set("shoveCd", Math.Max(0f, a.Get("shoveCd") - dt));
                if (a.IsBot) BotThink(a);

                if (a.Get("wantShove") > 0f) { a.Set("wantShove", 0f); DoShove(a); }
                if (a.Get("wantDash") > 0f) { a.Set("wantDash", 0f); TryDash(a); }

                if (a.DashT > 0f)
                {
                    MoveActor(a, dt); // dash handled within
                }
                else if (a.Get("shoveT") > 0f)
                {
                    a.Set("shoveT", a.Get("shoveT") - dt);
                    a.InDx = a.Get("shoveDx");
                    a.InDy = a.Get("shoveDy");
                    MoveAt(a, dt, ShoveLungeSpeed);
                }
                else
                {
                    MoveActor(a, dt);
                }

                if (a.HasAim) a.Facing = a.AimAngle;
                ApplyKnockback(a, dt);
                _powerups.Collect(a);
            }

            Separate();
            Lava(dt);
            Crown(dt);

            if (Alive.Count <= 1 || Elapsed >= TimeCap) _done = true;
        }

        private void UpdateIslands(float dt)
        {
            float t = Elapsed;
            if (!_suddenDeath && t >= OpeningGrace && (t >= TimeCap * 0.72f || Alive.Count <= 2))
                EnterSuddenDeath();

            if (_suddenDeath)
            {
                foreach (var isl in _islands)
                {
                    if (isl.Final) isl.TargetR = Math.Max(FinalR, isl.TargetR - dt * 7f);
                    else { isl.Phase = Phase.Sinking; isl.TargetR = 0f; }
                }
            }
            else
            {
                foreach (var isl in _islands)
                {
                    if (isl.Phase == Phase.Rising)
                    {
                        isl.TargetR = isl.MaxR;
                        if (isl.R >= isl.MaxR - 4f) { isl.Phase = Phase.Stable; isl.Timer = 6f + Rng.NextFloat() * 6f; }
                    }
                    else if (isl.Phase == Phase.Stable)
                    {
                        isl.Timer -= dt;
                        if (isl.Timer <= 0f) { isl.Phase = Phase.Sinking; isl.TargetR = 0f; Emit(new Effect(EffectKind.Ring, isl.X, isl.Y, isl.MaxR / 90f)); }
                    }
                    else isl.TargetR = 0f;
                }

                int desired = Math.Max(3, (int)Math.Round(5f - (t / TimeCap) * 2f));
                int afloat = _islands.Count(i => i.Phase != Phase.Sinking);
                _spawnTimer -= dt;
                if (afloat < desired && _spawnTimer <= 0f)
                {
                    _spawnTimer = 0.6f + Rng.NextFloat();
                    float shrink = 1f - 0.28f * (t / TimeCap);
                    float maxR = (RSmall + Rng.NextFloat() * (RLarge - RSmall)) * shrink;
                    SpawnIsland(maxR);
                }
            }

            foreach (var isl in _islands)
            {
                if (isl.Phase == Phase.Sinking) isl.R = Math.Max(0f, isl.R - SinkRate * dt);
                else isl.R += (isl.TargetR - isl.R) * Math.Min(1f, dt * 3f);
            }
            _islands = _islands.Where(i => i.Final || !(i.Phase == Phase.Sinking && i.R < 3f)).ToList();
        }

        private void EnterSuddenDeath()
        {
            _suddenDeath = true;
            Island best = null; float bestScore = -1f;
            foreach (var isl in _islands)
            {
                int occ = Alive.Count(a => Vec2.Distance(a.Pos, new Vec2(isl.X, isl.Y)) <= isl.R);
                float score = occ * 10000f + isl.R;
                if (score > bestScore) { bestScore = score; best = isl; }
            }
            if (best == null) best = SpawnIsland(RLarge * 0.8f, _cx, _cy);
            best.Final = true;
            best.Phase = Phase.Stable;
            best.TargetR = Math.Max(FinalR, Math.Min(best.MaxR, 140f));
        }

        private Island SpawnIsland(float maxR, float? atX = null, float? atY = null)
        {
            float x, y;
            if (atX.HasValue) { x = atX.Value; y = atY.Value; }
            else PickSpot(maxR, out x, out y);
            var isl = new Island { Id = _islandSeq++, X = x, Y = y, R = 6f, TargetR = maxR, MaxR = maxR, Phase = Phase.Rising };
            _islands.Add(isl);
            Emit(new Effect(EffectKind.Ring, x, y, maxR / 90f));
            return isl;
        }

        private void PickSpot(float maxR, out float bx, out float by)
        {
            float margin = maxR + 24f;
            bx = _cx; by = _cy; float bestGap = float.NegativeInfinity;
            for (int tries = 0; tries < 16; tries++)
            {
                float x = margin + Rng.NextFloat() * (Constants.ArenaW - 2f * margin);
                float y = margin + Rng.NextFloat() * (Constants.ArenaH - 2f * margin);
                float gap = float.MaxValue;
                foreach (var isl in _islands)
                    gap = Math.Min(gap, Vec2.Distance(new Vec2(x, y), new Vec2(isl.X, isl.Y)) - isl.MaxR - maxR);
                if (gap == float.MaxValue) { bx = x; by = y; return; }
                if (gap > 40f && gap < 230f) { bx = x; by = y; return; }
                if (gap > bestGap) { bestGap = gap; bx = x; by = y; }
            }
        }

        private void DoShove(Actor a)
        {
            if (a.Get("shoveCd") > 0f) return;
            float ang = a.HasAim ? a.AimAngle : a.Facing;
            float dx = (float)Math.Cos(ang), dy = (float)Math.Sin(ang);
            a.Set("shoveDx", dx); a.Set("shoveDy", dy);
            a.Set("shoveT", ShoveLungeDur);
            a.Set("shoveCd", ShoveCd);
            a.Facing = ang;

            foreach (var o in Alive)
            {
                if (o == a) continue;
                float ox = o.Pos.X - a.Pos.X, oy = o.Pos.Y - a.Pos.Y;
                float dd = Math.Max(0.001f, (float)Math.Sqrt(ox * ox + oy * oy));
                if (dd > ShoveRange + Constants.PlayerRadius * (a.Scale + o.Scale)) continue;
                if ((ox / dd) * dx + (oy / dd) * dy < ShoveArcCos) continue;
                float power = ShoveImpulse * ((a.Scale / (a.Scale + o.Scale)) * 2f);
                o.Set("kbX", o.Get("kbX") + (ox / dd) * power);
                o.Set("kbY", o.Get("kbY") + (oy / dd) * power);
                o.Flash = 1f;
                Emit(new Effect(EffectKind.Shove, o.Pos.X, o.Pos.Y));
            }
        }

        private void ApplyKnockback(Actor a, float dt)
        {
            float kx = a.Get("kbX"), ky = a.Get("kbY");
            if (kx == 0f && ky == 0f) return;
            float r = Constants.PlayerRadius * a.Scale;
            a.Pos = new Vec2(
                MathUtil.Clamp(a.Pos.X + kx * dt, r, Constants.ArenaW - r),
                MathUtil.Clamp(a.Pos.Y + ky * dt, r, Constants.ArenaH - r));
            float f = Math.Max(0f, 1f - dt * KbDecay);
            kx *= f; ky *= f;
            if ((float)Math.Sqrt(kx * kx + ky * ky) < 8f) { kx = 0f; ky = 0f; }
            a.Set("kbX", kx); a.Set("kbY", ky);
        }

        private void Separate()
        {
            var alive = Alive;
            for (int i = 0; i < alive.Count; i++)
                for (int j = i + 1; j < alive.Count; j++)
                {
                    var a = alive[i]; var b = alive[j];
                    float rr = Constants.PlayerRadius * a.Scale + Constants.PlayerRadius * b.Scale;
                    float dx = b.Pos.X - a.Pos.X, dy = b.Pos.Y - a.Pos.Y;
                    float d = Math.Max(0.01f, (float)Math.Sqrt(dx * dx + dy * dy));
                    if (d < rr)
                    {
                        float overlap = (rr - d) / 2f;
                        float nx = dx / d, ny = dy / d;
                        float wa = b.Scale / (a.Scale + b.Scale), wb = a.Scale / (a.Scale + b.Scale);
                        const float push = 1.5f;
                        a.Pos = new Vec2(a.Pos.X - nx * overlap * push * wa, a.Pos.Y - ny * overlap * push * wa);
                        b.Pos = new Vec2(b.Pos.X + nx * overlap * push * wb, b.Pos.Y + ny * overlap * push * wb);
                    }
                }
        }

        private void Lava(float dt)
        {
            foreach (var a in Alive)
            {
                bool inLava = IslandUnder(a.Pos) == null;
                a.Burning = inLava;
                if (inLava)
                {
                    a.Set("burnT", a.Get("burnT") + dt);
                    if (a.Get("burnT") >= BurnGrace)
                    {
                        if (a.Shield) { a.Shield = false; a.Set("burnT", 0f); Emit(new Effect(EffectKind.Ring, a.Pos.X, a.Pos.Y)); }
                        else if (Alive.Count <= 1) a.Set("burnT", BurnGrace * 0.5f);
                        else { a.Burning = false; Eliminate(a, "Lava'd!"); }
                    }
                }
                else a.Set("burnT", Math.Max(0f, a.Get("burnT") - dt * 1.5f));
            }
        }

        private void Crown(float dt)
        {
            Actor king = null; float bd = float.MaxValue;
            foreach (var a in Alive)
            {
                var isl = IslandUnder(a.Pos);
                if (isl == null) continue;
                float d = Vec2.Distance(a.Pos, new Vec2(isl.X, isl.Y));
                if (d < bd) { bd = d; king = a; }
            }
            _kingId = king?.Id;
            if (king != null) king.Set("kingT", king.Get("kingT") + dt);
        }

        private Island NearestIsland(Actor a, Island exclude)
        {
            Island best = null; float bd = float.MaxValue;
            foreach (var isl in _islands)
            {
                if (isl == exclude) continue;
                if (isl.Phase == Phase.Sinking && isl.R < Constants.PlayerRadius) continue;
                float d = Vec2.Distance(a.Pos, new Vec2(isl.X, isl.Y)) - isl.R;
                if (d < bd) { bd = d; best = isl; }
            }
            return best;
        }

        protected override void BotThink(Actor a)
        {
            var here = IslandUnder(a.Pos);
            bool safeHere = here != null && here.Phase != Phase.Sinking && here.R > Constants.PlayerRadius * 1.4f;
            var target = safeHere ? here : (NearestIsland(a, here) ?? here);

            float wx = 0f, wy = 0f;
            if (target != null)
            {
                float d = Math.Max(1f, Vec2.Distance(a.Pos, new Vec2(target.X, target.Y)));
                float pull = d > target.R ? 1f : 0.3f;
                wx += (target.X - a.Pos.X) / d * pull;
                wy += (target.Y - a.Pos.Y) / d * pull;
            }

            if (here != null)
            {
                Actor foe = null; float fd = float.MaxValue;
                foreach (var o in Alive)
                {
                    if (o == a) continue;
                    float od = Vec2.Distance(a.Pos, o.Pos);
                    if (od < fd) { fd = od; foe = o; }
                }
                if (foe != null && fd < 160f)
                {
                    wx += (foe.Pos.X - a.Pos.X) / Math.Max(1f, fd) * 0.8f;
                    wy += (foe.Pos.Y - a.Pos.Y) / Math.Max(1f, fd) * 0.8f;
                    float reach = ShoveRange + Constants.PlayerRadius * (a.Scale + foe.Scale);
                    if (a.Get("shoveCd") <= 0f && fd < reach && Rng.NextFloat() < 0.4f)
                    {
                        a.Facing = (float)Math.Atan2(foe.Pos.Y - a.Pos.Y, foe.Pos.X - a.Pos.X);
                        a.HasAim = false; // use facing
                        a.Set("wantShove", 1f);
                    }
                }
            }

            float m = Math.Max(1f, (float)Math.Sqrt(wx * wx + wy * wy));
            a.InDx = wx / m; a.InDy = wy / m;
        }

        public override RoundResult Result()
        {
            var ranked = AliveActors.OrderByDescending(a => a.Get("kingT")).ToList();
            return CrownResult(ranked, Ctx.ForceSingleSurvivor, "Ran out of ground");
        }

        protected override object BuildData() => new KothData
        {
            Islands = _islands.Select(i => new IslandView { X = i.X, Y = i.Y, R = i.R, Final = i.Final }).ToList(),
            TimeLeft = Math.Max(0f, TimeCap - Elapsed),
            KingId = _kingId,
            Alive = Alive.Count,
            Night = Ctx.Night,
            Pickups = _powerups.Snapshot()
        };

        public sealed class KothData
        {
            public List<IslandView> Islands;
            public float TimeLeft;
            public string KingId;
            public int Alive;
            public bool Night;
            public List<PickupView> Pickups;
        }
        public struct IslandView { public float X, Y, R; public bool Final; }
    }
}
