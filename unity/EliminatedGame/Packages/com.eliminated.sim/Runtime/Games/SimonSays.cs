using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// Simon Says — a barking Game Master issues an order; do the matching move in
    /// time, or on FREEZE touch nothing. Wrong move, too slow, or a twitch on
    /// freeze = out. It only gets faster. Ported from
    /// lib/server/games/SimonSays.ts (+ shared/simon.ts). Finale-capable.
    /// </summary>
    public sealed class SimonSays : IMinigame
    {
        public static readonly string[] Keys = { "head", "nose", "blink", "flip", "jump" };
        public const string FreezeKey = "freeze";
        private const float JudgeDur = 1.15f;

        private sealed class Contestant
        {
            public string Id;
            public bool IsBot;
            public bool Alive = true;
            public string Did;       // null = nothing pressed
            public string ResultKind = "";
            public int SurvivedBeats;
            public float Skill, Reaction, Recklessness;
            public bool Acted;
            public float PlanDelay;
            public string PlanKey;   // null = stay still
        }

        private readonly GameContext _ctx;
        private readonly Rng _rng;
        private readonly Dictionary<string, Contestant> _cs = new Dictionary<string, Contestant>();
        private readonly List<(string id, string note)> _elim = new List<(string, string)>();
        private readonly List<Effect> _fx = new List<Effect>();

        private string _phase = "ready";
        private float _phaseTime;
        private float _readyStart = 1.0f, _readyMin = 0.5f, _windowStart = 2.0f, _windowMin = 0.7f, _speedup = 0.92f;
        private float _readyCur, _windowCur;
        private int _beat;
        private string _command = "head";
        private bool _commandFreeze;
        private bool _lastFreeze;
        private string _lastActionKey = "";
        private float _elapsed;
        private bool _done;
        private float _freezeChance = 0.24f;
        private int _target = 1;
        private int _maxBeats = 16;

        public SimonSays(GameContext ctx) { _ctx = ctx; _rng = ctx.Rng; }

        public GameId Id => GameId.SimonSays;
        public bool IsDone => _done;
        public string Phase => _phase;
        public string CommandKey => _command;
        public bool IsFreeze => _commandFreeze;

        public void Start()
        {
            foreach (var a in _ctx.Actors)
            {
                float skill = 0.25f + _rng.NextFloat() * 0.7f;
                _cs[a.Id] = new Contestant
                {
                    Id = a.Id, IsBot = a.IsBot, Skill = skill,
                    Reaction = 0.55f - 0.3f * skill + _rng.NextFloat() * 0.12f,
                    Recklessness = _rng.NextFloat()
                };
            }
            int n = _ctx.Actors.Count;
            float r = MathUtil.Clamp01(_ctx.Intensity);
            _target = _ctx.ForceSingleSurvivor ? 1 : Math.Max(1, (int)Math.Ceiling(n * (1f - 0.55f * _ctx.Intensity)));
            _maxBeats = _ctx.ForceSingleSurvivor ? Math.Max(24, n * 4) : (int)Math.Round(8f + n * 0.6f + r * 12f);
            _freezeChance = 0.22f + 0.12f * r;
            _windowStart = 2.2f - 0.55f * r;
            _windowMin = 0.8f - 0.2f * r;
            _readyStart = 1.1f - 0.25f * r;
            _readyMin = 0.55f - 0.12f * r;
            _speedup = 0.93f - 0.04f * r;
            BeginBeat();
        }

        private void BeginBeat()
        {
            _beat++;
            _readyCur = Math.Max(_readyMin, _readyStart * (float)Math.Pow(_speedup, _beat - 1));
            _windowCur = Math.Max(_windowMin, _windowStart * (float)Math.Pow(_speedup, _beat - 1));
            ChooseCommand();
            foreach (var c in _cs.Values) { c.Did = null; c.ResultKind = ""; c.Acted = false; c.PlanDelay = 0f; c.PlanKey = null; }
            _phase = "ready";
            _phaseTime = 0f;
        }

        private void ChooseCommand()
        {
            bool canFreeze = _beat > 1 && !_lastFreeze;
            if (canFreeze && _rng.NextFloat() < _freezeChance)
            {
                _lastFreeze = true; _commandFreeze = true; _command = FreezeKey; return;
            }
            _lastFreeze = false; _commandFreeze = false;
            string cmd = Keys[_rng.NextInt(Keys.Length)];
            if (cmd == _lastActionKey && _rng.NextFloat() < 0.7f) cmd = Keys[_rng.NextInt(Keys.Length)];
            _lastActionKey = cmd;
            _command = cmd;
        }

        private void EnterCall()
        {
            _phase = "call";
            _phaseTime = 0f;
            PlanBots();
        }

        private void PlanBots()
        {
            float panic = MathUtil.Clamp01((_windowStart - _windowCur) / Math.Max(0.0001f, _windowStart - _windowMin));
            foreach (var c in _cs.Values)
            {
                if (!c.Alive || !c.IsBot) continue;
                c.PlanDelay = c.Reaction * (0.8f + _rng.NextFloat() * 0.5f);
                c.Acted = false;
                if (_commandFreeze)
                {
                    float twitch = MathUtil.Clamp01((0.08f + 0.45f * panic) * (0.55f + 0.85f * c.Recklessness));
                    c.PlanKey = _rng.NextFloat() < twitch ? RandomKey() : null;
                }
                else if (c.PlanDelay >= _windowCur) c.PlanKey = null;
                else
                {
                    float acc = MathUtil.Clamp(0.78f + 0.2f * c.Skill - 0.42f * panic, 0.12f, 0.98f);
                    c.PlanKey = _rng.NextFloat() < acc ? _command : WrongKey(_command);
                }
            }
        }

        private string RandomKey() => Keys[_rng.NextInt(Keys.Length)];
        private string WrongKey(string correct)
        {
            var wrong = Keys.Where(k => k != correct).ToList();
            return wrong[_rng.NextInt(wrong.Count)];
        }

        public void OnInput(string playerId, GameInput input)
        {
            if (input.Kind != InputKind.Choose || _phase != "call") return;
            if (!Keys.Contains(input.Value)) return;
            if (!_cs.TryGetValue(playerId, out var c) || !c.Alive || c.Did != null) return;
            c.Did = input.Value;
        }

        public void Tick(float dt)
        {
            if (_done) return;
            _elapsed += dt;
            _phaseTime += dt;

            if (_phase == "ready") { if (_phaseTime >= _readyCur) EnterCall(); return; }
            if (_phase == "call")
            {
                foreach (var c in _cs.Values)
                {
                    if (!c.Alive || !c.IsBot || c.Acted) continue;
                    if (_phaseTime >= c.PlanDelay) { c.Acted = true; if (c.PlanKey != null && c.Did == null) c.Did = c.PlanKey; }
                }
                if (_phaseTime >= _windowCur) Resolve();
                return;
            }
            if (_phaseTime >= JudgeDur) { if (ShouldEnd()) _done = true; else BeginBeat(); }
        }

        private void Resolve()
        {
            foreach (var c in _cs.Values)
            {
                if (!c.Alive) continue;
                bool survived; string note = "";
                if (_commandFreeze) { survived = c.Did == null; if (!survived) note = "Twitched on FREEZE!"; }
                else if (c.Did == _command) survived = true;
                else if (c.Did == null) { survived = false; note = "Too slow!"; }
                else { survived = false; note = "Wrong move!"; }

                if (survived) { c.ResultKind = "safe"; c.SurvivedBeats = _beat; }
                else
                {
                    c.ResultKind = "out"; c.Alive = false; SyncActor(c.Id, false);
                    _elim.Add((c.Id, note));
                    _fx.Add(new Effect(EffectKind.Death));
                }
            }
            _phase = "judge";
            _phaseTime = 0f;
        }

        private bool ShouldEnd()
        {
            int remaining = _cs.Values.Count(c => c.Alive);
            return remaining <= _target || remaining <= 1 || _beat >= _maxBeats;
        }

        public void Forfeit(string playerId)
        {
            if (!_cs.TryGetValue(playerId, out var c) || !c.Alive) return;
            c.Alive = false; c.ResultKind = "out"; SyncActor(playerId, false);
            _elim.Add((playerId, "Walked off mid-order"));
            if (ShouldEnd()) _done = true;
        }

        private void SyncActor(string id, bool alive)
        {
            var a = _ctx.Actors.FirstOrDefault(x => x.Id == id);
            if (a != null) a.Alive = alive;
        }

        public RoundResult Result()
        {
            var survivors = _cs.Values.Where(c => c.Alive).Select(c => c.Id).ToList();
            return RankingUtil.Crown(Id, survivors, _elim, _ctx.ForceSingleSurvivor, "Still standing at the buzzer");
        }

        public Snapshot BuildSnapshot()
        {
            var fx = _fx.Count > 0 ? new List<Effect>(_fx) : null;
            _fx.Clear();
            bool show = _phase != "ready";
            return new Snapshot
            {
                Game = Id, T = _elapsed * 1000.0, Actors = _ctx.Actors, Fx = fx,
                Data = new SimonData
                {
                    Phase = _phase, Beat = _beat, MaxBeats = _maxBeats,
                    Command = show ? _command : null, Freeze = show && _commandFreeze,
                    React = _phase == "call" ? MathUtil.Clamp01(_phaseTime / _windowCur) : 0f,
                    Contestants = _cs.Values.Select(c => new SimonContestant { Id = c.Id, Alive = c.Alive, Did = c.Did, Result = c.ResultKind }).ToList()
                }
            };
        }

        public sealed class SimonData
        {
            public string Phase, Command;
            public int Beat, MaxBeats;
            public bool Freeze;
            public float React;
            public List<SimonContestant> Contestants;
        }
        public struct SimonContestant { public string Id; public bool Alive; public string Did, Result; }
    }
}
