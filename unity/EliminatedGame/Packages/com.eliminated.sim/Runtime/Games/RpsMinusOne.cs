using System;
using System.Collections.Generic;
using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// RPS Minus One — 1v1 bracket. Pick TWO throws, drop one, play your kept
    /// throw. Ties replay (nobody dies on a coin flip); losers are out. As the
    /// finale, runs a full single-elim bracket to one survivor. Ported from
    /// lib/server/games/RpsMinusOne.ts.
    /// </summary>
    public sealed class RpsMinusOne : IMinigame
    {
        // Deliberately roomy: players read two throws, decide, then commit. The earlier
        // 4.5/3/2 felt frantic (you barely saw the buttons before the phase flipped).
        private const float PickT = 6.5f;
        private const float DropT = 4.5f;
        private const float ResolveT = 2.6f;
        private static readonly string[] Throws = { "R", "P", "S" };

        private static int Cmp(string a, string b)
        {
            if (a == b) return 0;
            if ((a == "R" && b == "S") || (a == "S" && b == "P") || (a == "P" && b == "R")) return 1;
            return -1;
        }

        private sealed class Duel
        {
            public string A;
            public string B; // null = bye
            public List<string> AThrows = new List<string>();
            public List<string> BThrows = new List<string>();
            public string AKeep, BKeep;
            public string Status = "pick";
            public string Winner;
            public int Ties;
        }

        private readonly GameContext _ctx;
        private readonly Rng _rng;
        private readonly List<Duel> _duels = new List<Duel>();
        private readonly List<string> _byes = new List<string>();
        private readonly List<(string id, string note)> _elim = new List<(string, string)>();
        private readonly HashSet<string> _bots = new HashSet<string>();

        private string _phase = "pick";
        private float _timer = PickT;
        private int _round;
        private bool _done;

        public RpsMinusOne(GameContext ctx) { _ctx = ctx; _rng = ctx.Rng; }

        public GameId Id => GameId.RpsMinusOne;
        public bool IsDone => _done;
        public string CurrentPhase => _phase;

        public void Start()
        {
            foreach (var a in _ctx.Actors) if (a.IsBot) _bots.Add(a.Id);
            var ids = _rng.Shuffle(_ctx.Actors.Select(a => a.Id).ToList());
            for (int i = 0; i + 1 < ids.Count; i += 2)
                _duels.Add(new Duel { A = ids[i], B = ids[i + 1] });
            if (ids.Count % 2 == 1) _byes.Add(ids[ids.Count - 1]);
            _phase = "pick";
            _timer = PickT;
            _round = 1;
        }

        private Duel DuelOf(string id) => _duels.FirstOrDefault(d => d.Status != "done" && (d.A == id || d.B == id));

        public void OnInput(string playerId, GameInput input)
        {
            if (input.Kind != InputKind.Choose) return;
            var d = DuelOf(playerId);
            if (d == null) return;
            bool isA = d.A == playerId;
            string v = input.Value ?? "";

            if (_phase == "pick" && d.Status == "pick")
            {
                var chars = v.Where(c => Throws.Contains(c.ToString())).Select(c => c.ToString()).ToList();
                if (chars.Count >= 2)
                {
                    if (isA) d.AThrows = new List<string> { chars[0], chars[1] };
                    else d.BThrows = new List<string> { chars[0], chars[1] };
                }
                else if (chars.Count == 1)
                {
                    var arr = isA ? d.AThrows : d.BThrows;
                    if (arr.Count < 2) arr.Add(chars[0]);
                }
            }
            else if (_phase == "drop" && d.Status == "drop")
            {
                if (!Throws.Contains(v)) return;
                var owned = isA ? d.AThrows : d.BThrows;
                if (!owned.Contains(v)) return;
                if (isA) d.AKeep = v; else d.BKeep = v;
            }
        }

        public void Tick(float dt)
        {
            if (_done) return;
            _timer -= dt;

            foreach (var d in _duels)
            {
                if (d.Status == "done") continue;
                BotFor(d, d.A);
                if (d.B != null) BotFor(d, d.B);
            }

            if (_phase == "resolve") { if (_timer <= 0f) AdvancePhase(); }
            else if (_timer <= 0f || PhaseComplete()) AdvancePhase();
        }

        private bool PhaseComplete()
        {
            if (_phase == "pick")
                return _duels.All(d => d.Status != "pick" || (d.AThrows.Count >= 2 && (d.B == null || d.BThrows.Count >= 2)));
            if (_phase == "drop")
                return _duels.All(d => d.Status != "drop" || (d.AKeep != null && (d.B == null || d.BKeep != null)));
            return false;
        }

        private void BotFor(Duel d, string id)
        {
            if (!_bots.Contains(id)) return;
            bool isA = d.A == id;
            if (_phase == "pick" && d.Status == "pick")
            {
                var arr = isA ? d.AThrows : d.BThrows;
                if (arr.Count < 2)
                {
                    arr.Clear();
                    arr.Add(Throws[_rng.NextInt(3)]);
                    arr.Add(Throws[_rng.NextInt(3)]);
                }
            }
            else if (_phase == "drop" && d.Status == "drop")
            {
                if ((isA ? d.AKeep : d.BKeep) != null) return;
                var own = (isA ? d.AThrows : d.BThrows);
                var opp = (isA ? d.BThrows : d.AThrows);
                var ownOpts = own.Count > 0 ? own : Throws.ToList();
                var oppOpts = opp.Count > 0 ? opp : Throws.ToList();
                string best = ownOpts[0];
                float bestScore = float.NegativeInfinity;
                foreach (var c in ownOpts)
                {
                    float s = oppOpts.Sum(o => Cmp(c, o)) / (float)oppOpts.Count + (_rng.NextFloat() - 0.5f) * 0.2f;
                    if (s > bestScore) { bestScore = s; best = c; }
                }
                if (isA) d.AKeep = best; else d.BKeep = best;
            }
        }

        private void AdvancePhase()
        {
            if (_phase == "pick")
            {
                foreach (var d in _duels)
                {
                    if (d.Status != "pick") continue;
                    bool aMissed = d.AThrows.Count < 2;
                    bool bMissed = d.B != null && d.BThrows.Count < 2;
                    if (d.B != null && (aMissed || bMissed)) { ResolveForfeit(d, aMissed, bMissed); continue; }
                    d.Status = "drop";
                }
                _phase = "drop";
                _timer = DropT;
            }
            else if (_phase == "drop")
            {
                foreach (var d in _duels)
                {
                    if (d.Status != "drop") continue;
                    bool aMissed = d.AKeep == null;
                    bool bMissed = d.B != null && d.BKeep == null;
                    if (d.B != null && (aMissed || bMissed)) { ResolveForfeit(d, aMissed, bMissed); continue; }
                    d.Status = "resolve";
                }
                _phase = "resolve";
                _timer = ResolveT;
                ResolveAll();
            }
            else
            {
                if (_duels.Any(d => d.Status == "pick")) { _phase = "pick"; _timer = PickT; _round++; }
                else if (_ctx.ForceSingleSurvivor && Standing().Count > 1) NextBracketRound();
                else _done = true;
            }
        }

        private List<string> Standing()
        {
            var ids = _duels.Where(d => d.Winner != null).Select(d => d.Winner).ToList();
            ids.AddRange(_byes);
            return ids;
        }

        private void NextBracketRound()
        {
            var survivors = _rng.Shuffle(Standing());
            _duels.Clear();
            _byes.Clear();
            for (int i = 0; i + 1 < survivors.Count; i += 2)
                _duels.Add(new Duel { A = survivors[i], B = survivors[i + 1] });
            if (survivors.Count % 2 == 1) _byes.Add(survivors[survivors.Count - 1]);
            _phase = "pick";
            _timer = PickT;
            _round++;
        }

        private void ResolveAll()
        {
            foreach (var d in _duels)
            {
                if (d.Status != "resolve" || d.B == null)
                {
                    if (d.B == null) { d.Status = "done"; d.Winner = d.A; }
                    continue;
                }
                int r = Cmp(d.AKeep, d.BKeep);
                if (r == 0)
                {
                    d.Ties++;
                    d.AThrows.Clear(); d.BThrows.Clear();
                    d.AKeep = null; d.BKeep = null;
                    d.Status = "pick";
                }
                else Settle(d, r > 0 ? d.A : d.B);
            }
        }

        private void Settle(Duel d, string winner, string note = "Out-thrown!")
        {
            string loser = winner == d.A ? d.B : d.A;
            d.Winner = winner;
            d.Status = "done";
            _elim.Add((loser, note));
            SyncActor(loser, false);
        }

        private void ResolveForfeit(Duel d, bool aMissed, bool bMissed)
        {
            if (aMissed && bMissed)
            {
                if (_ctx.ForceSingleSurvivor)
                    Settle(d, _rng.NextFloat() < 0.5f ? d.A : d.B, "Forfeit — both froze, coin flip!");
                else
                {
                    d.Status = "done"; d.Winner = null;
                    foreach (var id in new[] { d.A, d.B }) { _elim.Add((id, "Forfeit — froze up!")); SyncActor(id, false); }
                }
                return;
            }
            Settle(d, aMissed ? d.B : d.A, "Forfeit — too slow!");
        }

        public void Forfeit(string playerId)
        {
            var d = DuelOf(playerId);
            if (d != null && d.B != null) Settle(d, d.A == playerId ? d.B : d.A);
            else if (_byes.Contains(playerId)) _byes.Remove(playerId);
            SyncActor(playerId, false);
        }

        private void SyncActor(string id, bool alive)
        {
            var a = _ctx.Actors.FirstOrDefault(x => x.Id == id);
            if (a != null) a.Alive = alive;
        }

        public RoundResult Result()
        {
            var winners = new List<string>();
            foreach (var d in _duels) if (d.Winner != null && !winners.Contains(d.Winner)) winners.Add(d.Winner);
            foreach (var b in _byes) if (!winners.Contains(b)) winners.Add(b);
            foreach (var a in _ctx.Actors) SyncActor(a.Id, winners.Contains(a.Id));
            return RankingUtil.Build(Id, winners, _elim);
        }

        // Stand each duel's two fighters face to face, one duel per row; byes
        // wait along the right wall. Otherwise the bracket would render as a
        // heap of players in the corner.
        private void Layout()
        {
            var pos = new Dictionary<string, Vec2>();
            for (int i = 0; i < _duels.Count; i++)
            {
                var d = _duels[i];
                float y = Stage.Spread(i, _duels.Count, Stage.MinY + 30f, Stage.MaxY - 30f);
                if (d.A != null) pos[d.A] = Stage.Clamp(Stage.CenterX - 150f, y);
                if (d.B != null) pos[d.B] = Stage.Clamp(Stage.CenterX + 150f, y);
            }
            for (int i = 0; i < _byes.Count; i++)
                pos[_byes[i]] = Stage.Clamp(Stage.MaxX, Stage.Spread(i, _byes.Count, Stage.MinY, Stage.MaxY));

            foreach (var a in _ctx.Actors)
                if (pos.TryGetValue(a.Id, out var p))
                {
                    a.Pos = p;
                    a.Facing = p.X < Stage.CenterX ? 0f : (float)Math.PI; // face the opponent
                }
        }

        public Snapshot BuildSnapshot()
        {
            Layout();
            return new Snapshot
            {
                Game = Id,
                Actors = _ctx.Actors,
                Data = new RpsData
                {
                    Phase = _phase,
                    TimeLeft = Math.Max(0f, _timer),
                    Round = _round,
                    Byes = new List<string>(_byes),
                    Duels = _duels.Select(d => new DuelView
                    {
                        A = d.A, B = d.B,
                        AThrows = _phase == "pick" ? new List<string>() : new List<string>(d.AThrows),
                        BThrows = _phase == "pick" ? new List<string>() : new List<string>(d.BThrows),
                        AKeep = _phase == "resolve" ? d.AKeep : null,
                        BKeep = _phase == "resolve" ? d.BKeep : null,
                        Status = d.Status, Winner = d.Winner, Ties = d.Ties
                    }).ToList()
                }
            };
        }

        public sealed class RpsData
        {
            public string Phase;
            public float TimeLeft;
            public int Round;
            public List<string> Byes;
            public List<DuelView> Duels;
        }
        public sealed class DuelView
        {
            public string A, B;
            public List<string> AThrows, BThrows;
            public string AKeep, BKeep, Status, Winner;
            public int Ties;
        }
    }
}
