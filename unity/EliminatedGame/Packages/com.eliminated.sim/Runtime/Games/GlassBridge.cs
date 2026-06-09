using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Glass Stepping Stones — one shared bridge, one hidden safe side per row.
    /// Blobs cross one at a time in line order, guessing LEFT/RIGHT for the next
    /// row. Correct advances the frontier and reveals the row; wrong shatters
    /// (unless you're the last, who gets a lucky catch). Ported from
    /// lib/server/games/GlassBridge.ts. Finale-capable via single survivor.
    /// </summary>
    public sealed class GlassBridge : IMinigame
    {
        private const float TurnTime = 6f;
        private const float ResolveTime = 1.1f;
        private const float StepTime = 0.5f;
        private const float TimeLimit = 110f;

        private sealed class Walker
        {
            public string Id;
            public bool IsBot;
            public bool Alive = true;
            public bool Finished;
            public float BotThink;
        }

        private readonly GameContext _ctx;
        private readonly Rng _rng;
        private readonly Dictionary<string, Walker> _walkers = new Dictionary<string, Walker>();
        private readonly List<string> _order = new List<string>();
        private readonly List<int> _pattern = new List<int>();
        private readonly List<bool> _revealed = new List<bool>();
        private readonly List<(string id, string note)> _elim = new List<(string, string)>();
        private readonly List<Effect> _fx = new List<Effect>();

        private int _rows = 8;
        private int _frontier;
        private string _phase = "choose";
        private float _timer;
        private int _turnPtr;
        private string _activeId = "";
        private float _elapsed;
        private bool _done;

        public GlassBridge(GameContext ctx) { _ctx = ctx; _rng = ctx.Rng; }

        public GameId Id => GameId.GlassBridge;
        public bool IsDone => _done;

        // Inspection (view + tests)
        public int Rows => _rows;
        public int Frontier => _frontier;
        public string ActiveId => _activeId;
        public string CurrentPhase => _phase;
        public int SafeSide(int row) => _pattern[row];

        public void Start()
        {
            int n = _ctx.Actors.Count;
            _rows = MathUtil.Clamp((int)System.Math.Round(n * (0.7f + _ctx.Intensity * 0.9f)), 4, 12);
            for (int r = 0; r < _rows; r++)
            {
                _pattern.Add(_rng.NextFloat() < 0.5f ? 0 : 1);
                _revealed.Add(false);
            }
            foreach (var id in _rng.Shuffle(_ctx.Actors.Select(a => a.Id).ToList())) _order.Add(id);
            foreach (var a in _ctx.Actors)
                _walkers[a.Id] = new Walker { Id = a.Id, IsBot = a.IsBot };
            BeginTurn(0);
        }

        private int AliveCount() => _walkers.Values.Count(w => w.Alive && !w.Finished);

        private string NextActive(int from)
        {
            for (int i = 0; i < _order.Count; i++)
            {
                int idx = (from + i) % _order.Count;
                var w = _walkers[_order[idx]];
                if (w.Alive && !w.Finished) { _turnPtr = idx; return w.Id; }
            }
            return null;
        }

        private void BeginTurn(int from)
        {
            if (_frontier >= _rows) { FinishAllAlive(); return; }
            var id = NextActive(from);
            if (id == null) { _done = true; return; }
            _activeId = id;
            if (_revealed[_frontier]) { _phase = "step"; _timer = StepTime; return; }
            _phase = "choose";
            _timer = TurnTime;
            var w = _walkers[id];
            if (w.IsBot) w.BotThink = 0.6f + _rng.NextFloat() * 1.5f;
        }

        private void FinishAllAlive()
        {
            foreach (var w in _walkers.Values)
                if (w.Alive && !w.Finished) w.Finished = true;
            _phase = "done";
            _done = true;
        }

        public void OnInput(string playerId, GameInput input)
        {
            if (input.Kind != InputKind.Choose) return;
            if (_phase != "choose" || playerId != _activeId) return;
            int side = input.Value == "R" ? 1 : 0;
            ResolveGuess(side);
        }

        private void ResolveGuess(int side)
        {
            var w = _walkers[_activeId];
            if (!w.Alive || w.Finished) return;
            int row = _frontier;
            int safe = _pattern[row];
            _revealed[row] = true;

            if (side == safe)
            {
                _frontier++;
                _fx.Add(new Effect(EffectKind.Spark, 0f, row));
            }
            else if (AliveCount() <= 1)
            {
                _frontier++; // lucky catch for the last blob
                _fx.Add(new Effect(EffectKind.Spark, 0f, row));
            }
            else
            {
                w.Alive = false;
                SyncActor(w.Id, false);
                _elim.Add((w.Id, $"Shattered the glass at row {row + 1}"));
                _fx.Add(new Effect(EffectKind.Shatter, 0f, row));
            }
            _phase = "resolve";
            _timer = ResolveTime;
        }

        public void Tick(float dt)
        {
            if (_done) return;
            _elapsed += dt;
            _timer -= dt;

            if (_phase == "choose")
            {
                var w = _walkers[_activeId];
                if (w.IsBot)
                {
                    w.BotThink -= dt;
                    if (w.BotThink <= 0f) { ResolveGuess(_rng.NextFloat() < 0.5f ? 0 : 1); return; }
                }
                if (_timer <= 0f) ResolveGuess(_rng.NextFloat() < 0.5f ? 0 : 1);
                return;
            }
            if (_phase == "step")
            {
                if (_timer <= 0f)
                {
                    _frontier++;
                    _fx.Add(new Effect(EffectKind.Spark, 0f, _frontier - 1));
                    BeginTurn(_turnPtr);
                }
                return;
            }
            if (_phase == "resolve")
            {
                if (_timer <= 0f) BeginTurn(_turnPtr + 1);
                return;
            }
            if (_elapsed >= TimeLimit) FinishAllAlive();
        }

        public void Forfeit(string playerId)
        {
            if (!_walkers.TryGetValue(playerId, out var w) || !w.Alive || w.Finished) return;
            w.Alive = false;
            SyncActor(playerId, false);
            _elim.Add((playerId, "Bailed off the bridge"));
            if (playerId == _activeId && _phase != "done")
            {
                if (AliveCount() == 0) _done = true;
                else BeginTurn(_turnPtr + 1);
            }
        }

        private void SyncActor(string id, bool alive)
        {
            var a = _ctx.Actors.FirstOrDefault(x => x.Id == id);
            if (a != null) a.Alive = alive;
        }

        public RoundResult Result()
        {
            var survivors = _order.Where(id => { var w = _walkers[id]; return w.Finished || w.Alive; }).ToList();
            foreach (var w in _walkers.Values) SyncActor(w.Id, w.Finished || w.Alive);
            return RankingUtil.Build(Id, survivors, _elim);
        }

        public Snapshot BuildSnapshot()
        {
            var fx = _fx.Count > 0 ? new List<Effect>(_fx) : null;
            _fx.Clear();
            return new Snapshot
            {
                Game = Id,
                T = _elapsed * 1000.0,
                Actors = _ctx.Actors,
                Fx = fx,
                Data = new GlassData
                {
                    Rows = _rows,
                    Frontier = _frontier,
                    Phase = _phase,
                    ActiveId = _activeId,
                    TurnTimeLeft = _phase == "choose" ? System.Math.Max(0f, _timer) : 0f,
                    RevealedSides = _pattern.Select((s, r) => _revealed[r] ? s : -1).ToList()
                }
            };
        }

        public sealed class GlassData
        {
            public int Rows;
            public int Frontier;
            public string Phase;
            public string ActiveId;
            public float TurnTimeLeft;
            public List<int> RevealedSides;
        }
    }
}
