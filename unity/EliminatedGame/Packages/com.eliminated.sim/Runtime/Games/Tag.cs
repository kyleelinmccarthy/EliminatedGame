using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Powerups;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Freeze Tag — a FEW icy "it" freezers chase a CROWD of runners (classic freeze
    /// tag, NOT even teams — being a runner should feel survivable, not a coin-flip).
    /// One freezer per ~5 players, with a small speed edge so they can actually catch.
    /// A touch freezes a runner solid; a free runner thaws a frozen one, until the final
    /// deep-freeze window. At the buzzer: frozen runners and freezers who caught nobody
    /// are out (never the whole field). Ported from lib/server/games/Tag.ts.
    /// </summary>
    public sealed class Tag : ArenaGame
    {
        private const float RoundTime = 34f;
        private const float FreezeR = Constants.PlayerRadius * 2f - 4f; // 48
        private const float ThawR = Constants.PlayerRadius * 2f + 2f;   // 54
        private const float FreezeCd = 0.4f;
        private const float ThawImmune = 0.8f;
        private const int FreezerTeam = 0;
        private const int RunnerTeam = 1;
        private const float FreezerSpeedEdge = 1.12f; // "it" is a touch faster (classic tag)

        private readonly PowerupField _powerups;
        private float _timer = RoundTime;
        private float _deepFreezeLen = 5f;
        private bool _deepFreeze;
        private bool _done;
        private int _numFreezers;

        /// <summary>How many "it" freezers for a field of <paramref name="n"/> — just ONE for a
        /// small field, TWO once it's bigger, and never more than two (by design: a lone hunter or
        /// a duo against the crowd). Their speed edge + proactive runner rescues keep it lively.</summary>
        public static int FreezerCount(int n) => n <= 2 ? 1 : (n >= 7 ? 2 : 1);

        public Tag(GameContext ctx) : base(ctx)
        {
            _powerups = new PowerupField(ctx.Rng, every: 2.5f, max: 6, goodWeight: 0.55f, emit: Emit);
        }

        public override GameId Id => GameId.Tag;
        public override bool IsDone => _done;
        protected override float MoveSpeed => 175f;
        protected override float DashSpeedMul => 2.6f;
        protected override float DashCooldown => 2.0f;

        public override void Start()
        {
            Elapsed = 0f;
            var shuffled = Rng.Shuffle(Actors);
            _numFreezers = Math.Min(shuffled.Count - 1, FreezerCount(shuffled.Count));
            if (_numFreezers < 1) _numFreezers = Math.Min(1, shuffled.Count);
            for (int i = 0; i < shuffled.Count; i++)
            {
                var a = shuffled[i];
                bool freezer = i < _numFreezers;     // the first few are "it"; the rest run
                int team = freezer ? FreezerTeam : RunnerTeam;
                a.Team = team;
                a.It = freezer;
                // Freezers start spread across the middle; runners scatter to the edges so
                // there's breathing room (no instant 50/50 clash).
                a.Pos = freezer
                    ? new Vec2(Constants.ArenaW * (0.32f + Rng.NextFloat() * 0.36f),
                               Constants.ArenaH * (0.30f + Rng.NextFloat() * 0.40f))
                    : new Vec2(Constants.ArenaW * (0.08f + Rng.NextFloat() * 0.84f),
                               Constants.ArenaH * (0.10f + Rng.NextFloat() * 0.80f));
                a.Frozen = false;
                a.Set("freezeCd", 0f);
                a.Set("immune", 0f);
                a.Set("freezes", 0f);
                a.Set("roam", Rng.NextFloat() * 6.2831853f);
            }
            _deepFreezeLen = 3.5f + Ctx.Intensity * 4.5f;
        }

        // The few "it" freezers move a touch faster so a lone hunter can actually run a
        // fleeing runner down; runners keep base speed.
        protected override float RoleSpeedMul(Actor a)
            => a.Alive && a.Team == FreezerTeam ? FreezerSpeedEdge : 1f;

        private List<Actor> Alive => Actors.Where(a => a.Alive).ToList();
        private int FreeRunners => Actors.Count(a => a.Alive && a.Team == RunnerTeam && !a.Frozen);

        public override void Tick(float dt)
        {
            if (_done) return;
            Elapsed += dt;
            _timer -= dt;
            _deepFreeze = _timer <= _deepFreezeLen;
            _powerups.Tick(dt);

            foreach (var a in Actors)
            {
                if (!a.Alive) continue;
                UpdateStatus(a, dt);
                a.Set("freezeCd", Math.Max(0f, a.Get("freezeCd") - dt));
                a.Set("immune", Math.Max(0f, a.Get("immune") - dt));

                if (a.Frozen)
                {
                    a.InDx = 0f; a.InDy = 0f;
                    a.Anim = AnimState.Fall;
                    a.DashT = 0f; a.Ghost = false;
                    continue;
                }
                if (a.IsBot) BotThink(a);
                MoveActor(a, dt);
                if (!a.Frozen) _powerups.Collect(a, Actors);
            }

            ResolveContacts();

            if (_timer <= 0f) Buzzer();
            else if (FreeRunners == 0) Buzzer();
        }

        private void ResolveContacts()
        {
            var alive = Alive;
            // FREEZE
            foreach (var f in alive)
            {
                if (f.Team != FreezerTeam || f.Get("freezeCd") > 0f) continue;
                foreach (var t in alive)
                {
                    if (t.Team != RunnerTeam || t.Frozen || t.Get("immune") > 0f) continue;
                    if (Vec2.Distance(f.Pos, t.Pos) < FreezeR * Math.Max(f.Scale, t.Scale))
                    {
                        if (t.Shield)
                        {
                            t.Shield = false;
                            Emit(new Effect(EffectKind.Ring, t.Pos.X, t.Pos.Y));
                        }
                        else
                        {
                            t.Frozen = true;
                            t.Anim = AnimState.Fall;
                            t.InDx = 0f; t.InDy = 0f;
                            Emit(new Effect(EffectKind.Freeze, t.Pos.X, t.Pos.Y));
                        }
                        f.Set("freezes", f.Get("freezes") + 1f);
                        f.Set("freezeCd", FreezeCd);
                        break;
                    }
                }
            }
            // THAW (outside deep freeze)
            if (!_deepFreeze)
            {
                foreach (var r in alive)
                {
                    if (r.Team != RunnerTeam || r.Frozen) continue;
                    foreach (var t in alive)
                    {
                        if (t == r || t.Team != RunnerTeam || !t.Frozen) continue;
                        if (Vec2.Distance(r.Pos, t.Pos) < ThawR)
                        {
                            t.Frozen = false;
                            t.Anim = AnimState.Idle;
                            t.Set("immune", ThawImmune);
                            Emit(new Effect(EffectKind.Thaw, t.Pos.X, t.Pos.Y));
                        }
                    }
                }
            }
        }

        private void Buzzer()
        {
            if (_done) return;
            var alive = Alive;
            var frozenRunners = alive.Where(a => a.Team == RunnerTeam && a.Frozen).ToList();
            var idleFreezers = alive.Where(a => a.Team == FreezerTeam && a.Get("freezes") == 0f).ToList();
            var doomed = frozenRunners.Concat(idleFreezers).ToList();

            // never wipe everyone: spare the first if all survivors are doomed
            if (doomed.Count >= alive.Count && alive.Count > 0)
            {
                var spared = doomed[0];
                spared.Frozen = false;
                doomed = doomed.Skip(1).ToList();
            }

            foreach (var l in doomed)
            {
                string note = l.Team == RunnerTeam ? "Frozen at the buzzer!" : "Caught nobody — frozen out!";
                Eliminate(l, note);
            }
            _done = true;
        }

        protected override void BotThink(Actor a)
        {
            var alive = Alive;
            if (a.Team == FreezerTeam)
            {
                var prey = Nearest(a, alive.Where(o => o.Team == RunnerTeam && !o.Frozen));
                if (prey == null) { Wander(a); return; }
                var d = (prey.Pos - a.Pos).Normalized;
                var sep = Spread(a, alive.Where(o => o.Team == FreezerTeam), 220f);
                a.InDx = d.X + sep.X * 0.7f;
                a.InDy = d.Y + sep.Y * 0.7f;
                return;
            }

            // RUNNER. Rescuing frozen friends is the heart of the game, so be PROACTIVE about it:
            // go thaw a frozen teammate whenever you're not in a freezer's face (or it's a quick grab),
            // rather than only when the rescue happens to be nearer than the threat.
            var threat = Nearest(a, alive.Where(o => o.Team == FreezerTeam));
            float threatD = threat != null ? Vec2.Distance(a.Pos, threat.Pos) : float.MaxValue;
            var frozenMate = Nearest(a, alive.Where(o => o.Team == RunnerTeam && o.Frozen && o != a));
            if (!_deepFreeze && frozenMate != null)
            {
                float rescueD = Vec2.Distance(a.Pos, frozenMate.Pos);
                var guard = Nearest(frozenMate, alive.Where(o => o.Team == FreezerTeam));
                bool guarded = guard != null && Vec2.Distance(frozenMate.Pos, guard.Pos) < 95f;
                if ((threatD > 150f && !guarded) || rescueD < 90f) // safe to go, or a quick brave grab
                {
                    var dr = (frozenMate.Pos - a.Pos).Normalized;
                    a.InDx = dr.X; a.InDy = dr.Y;
                    return;
                }
            }
            // Flee only when a freezer is actually closing in (otherwise mill / patrol).
            if (threat != null && threatD < 250f)
            {
                var d = (a.Pos - threat.Pos).Normalized;
                float weave = (float)Math.Sin(Elapsed * 1.8f + a.Get("roam")) * 0.5f;
                var wall = OffWalls(a);
                var sep = Spread(a, alive.Where(o => o.Team == RunnerTeam && !o.Frozen), 170f);
                a.InDx = d.X - d.Y * weave + wall.X * 0.9f + sep.X * 0.5f;
                a.InDy = d.Y + d.X * weave + wall.Y * 0.9f + sep.Y * 0.5f;
                return;
            }
            Wander(a);
        }

        // ── Bot steering helpers (shared shape with other arena games) ───
        private static Actor Nearest(Actor a, IEnumerable<Actor> list)
        {
            Actor best = null; float bd = float.MaxValue;
            foreach (var o in list)
            {
                float d = Vec2.SqrDistance(a.Pos, o.Pos);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        private static Vec2 Spread(Actor a, IEnumerable<Actor> mates, float radius)
        {
            float x = 0f, y = 0f;
            foreach (var o in mates)
            {
                if (o == a) continue;
                float dx = a.Pos.X - o.Pos.X, dy = a.Pos.Y - o.Pos.Y;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                if (d > 0.01f && d < radius)
                {
                    float w = 1f - d / radius;
                    x += dx / d * w; y += dy / d * w;
                }
            }
            return new Vec2(x, y);
        }

        private static Vec2 OffWalls(Actor a)
        {
            const float margin = 150f;
            float x = 0f, y = 0f;
            if (a.Pos.X < margin) x = 1f - a.Pos.X / margin;
            else if (a.Pos.X > Constants.ArenaW - margin) x = -(1f - (Constants.ArenaW - a.Pos.X) / margin);
            if (a.Pos.Y < margin) y = 1f - a.Pos.Y / margin;
            else if (a.Pos.Y > Constants.ArenaH - margin) y = -(1f - (Constants.ArenaH - a.Pos.Y) / margin);
            return new Vec2(x, y);
        }

        private void Wander(Actor a)
        {
            float t = Elapsed * 0.6f + a.Get("roam");
            float tx = Constants.ArenaW * (0.5f + 0.42f * (float)Math.Cos(t));
            float ty = Constants.ArenaH * (0.5f + 0.42f * (float)Math.Sin(t * 1.3f));
            a.InDx = (tx - a.Pos.X) / Constants.ArenaW;
            a.InDy = (ty - a.Pos.Y) / Constants.ArenaH;
        }

        protected override object BuildData()
        {
            var alive = Alive;
            return new TagData
            {
                TimeLeft = Math.Max(0f, _timer),
                DeepFreeze = _deepFreeze,
                Frozen = alive.Count(a => a.Team == RunnerTeam && a.Frozen),
                FreezersAlive = alive.Count(a => a.Team == FreezerTeam),
                RunnersAlive = alive.Count(a => a.Team == RunnerTeam),
                FreezerTeamId = FreezerTeam,
                Night = Ctx.Night,
                Pickups = _powerups.Snapshot()
            };
        }

        public sealed class TagData
        {
            public float TimeLeft;
            public bool DeepFreeze;
            public int Frozen;
            public int FreezersAlive;
            public int RunnersAlive;
            public int FreezerTeamId; // which Team value is "it" (so the view/HUD can tag YOUR role)
            public bool Night;
            public List<PickupView> Pickups;
        }
    }
}
