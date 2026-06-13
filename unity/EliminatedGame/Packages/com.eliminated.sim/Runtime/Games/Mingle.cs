using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Mingle — everyone mills on a central spinning platform; when a number is
    /// called, scramble off into the ring rooms in groups of exactly that size.
    /// Wrong size (or still on the platform) = out. Ported from
    /// lib/server/games/Mingle.ts + shared/mingle.ts.
    /// </summary>
    public sealed class Mingle : ArenaGame
    {
        public enum MinglePhase { Wander, Mingle, Flash }
        public struct Room { public float X, Y, R; }

        // Layout (shared/mingle.ts)
        public static readonly float PlatformX = Constants.ArenaW / 2f;
        public static readonly float PlatformY = Constants.ArenaH / 2f;
        public const float PlatformR = 112f;
        private const int RoomCount = 4;        // one per CORNER — with 12 players a small call can't save all
        private const float RoomRadius = 92f;
        private const float WanderTime = 4.5f;
        private const float SpinRate = 0.7f;    // platform (and the riders on it) angular speed, rad/s

        // The four corner "safe" rooms, set well out from the central platform so the scramble is a
        // real sprint and a small called number leaves players stranded.
        private static readonly (float x, float y)[] CornerRooms =
        {
            (245f, 168f), (1035f, 168f), (245f, 552f), (1035f, 552f),
        };

        private readonly List<Room> _rooms = new List<Room>();
        private MinglePhase _phase = MinglePhase.Wander;
        private float _timer;
        private int _callN = 2;
        private int _round;
        private int _startCount;
        private float _spin;
        private bool _done;
        private int[] _lastCounts = Array.Empty<int>();

        public Mingle(GameContext ctx) : base(ctx) { }

        public override GameId Id => GameId.Mingle;
        public override bool IsDone => _done;
        protected override float DashCooldown => 1.8f;
        public MinglePhase CurrentPhase => _phase;
        public int CallN => _callN;
        public IReadOnlyList<Room> Rooms => _rooms;

        public override void Start()
        {
            Elapsed = 0f;
            _startCount = Actors.Count;
            for (int i = 0; i < RoomCount; i++)
                _rooms.Add(new Room { X = CornerRooms[i].x, Y = CornerRooms[i].y, R = RoomRadius });
            GatherOnPlatform();
            _phase = MinglePhase.Wander;
            _timer = WanderTime;
        }

        private List<Actor> Alive => Actors.Where(a => a.Alive).ToList();

        private void GatherOnPlatform()
        {
            var alive = Alive;
            for (int i = 0; i < alive.Count; i++)
            {
                float ang = (i / (float)Math.Max(1, alive.Count)) * 6.2831853f;
                float rad = PlatformR * 0.6f;
                alive[i].Pos = new Vec2(PlatformX + (float)Math.Cos(ang) * rad, PlatformY + (float)Math.Sin(ang) * rad);
                alive[i].InDx = 0f; alive[i].InDy = 0f; alive[i].Vel = Vec2.Zero;
            }
        }

        // Ride the spinning carousel: keep the player's radius from the centre but rotate their
        // angle by the platform's spin, so they travel WITH it (and stay on it). No free movement.
        private void RideCarousel(Actor a, float dt)
        {
            float dx = a.Pos.X - PlatformX, dy = a.Pos.Y - PlatformY;
            float rad = Math.Min((float)Math.Sqrt(dx * dx + dy * dy), PlatformR - Constants.PlayerRadius * a.Scale);
            float ang = (float)Math.Atan2(dy, dx) + SpinRate * dt;
            a.Pos = new Vec2(PlatformX + (float)Math.Cos(ang) * rad, PlatformY + (float)Math.Sin(ang) * rad);
            a.InDx = 0f; a.InDy = 0f; a.Vel = Vec2.Zero;
            a.Facing = ang + (float)Math.PI * 0.5f; // face the direction of travel
        }

        private void ConfineToPlatform(Actor a)
        {
            float maxR = PlatformR - Constants.PlayerRadius * a.Scale;
            float dx = a.Pos.X - PlatformX, dy = a.Pos.Y - PlatformY;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d > maxR)
            {
                float m = d <= 0f ? 1f : d;
                a.Pos = new Vec2(PlatformX + dx / m * maxR, PlatformY + dy / m * maxR);
                a.Vel = Vec2.Zero;
            }
        }

        private int RoomOf(Actor a)
        {
            for (int i = 0; i < _rooms.Count; i++)
                if (Vec2.Distance(a.Pos, new Vec2(_rooms[i].X, _rooms[i].Y)) <= _rooms[i].R) return i;
            return -1;
        }

        private int[] RoomCounts()
        {
            var counts = new int[_rooms.Count];
            foreach (var a in Alive)
            {
                int ri = RoomOf(a);
                if (ri >= 0) counts[ri]++;
            }
            return counts;
        }

        public override void Tick(float dt)
        {
            if (_done) return;
            Elapsed += dt;
            _timer -= dt;
            _spin += dt * SpinRate;

            foreach (var a in Actors)
            {
                if (!a.Alive) continue;
                UpdateStatus(a, dt);
                if (_phase == MinglePhase.Wander)
                {
                    // Music's playing: everyone simply RIDES the spinning platform (orbits the centre
                    // with it) — no frantic darting. The scramble only starts when the number is called.
                    RideCarousel(a, dt);
                }
                else
                {
                    if (a.IsBot) BotThink(a);
                    MoveActor(a, dt);
                }
            }

            if (_phase == MinglePhase.Wander) { if (_timer <= 0f) BeginMingle(); }
            else if (_phase == MinglePhase.Mingle) { _lastCounts = RoomCounts(); if (_timer <= 0f) Evaluate(); }
            else if (_phase == MinglePhase.Flash) { if (_timer <= 0f) AfterFlash(); }
        }

        private void BeginMingle()
        {
            _round++;
            int alive = Alive.Count;
            var choices = new[] { 2, 3, 4 }.Where(n => n < alive).ToList();
            _callN = choices.Count > 0 ? choices[Rng.NextInt(choices.Count)] : 2;
            _phase = MinglePhase.Mingle;
            _timer = Math.Max(4.5f, 7f - _round * 0.5f);
            Emit(new Effect(EffectKind.Ring, Constants.ArenaW / 2f, Constants.ArenaH / 2f, 3f));
        }

        private void Evaluate()
        {
            var counts = RoomCounts();
            var alive = Alive;
            var doomed = new List<Actor>();
            foreach (var a in alive)
            {
                int ri = RoomOf(a);
                if (ri >= 0 && counts[ri] == _callN) Emit(new Effect(EffectKind.Confetti, a.Pos.X, a.Pos.Y));
                else doomed.Add(a);
            }
            if (doomed.Count == alive.Count && doomed.Count > 0)
            {
                Emit(new Effect(EffectKind.Confetti, doomed[0].Pos.X, doomed[0].Pos.Y));
                doomed.RemoveAt(0);
            }
            foreach (var a in doomed)
            {
                int ri = RoomOf(a);
                string note = ri < 0 ? "Stuck on the platform!" : (counts[ri] < _callN ? "Too few!" : "Too many!");
                Eliminate(a, note);
            }
            _lastCounts = counts;
            _phase = MinglePhase.Flash;
            _timer = 2.0f;
        }

        private void AfterFlash()
        {
            int alive = Alive.Count;
            int target = Math.Max(2, (int)Math.Ceiling(_startCount * (1f - 0.5f * Ctx.Intensity)));
            int maxRounds = Ctx.Intensity < 0.4f ? 2 : (Ctx.Intensity < 0.7f ? 3 : 4);
            if (alive <= target || _round >= maxRounds || alive <= 2) _done = true;
            else
            {
                GatherOnPlatform();
                _phase = MinglePhase.Wander;
                _timer = WanderTime;
            }
        }

        protected override void BotThink(Actor a)
        {
            if (_phase != MinglePhase.Mingle)
            {
                a.InDx = (float)Math.Sin(Elapsed * 1.5f + a.Pos.Y) * 0.4f;
                a.InDy = (float)Math.Cos(Elapsed * 1.3f + a.Pos.X) * 0.4f;
                return;
            }
            var counts = RoomCounts();
            int myRoom = RoomOf(a);
            if (myRoom >= 0 && counts[myRoom] == _callN) { a.InDx *= 0.5f; a.InDy *= 0.5f; return; }

            int best = -1; float bestScore = float.NegativeInfinity;
            for (int i = 0; i < _rooms.Count; i++)
            {
                int c = counts[i] - (myRoom == i ? 1 : 0);
                if (c >= _callN) continue;
                float d = Vec2.Distance(a.Pos, new Vec2(_rooms[i].X, _rooms[i].Y));
                float score = c * 200f - d;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            if (best < 0) best = 0;
            var dir = (new Vec2(_rooms[best].X, _rooms[best].Y) - a.Pos).Normalized;
            a.InDx = dir.X; a.InDy = dir.Y;
        }

        protected override object BuildData()
        {
            var counts = _lastCounts.Length > 0 ? _lastCounts : RoomCounts();
            return new MingleData
            {
                Phase = _phase.ToString(),
                N = _callN,
                Round = _round,
                TimeLeft = Math.Max(0f, _timer),
                PlatformX = PlatformX, PlatformY = PlatformY, PlatformR = PlatformR, Spin = _spin,
                Rooms = _rooms.Select((r, i) => new RoomView
                {
                    X = r.X, Y = r.Y, R = r.R,
                    Count = i < counts.Length ? counts[i] : 0,
                    Ok = (i < counts.Length ? counts[i] : 0) == _callN
                }).ToList()
            };
        }

        public sealed class MingleData
        {
            public string Phase;
            public int N, Round;
            public float TimeLeft;
            public float PlatformX, PlatformY, PlatformR, Spin;
            public List<RoomView> Rooms;
        }
        public struct RoomView { public float X, Y, R; public int Count; public bool Ok; }
    }
}
