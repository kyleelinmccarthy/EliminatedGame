using System;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Horizontal race toward the Doll: move on green, freeze on red. Moving
    /// after the brief red grace is lethal. Reuses the base class's finisher/
    /// elimination ranking. Ported from lib/server/games/RedLightGreenLight.ts.
    /// </summary>
    public sealed class RedLightGreenLight : ArenaGame
    {
        public const float StartX = 90f;
        public static readonly float FinishX = Constants.ArenaW - 120f; // 1160
        private const float TimeLimit = 70f;
        private const float Grace = 0.38f;   // s after red before detection is lethal
        private const float MoveEps = 12f;   // units/s considered "moving"
        private const float RaceSpeed = 150f; // slower than the shared 240 on purpose

        private enum Light { Green, Red }

        private Light _light = Light.Green;
        private float _phaseTime;
        private float _phaseDur = 2.2f;
        private float _redLethalIn;
        private bool _done;

        public RedLightGreenLight(GameContext ctx) : base(ctx) { }

        public override GameId Id => GameId.RedLight;
        public override bool IsDone => _done;
        protected override float MoveSpeed => RaceSpeed;

        public bool IsRed => _light == Light.Red;
        public bool Lethal => _light == Light.Red && _redLethalIn <= 0f;

        public override void Start()
        {
            Elapsed = 0f;
            int n = Actors.Count;
            float spacing = Math.Min(110f, (Constants.ArenaH - 160f) / Math.Max(1, n));
            float startY = Constants.ArenaH / 2f - spacing * (n - 1) / 2f;
            for (int i = 0; i < n; i++)
            {
                var a = Actors[i];
                a.Pos = new Vec2(StartX, startY + i * spacing);
                a.Facing = 0f; // facing right
                a.Set("react", 0.12f + Rng.NextFloat() * 0.5f);
                a.Set("reckless", Rng.NextFloat() < 0.25f ? 1f : 0f);
                a.Set("finished", 0f);
                a.Set("stopTimer", 0f);
            }
            NextLight(first: true);
        }

        private void NextLight(bool first = false)
        {
            if (first || _light == Light.Red)
            {
                _light = Light.Green;
                _phaseDur = 1.6f + Rng.NextFloat() * 2.0f;
            }
            else
            {
                _light = Light.Red;
                _phaseDur = 1.2f + Rng.NextFloat() * 1.7f;
                _redLethalIn = Grace;
                Emit(new Effect(EffectKind.Ring, FinishX + 30f, Constants.ArenaH / 2f, 2f));
            }
            _phaseTime = 0f;
        }

        public override void Tick(float dt)
        {
            if (_done) return;
            Elapsed += dt;
            _phaseTime += dt;
            if (_redLethalIn > 0f) _redLethalIn = Math.Max(0f, _redLethalIn - dt);
            if (_phaseTime >= _phaseDur) NextLight();

            bool lethal = Lethal;

            foreach (var a in Actors)
            {
                if (!a.Alive || a.Get("finished") > 0f) continue;
                if (a.IsBot) BotThink(a);
                MoveActor(a, dt);

                if (a.Pos.X >= FinishX)
                {
                    a.Set("finished", 1f);
                    a.Anim = AnimState.Cheer;
                    a.InDx = 0f; a.InDy = 0f;
                    a.Pos = new Vec2(FinishX, a.Pos.Y);
                    a.Progress = 1f;
                    MarkFinished(a);
                    Emit(new Effect(EffectKind.Confetti, a.Pos.X, a.Pos.Y));
                    continue;
                }

                a.Progress = MathUtil.Clamp01((a.Pos.X - StartX) / (FinishX - StartX));

                if (lethal && a.Vel.Length > MoveEps)
                    Eliminate(a, "Caught moving!");
            }

            var active = Actors.Where(a => a.Alive && a.Get("finished") <= 0f).ToList();
            if (active.Count == 0) _done = true;
            if (Elapsed >= TimeLimit)
            {
                foreach (var a in active) Eliminate(a, "Out of time!");
                _done = true;
            }
        }

        protected override void BotThink(Actor a)
        {
            if (_light == Light.Green)
            {
                a.InDx = 1f;
                a.InDy = (float)Math.Sin(Elapsed * 2f + a.Pos.Y) * 0.15f; // wiggle
                a.Set("stopTimer", 0f);
            }
            else
            {
                float st = a.Get("stopTimer") + Constants.Dt;
                a.Set("stopTimer", st);
                float react = a.Get("reckless") > 0f ? a.Get("react") + 0.25f : a.Get("react");
                if (st >= react)
                {
                    a.InDx = 0f;
                    a.InDy = 0f;
                }
            }
        }

        protected override object BuildData() => new RlglData
        {
            Red = _light == Light.Red,
            Lethal = Lethal,
            FinishX = FinishX,
            TimeLeft = Math.Max(0f, TimeLimit - Elapsed)
        };

        /// <summary>Per-tick payload the client renders.</summary>
        public sealed class RlglData
        {
            public bool Red;
            public bool Lethal;
            public float FinishX;
            public float TimeLeft;
        }
    }
}
