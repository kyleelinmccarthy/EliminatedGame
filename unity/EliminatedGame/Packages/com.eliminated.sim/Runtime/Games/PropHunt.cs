using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Prop Hunt — deadly hide &amp; seek. Hiders disguise as props among identical
    /// decoys; one Seeker stalks with a blade that only swings so many times. Hold
    /// still to blend in; move and you twitch (a tell). The Seeker must hit a quota
    /// or get boxed too. Ported from lib/server/games/PropHunt.ts.
    /// </summary>
    public sealed class PropHunt : ArenaGame
    {
        private static readonly string[] PropKinds = { "crate", "barrel", "jar", "bush", "stool", "rock" };
        private const float HideTime = 7f;
        private static readonly float SwordReach = Constants.PlayerRadius * 2.7f;
        private const float SwingCd = 0.55f;
        private const float SeekerSpeed = 1.06f;
        private const float CreepSpeed = 0.5f;
        private const float ExposeTime = 1.3f;
        private const float MoveEps = 8f;
        private const float EndGrace = 1.4f;

        private sealed class Decoy { public string Id; public float X, Y; public string Kind; public bool Dead; }

        private string _phase = "hide";
        private float _timer = HideTime;
        private string _seekerId = "";
        private int _swings, _maxSwings, _quota, _found, _hidersTotal;
        private readonly List<Decoy> _decoys = new List<Decoy>();
        private readonly Dictionary<string, string> _hiderKind = new Dictionary<string, string>();
        private readonly List<(string id, string note)> _elim = new List<(string, string)>();
        private bool _ended, _done;
        private float _goalX, _goalY, _goalRepick;
        private string _goalHiderId = "";

        public PropHunt(GameContext ctx) : base(ctx) { }

        public override GameId Id => GameId.PropHunt;
        public override bool IsDone => _done;
        protected override float DashCooldown => 2.4f;
        public string Phase => _phase;
        public string SeekerId => _seekerId;
        public int Found => _found;
        public int Quota => _quota;
        public int Swings => _swings;

        public override void Start()
        {
            Elapsed = 0f;
            var ps = Rng.Shuffle(Actors);
            var seeker = ps[0];
            var hiders = ps.Skip(1).ToList();
            _hidersTotal = hiders.Count;
            _seekerId = seeker.Id;

            int d = MathUtil.Clamp((int)Math.Round(_hidersTotal * 1.3f) + 5, 7, 40);
            const float margin = 110f;
            for (int i = 0; i < d; i++)
                _decoys.Add(new Decoy
                {
                    Id = "decoy_" + i,
                    X = margin + Rng.NextFloat() * (Constants.ArenaW - margin * 2f),
                    Y = margin + Rng.NextFloat() * (Constants.ArenaH - margin * 2f),
                    Kind = PropKinds[Rng.NextInt(PropKinds.Length)]
                });
            var kindsPresent = _decoys.Select(x => x.Kind).Distinct().ToList();

            seeker.Pos = new Vec2(Constants.ArenaW / 2f, Constants.ArenaH / 2f);
            seeker.It = true;
            seeker.Facing = (float)Math.PI / 2f;

            foreach (var h in hiders)
            {
                h.Pos = new Vec2(margin + Rng.NextFloat() * (Constants.ArenaW - margin * 2f),
                                 margin + Rng.NextFloat() * (Constants.ArenaH - margin * 2f));
                string kind = kindsPresent[Rng.NextInt(kindsPresent.Count)];
                _hiderKind[h.Id] = kind;
                h.Set("nerve", Rng.NextFloat());
                var sameKind = _decoys.Where(x => x.Kind == kind).ToList();
                var spot = sameKind.Count > 0 ? sameKind[Rng.NextInt(sameKind.Count)] : _decoys[Rng.NextInt(_decoys.Count)];
                h.Set("spotX", spot.X + (Rng.NextFloat() - 0.5f) * 70f);
                h.Set("spotY", spot.Y + (Rng.NextFloat() - 0.5f) * 70f);
            }

            _maxSwings = MathUtil.Clamp((int)Math.Round(_hidersTotal * (0.4f + Ctx.Intensity * 0.4f)), 2, Math.Max(2, _hidersTotal));
            _swings = _maxSwings;
            _quota = Math.Max(1, Math.Min(Math.Min(1 + (int)(Ctx.Intensity * 2f), _maxSwings), _hidersTotal));
        }

        private List<Actor> AliveHiders() => Actors.Where(a => a.Alive && !a.It).ToList();

        public override void OnInput(string actorId, GameInput input)
        {
            var a = Find(actorId);
            if (a == null || !a.Alive) return;
            switch (input.Kind)
            {
                case InputKind.Move: a.InDx = input.Dx; a.InDy = input.Dy; break;
                case InputKind.Action:
                    if (input.Name == "swing") TrySwing(a);
                    else if (input.Name == "dash") TryDash(a);
                    break;
            }
        }

        public override void Tick(float dt)
        {
            if (_done) return;
            Elapsed += dt;
            _timer -= dt;
            var seeker = Find(_seekerId);

            if (_phase == "hide")
            {
                foreach (var a in Actors)
                {
                    if (!a.Alive) continue;
                    UpdateStatus(a, dt);
                    if (a.It) { a.InDx = 0f; a.InDy = 0f; a.Anim = AnimState.Idle; continue; }
                    if (a.IsBot) BotHide(a);
                    MoveAt(a, dt, 1f);
                }
                if (_timer <= 0f) StartHunt();
                return;
            }

            // hunt
            if (seeker != null && seeker.Get("swingCd") > 0f) seeker.Set("swingCd", Math.Max(0f, seeker.Get("swingCd") - dt));
            if (seeker != null && seeker.Progress > 0f) seeker.Progress = Math.Max(0f, seeker.Progress - dt / 0.3f);

            foreach (var a in Actors)
            {
                if (!a.Alive) continue;
                UpdateStatus(a, dt);
                if (a.It)
                {
                    if (a.IsBot) BotSeek(a, dt);
                    if (a.DashT > 0f) MoveActor(a, dt); else MoveAt(a, dt, SeekerSpeed);
                    continue;
                }
                if (a.IsBot) BotHide(a);
                bool dashing = a.DashT > 0f;
                if (dashing) MoveActor(a, dt); else MoveAt(a, dt, CreepSpeed);
                bool moving = dashing || a.Vel.Length > MoveEps;
                a.Set("exposed", moving ? ExposeTime : Math.Max(0f, a.Get("exposed") - dt));
            }

            if (AliveHiders().Count == 0) { EndHunt(); return; }
            if (_timer <= 0f) { EndHunt(); return; }
        }

        private (string kind, Actor hider, Decoy decoy, float d)? NearestProp(Actor a)
        {
            (string kind, Actor hider, Decoy decoy, float d)? best = null;
            foreach (var h in AliveHiders())
            {
                float dd = Vec2.Distance(a.Pos, h.Pos);
                if (dd <= SwordReach && (best == null || dd < best.Value.d)) best = ("hider", h, null, dd);
            }
            foreach (var dc in _decoys)
            {
                if (dc.Dead) continue;
                float dd = Vec2.Distance(a.Pos, new Vec2(dc.X, dc.Y));
                if (dd <= SwordReach && (best == null || dd < best.Value.d)) best = ("decoy", null, dc, dd);
            }
            return best;
        }

        private void TrySwing(Actor a)
        {
            if (a.Id != _seekerId || _phase != "hunt" || _ended) return;
            if (_swings <= 0 || a.Get("swingCd") > 0f) return;
            a.Set("swingCd", SwingCd);
            a.Progress = 1f;
            var target = NearestProp(a);
            if (target == null) { Emit(new Effect(EffectKind.Spark, a.Pos.X, a.Pos.Y)); return; }
            _swings--;
            if (target.Value.kind == "hider") FindHider(target.Value.hider);
            else { target.Value.decoy.Dead = true; Emit(new Effect(EffectKind.Shatter, target.Value.decoy.X, target.Value.decoy.Y)); }
            if (_swings <= 0 && !_ended) _timer = Math.Min(_timer, EndGrace);
        }

        private void FindHider(Actor h)
        {
            h.Alive = false; h.Anim = AnimState.Dead; h.InDx = 0f; h.InDy = 0f;
            _found++;
            _elim.Add((h.Id, "Skewered mid-disguise!"));
            Emit(new Effect(EffectKind.Death, h.Pos.X, h.Pos.Y));
            if (AliveHiders().Count == 0 && !_ended) _timer = Math.Min(_timer, EndGrace);
        }

        private void StartHunt()
        {
            _phase = "hunt";
            int hiderCount = AliveHiders().Count;
            _timer = MathUtil.Clamp(20f + hiderCount * 1.2f, 22f, 42f);
            foreach (var a in Actors)
            {
                if (a.It || !a.Alive) continue;
                a.Carrying = _hiderKind.TryGetValue(a.Id, out var k) ? k : null;
                a.Anim = AnimState.Idle; a.InDx = 0f; a.InDy = 0f;
                a.Set("exposed", 0f);
            }
            _goalRepick = 0f;
        }

        private void EndHunt()
        {
            if (_ended) return;
            _ended = true;
            var survivingHiders = AliveHiders();
            var seeker = Find(_seekerId);
            bool seekerSurvives = _found >= _quota || survivingHiders.Count == 0 || _hidersTotal == 0;
            if (!seekerSurvives && seeker != null && seeker.Alive)
            {
                seeker.Alive = false; seeker.Anim = AnimState.Dead; seeker.It = false;
                _elim.Insert(0, (_seekerId, $"Found only {_found}/{_quota}. Disappointing."));
                Emit(new Effect(EffectKind.Death, seeker.Pos.X, seeker.Pos.Y));
            }
            _done = true;
        }

        private void BotHide(Actor a)
        {
            if (_phase == "hide")
            {
                float tx = a.Get("spotX", a.Pos.X), ty = a.Get("spotY", a.Pos.Y);
                float d = Vec2.Distance(a.Pos, new Vec2(tx, ty));
                if (d < 14f) { a.InDx = 0f; a.InDy = 0f; }
                else { a.InDx = (tx - a.Pos.X) / d; a.InDy = (ty - a.Pos.Y) / d; }
                return;
            }
            var seeker = Find(_seekerId);
            if (seeker != null && seeker.Alive)
            {
                float dd = Vec2.Distance(a.Pos, seeker.Pos);
                if (dd < 190f && a.Get("nerve") > 0.55f && Rng.NextFloat() < 0.45f)
                {
                    float m = dd <= 0f ? 1f : dd;
                    a.InDx = (a.Pos.X - seeker.Pos.X) / m; a.InDy = (a.Pos.Y - seeker.Pos.Y) / m;
                    return;
                }
            }
            a.InDx = 0f; a.InDy = 0f;
        }

        private Actor NearestHider(Actor a, List<Actor> list)
        {
            Actor best = null; float bd = float.MaxValue;
            foreach (var h in list) { float d = Vec2.SqrDistance(a.Pos, h.Pos); if (d < bd) { bd = d; best = h; } }
            return best;
        }

        private void BotSeek(Actor a, float dt)
        {
            var hiders = AliveHiders();
            _goalRepick -= dt;
            if (hiders.Count == 0)
            {
                a.InDx = (float)Math.Sin(Elapsed + a.Pos.Y) * 0.4f;
                a.InDy = (float)Math.Cos(Elapsed + a.Pos.X) * 0.4f;
                return;
            }
            var tracked = !string.IsNullOrEmpty(_goalHiderId) ? Find(_goalHiderId) : null;
            bool needNewGoal = _goalRepick <= 0f || (!string.IsNullOrEmpty(_goalHiderId) && (tracked == null || !tracked.Alive));
            if (needNewGoal)
            {
                _goalRepick = 0.6f + Rng.NextFloat() * 0.5f;
                float acc = 0.5f + Ctx.Intensity * 0.3f;
                var exposed = hiders.Where(h => h.Get("exposed") > 0f).ToList();
                Actor chosen = null;
                if (exposed.Count > 0 && Rng.NextFloat() < 0.85f) chosen = NearestHider(a, exposed);
                else if (Rng.NextFloat() < acc) chosen = NearestHider(a, hiders);

                if (chosen != null) { _goalHiderId = chosen.Id; _goalX = chosen.Pos.X; _goalY = chosen.Pos.Y; }
                else
                {
                    var live = _decoys.Where(x => !x.Dead).ToList();
                    var dc = live.Count > 0 ? live[Rng.NextInt(live.Count)] : null;
                    _goalHiderId = "";
                    _goalX = dc?.X ?? a.Pos.X; _goalY = dc?.Y ?? a.Pos.Y;
                }
            }
            if (!string.IsNullOrEmpty(_goalHiderId))
            {
                var h = Find(_goalHiderId);
                if (h != null && h.Alive) { _goalX = h.Pos.X; _goalY = h.Pos.Y; }
            }

            float gd = Vec2.Distance(a.Pos, new Vec2(_goalX, _goalY));
            if (gd > 6f) { a.InDx = (_goalX - a.Pos.X) / gd; a.InDy = (_goalY - a.Pos.Y) / gd; }
            else { a.InDx = 0f; a.InDy = 0f; }

            if (a.Get("swingCd") <= 0f && _swings > 0 && NearestProp(a) != null)
            {
                bool committed = !string.IsNullOrEmpty(_goalHiderId)
                    ? tracked != null && gd <= SwordReach
                    : gd <= SwordReach * 1.2f;
                if (committed) TrySwing(a);
            }
        }

        public override void Forfeit(string actorId)
        {
            var a = Find(actorId);
            bool wasAlive = a != null && a.Alive;
            if (a != null) { a.Alive = false; a.Anim = AnimState.Dead; }
            if (!wasAlive) return;
            if (actorId == _seekerId) { if (!_ended) { _ended = true; _done = true; } }
            else _elim.Add((actorId, "Bailed mid-disguise."));
        }

        public override RoundResult Result()
        {
            var survivors = AliveHiders().Select(a => a.Id).ToList();
            var seeker = Find(_seekerId);
            if (seeker != null && seeker.Alive) survivors.Add(_seekerId);
            return RankingUtil.Build(Id, survivors, _elim);
        }

        protected override object BuildData() => new PropData
        {
            Phase = _phase,
            TimeLeft = Math.Max(0f, _timer),
            SeekerId = _seekerId,
            Swings = _swings, MaxSwings = _maxSwings, Quota = _quota, Found = _found,
            HidersLeft = AliveHiders().Count,
            Night = Ctx.Night,
            Decoys = _decoys.Where(x => !x.Dead).Select(x => new DecoyView { Id = x.Id, X = x.X, Y = x.Y, Kind = x.Kind }).ToList()
        };

        public sealed class PropData
        {
            public string Phase, SeekerId;
            public float TimeLeft;
            public int Swings, MaxSwings, Quota, Found, HidersLeft;
            public bool Night;
            public List<DecoyView> Decoys;
        }
        public struct DecoyView { public string Id; public float X, Y; public string Kind; }
    }
}
