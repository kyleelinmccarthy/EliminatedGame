using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Shared base for top-down movement minigames. Owns the DRY building blocks
    /// every arena game reuses: input handling, powerup status, dash, circle
    /// movement with arena clamping, elimination bookkeeping, ranking, and
    /// snapshot building. Subclasses add the game-specific rules in
    /// <see cref="Start"/>, <see cref="Tick"/>, <see cref="IsDone"/>, and
    /// (for bots) <see cref="BotThink"/>. Ported from lib/server/games/Minigame.ts.
    /// </summary>
    public abstract class ArenaGame : IMinigame
    {
        protected readonly GameContext Ctx;
        protected readonly Rng Rng;
        protected readonly List<Actor> Actors;

        /// <summary>Seconds elapsed since play started.</summary>
        protected float Elapsed;

        // ── Tuning (override per game) ───────────────────────────────────
        protected virtual float MoveSpeed => Constants.PlayerSpeed; // 240
        protected virtual float DashCooldown => 1.4f;
        protected virtual float DashIFrames => 0f;
        protected const float DashDuration = 0.18f;
        protected const float DashSpeedMul = 3.1f;
        private const float DizzyFreq = 8f;
        private const float DizzyAmp = 0.8f;

        // ── Bookkeeping ──────────────────────────────────────────────────
        private readonly List<string> _elimOrder = new List<string>();   // earliest first
        private readonly List<string> _finishOrder = new List<string>(); // survivor ordering
        private readonly Dictionary<string, string> _notes = new Dictionary<string, string>();
        private readonly List<Effect> _fx = new List<Effect>();

        protected ArenaGame(GameContext ctx)
        {
            Ctx = ctx;
            Rng = ctx.Rng;
            Actors = ctx.Actors;
        }

        public abstract GameId Id { get; }
        public abstract bool IsDone { get; }
        public abstract void Start();

        public IEnumerable<Actor> AliveActors => Actors.Where(a => a.Alive);
        protected int AliveCount => Actors.Count(a => a.Alive);

        // ── Input ────────────────────────────────────────────────────────
        public virtual void OnInput(string actorId, GameInput input)
        {
            var a = Find(actorId);
            if (a == null || !a.Alive) return;
            switch (input.Kind)
            {
                case InputKind.Move:
                    a.InDx = input.Dx;
                    a.InDy = input.Dy;
                    break;
                case InputKind.Aim:
                    a.AimAngle = input.Angle;
                    a.HasAim = true;
                    break;
                case InputKind.Action:
                    if (input.Name == "dash") TryDash(a);
                    else OnAction(a, input.Name, input.On);
                    break;
                case InputKind.Tap:
                    OnAction(a, "tap", input.On);
                    break;
                case InputKind.Choose:
                    OnChoose(a, input.Value);
                    break;
            }
        }

        /// <summary>Game-specific named action (throw/jump/pull/shove/spike/tap).</summary>
        protected virtual void OnAction(Actor a, string name, bool on) { }

        /// <summary>Game-specific discrete choice (glass-bridge side, rps throw…).</summary>
        protected virtual void OnChoose(Actor a, string value) { }

        protected Actor Find(string id)
        {
            for (int i = 0; i < Actors.Count; i++)
                if (Actors[i].Id == id) return Actors[i];
            return null;
        }

        // ── Tick ─────────────────────────────────────────────────────────
        /// <summary>Default tick: advance time and run standard movement for all
        /// alive actors. Games override for bespoke logic (and may still call
        /// <see cref="StandardMovement"/>).</summary>
        public virtual void Tick(float dt)
        {
            Elapsed += dt;
            StandardMovement(dt);
        }

        protected void StandardMovement(float dt)
        {
            foreach (var a in Actors)
            {
                if (!a.Alive) continue;
                if (a.IsBot) BotThink(a);
                UpdateStatus(a, dt);
                MoveActor(a, dt);
            }
        }

        /// <summary>Set a bot's input intent (InDx/InDy/AimAngle). No-op by default.</summary>
        protected virtual void BotThink(Actor a) { }

        // ── Status / powerups ────────────────────────────────────────────
        protected void UpdateStatus(Actor a, float dt)
        {
            a.PuSpeedT = Dec(a.PuSpeedT, dt);
            a.PuSlowT = Dec(a.PuSlowT, dt);
            a.PuReverseT = Dec(a.PuReverseT, dt);
            a.PuDizzyT = Dec(a.PuDizzyT, dt);
            a.PuVisionT = Dec(a.PuVisionT, dt);
            a.PuTinyT = Dec(a.PuTinyT, dt);
            a.PuGiantT = Dec(a.PuGiantT, dt);
            a.DashCdT = Dec(a.DashCdT, dt);
            a.IFrameT = Dec(a.IFrameT, dt);
            a.DashT = Dec(a.DashT, dt);
            a.Ghost = a.DashT > 0f;
            if (a.Flash > 0f) a.Flash = Math.Max(0f, a.Flash - dt * 3f);

            // size from tiny/giant powerups (giant, the curse, wins ties)
            a.Scale = a.PuTinyT > 0f ? 0.62f : (a.PuGiantT > 0f ? 1.5f : 1f);

            // night-mode vision radius
            a.Vision = Ctx.Night
                ? Constants.NightBaseVision + (a.PuVisionT > 0f ? Constants.NightLanternBonus : 0f)
                : 0f;
        }

        private static float Dec(float t, float dt) => t > 0f ? Math.Max(0f, t - dt) : 0f;

        protected float StatusSpeedMul(Actor a)
        {
            float m = 1f;
            if (a.PuSpeedT > 0f) m *= 1.6f;
            if (a.PuSlowT > 0f) m *= 0.5f;
            return m;
        }

        protected float SizeSpeedMul(Actor a)
        {
            if (a.PuTinyT > 0f) return 1.15f; // nimble
            if (a.PuGiantT > 0f) return 0.62f; // lumbering
            return 1f;
        }

        // ── Dash ─────────────────────────────────────────────────────────
        protected bool TryDash(Actor a)
        {
            if (a.DashCdT > 0f || a.DashT > 0f) return false;
            a.DashT = DashDuration;
            a.DashCdT = DashCooldown;
            a.IFrameT = DashIFrames;
            float dx = a.InDx, dy = a.InDy;
            if (dx * dx + dy * dy < 0.0025f)
            {
                dx = (float)Math.Cos(a.Facing);
                dy = (float)Math.Sin(a.Facing);
            }
            var dir = new Vec2(dx, dy).Normalized;
            a.Set("dashDx", dir.X);
            a.Set("dashDy", dir.Y);
            a.Ghost = true;
            return true;
        }

        // ── Movement ─────────────────────────────────────────────────────
        protected void MoveActor(Actor a, float dt)
        {
            Vec2 vel;
            if (a.DashT > 0f)
            {
                var dir = new Vec2(a.Get("dashDx"), a.Get("dashDy"));
                float speed = MoveSpeed * DashSpeedMul * SizeSpeedMul(a);
                vel = dir * speed;
                a.Anim = AnimState.Run;
                if (dir.SqrLength > 0.0025f) a.Facing = (float)Math.Atan2(dir.Y, dir.X);
            }
            else
            {
                var (dx, dy) = ResolveInput(a);
                var dir = new Vec2(dx, dy).ClampMagnitude(1f);
                float speed = MoveSpeed * StatusSpeedMul(a) * SizeSpeedMul(a);
                vel = dir * speed;
                if (dir.SqrLength > 0.0025f)
                {
                    a.Facing = (float)Math.Atan2(dir.Y, dir.X);
                    a.Anim = AnimState.Run;
                }
                else
                {
                    a.Anim = AnimState.Idle;
                }
            }

            a.Vel = vel;
            float r = a.Radius;
            var pos = a.Pos + vel * dt;
            a.Pos = new Vec2(
                MathUtil.Clamp(pos.X, r, Constants.ArenaW - r),
                MathUtil.Clamp(pos.Y, r, Constants.ArenaH - r));
        }

        /// <summary>
        /// Integrate movement at a caller-supplied speed multiplier (no powerup
        /// status / base dash applied). For games that manage their own combat
        /// dash and speed buffs (e.g. Boomerang). Mirrors the reference
        /// moveActor(a, dt, mul).
        /// </summary>
        protected void MoveAt(Actor a, float dt, float speedMul)
        {
            var dir = new Vec2(a.InDx, a.InDy).ClampMagnitude(1f);
            float speed = MoveSpeed * speedMul;
            var vel = dir * speed;
            a.Vel = vel;
            float r = a.Radius;
            var pos = a.Pos + vel * dt;
            a.Pos = new Vec2(
                MathUtil.Clamp(pos.X, r, Constants.ArenaW - r),
                MathUtil.Clamp(pos.Y, r, Constants.ArenaH - r));
            if (dir.SqrLength > 0.0025f)
            {
                a.Facing = (float)Math.Atan2(dir.Y, dir.X);
                a.Anim = AnimState.Run;
            }
            else
            {
                a.Anim = AnimState.Idle;
            }
        }

        /// <summary>
        /// Build a result where, in a finale (<paramref name="forceSingle"/>),
        /// the best survivor is crowned and any co-survivors are demoted to
        /// just-eliminated with <paramref name="demoteNote"/>. Otherwise all
        /// survivors place by the given best-first order. Eliminated follow in
        /// reverse elimination order. Reusable by finale-capable games.
        /// </summary>
        protected RoundResult CrownResult(IEnumerable<Actor> survivorsBestFirst,
            bool forceSingle, string demoteNote)
        {
            var survivors = survivorsBestFirst.ToList();
            var res = new RoundResult { Game = Id };
            int place = 1;

            if (forceSingle && survivors.Count > 1)
            {
                res.Ranking.Add(new RankEntry(survivors[0].Id, place++, true, Note(survivors[0].Id)));
                res.SurvivorIds.Add(survivors[0].Id);
                for (int i = 1; i < survivors.Count; i++)
                    res.Ranking.Add(new RankEntry(survivors[i].Id, place++, false, demoteNote));
            }
            else
            {
                foreach (var a in survivors)
                {
                    res.Ranking.Add(new RankEntry(a.Id, place++, true, Note(a.Id)));
                    res.SurvivorIds.Add(a.Id);
                }
            }

            for (int i = _elimOrder.Count - 1; i >= 0; i--)
                res.Ranking.Add(new RankEntry(_elimOrder[i], place++, false, Note(_elimOrder[i])));

            return res;
        }

        /// <summary>Applies control-inverting curses (reverse, dizzy) to raw input.</summary>
        protected (float, float) ResolveInput(Actor a)
        {
            float dx = a.InDx, dy = a.InDy;
            if (a.PuReverseT > 0f) { dx = -dx; dy = -dy; }
            if (a.PuDizzyT > 0f)
            {
                float ang = (float)Math.Sin(Elapsed * DizzyFreq) * DizzyAmp;
                float c = (float)Math.Cos(ang), s = (float)Math.Sin(ang);
                float rx = dx * c - dy * s;
                float ry = dx * s + dy * c;
                dx = rx; dy = ry;
            }
            return (dx, dy);
        }

        // ── Elimination & result ─────────────────────────────────────────
        protected void Eliminate(Actor a, string note = null)
        {
            if (!a.Alive) return;
            a.Alive = false;
            a.Anim = AnimState.Dead;
            a.Frozen = false;
            _elimOrder.Add(a.Id);
            if (note != null) _notes[a.Id] = note;
            Emit(new Effect(EffectKind.Death, a.Pos.X, a.Pos.Y));
        }

        /// <summary>Records a survivor's finishing order (races/finish lines).</summary>
        protected void MarkFinished(Actor a)
        {
            if (!_finishOrder.Contains(a.Id)) _finishOrder.Add(a.Id);
        }

        protected void Emit(Effect e)
        {
            _fx.Add(e);
            Ctx.EmitFx?.Invoke(e);
        }

        public virtual void Forfeit(string actorId)
        {
            var a = Find(actorId);
            if (a != null) Eliminate(a, "Left the game");
        }

        public virtual RoundResult Result()
        {
            var result = new RoundResult { Game = Id };
            var survivors = Actors.Where(a => a.Alive).ToList();
            result.SurvivorIds = survivors.Select(a => a.Id).ToList();

            // Survivors ranked first: finishers (in finish order) then remaining alive.
            var ordered = new List<Actor>();
            foreach (var id in _finishOrder)
            {
                var a = survivors.FirstOrDefault(s => s.Id == id);
                if (a != null) ordered.Add(a);
            }
            foreach (var a in survivors)
                if (!ordered.Contains(a)) ordered.Add(a);

            int place = 1;
            foreach (var a in ordered)
                result.Ranking.Add(new RankEntry(a.Id, place++, true, Note(a.Id)));

            // Eliminated come next, in reverse elimination order (last out ranks higher).
            for (int i = _elimOrder.Count - 1; i >= 0; i--)
            {
                string id = _elimOrder[i];
                result.Ranking.Add(new RankEntry(id, place++, false, Note(id)));
            }
            return result;
        }

        private string Note(string id) => _notes.TryGetValue(id, out var n) ? n : null;

        // ── Snapshot ─────────────────────────────────────────────────────
        public virtual Snapshot BuildSnapshot()
        {
            List<Effect> fx = _fx.Count > 0 ? new List<Effect>(_fx) : null;
            _fx.Clear();
            return new Snapshot
            {
                Game = Id,
                T = Elapsed * 1000.0,
                Actors = Actors,
                Data = BuildData(),
                Fx = fx
            };
        }

        /// <summary>Per-game snapshot payload (light phase, rope position, …).</summary>
        protected virtual object BuildData() => null;
    }
}
