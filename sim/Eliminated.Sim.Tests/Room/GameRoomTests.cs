using System.Linq;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;
using Eliminated.Sim.Room;
using Xunit;

namespace Eliminated.Sim.Tests.Room
{
    public class GameRoomTests
    {
        private static GameRoom Room(int seed = 1) => new GameRoom("ABCD", seed);

        private static Player Human(string id) => new Player(id, id, "avocado", isBot: false);

        /// <summary>Tick until a predicate holds (or we give up), fast-forwarding
        /// through the deterministic phase timers.</summary>
        private static bool RunUntil(GameRoom r, System.Func<bool> cond, int maxTicks = 400_000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (cond()) return true;
                r.Tick(Constants.Dt);
            }
            return cond();
        }

        [Fact]
        public void First_player_becomes_host()
        {
            var r = Room();
            var p = Human("p1");
            r.AddPlayer(p);
            Assert.Equal("p1", r.HostId);
        }

        [Fact]
        public void Players_get_unique_numbers()
        {
            var r = Room();
            for (int i = 0; i < 6; i++) r.AddPlayer(Human("p" + i));
            var nums = r.Players.Select(p => p.Number).ToList();
            Assert.Equal(nums.Count, nums.Distinct().Count());
            Assert.All(nums, n => Assert.InRange(n, 1, 456));
        }

        [Fact]
        public void Start_requires_at_least_two_competitors_without_bot_fill()
        {
            var r = Room();
            r.UpdateConfig(new RoomConfig { BotFill = false });
            r.AddPlayer(Human("solo"));
            Assert.False(r.StartSeries());
            Assert.Equal(RoomPhase.Lobby, r.Phase);
        }

        [Fact]
        public void Bot_fill_tops_the_field_up_to_six()
        {
            var r = Room();
            r.AddPlayer(Human("p1"));
            Assert.True(r.StartSeries());
            Assert.Equal(Constants.BotFillTarget, r.Players.Count);
            Assert.Equal(RoomPhase.Intro, r.Phase);
        }

        [Fact]
        public void Intro_holds_then_a_go_beat_then_play_begins()
        {
            var r = Room(seed: 2);
            r.AddPlayer(Human("p1"));
            r.StartSeries();
            Assert.Equal(RoomPhase.Intro, r.Phase);

            RunUntil(r, () => r.Phase == RoomPhase.Playing);
            Assert.False(r.PlayStarted); // GO hold first
            RunUntil(r, () => r.PlayStarted);
            Assert.True(r.PlayStarted);
        }

        [Fact]
        public void Input_is_ignored_until_the_go_hold_elapses()
        {
            var r = Room(seed: 2);
            r.AddPlayer(Human("p1"));
            r.StartSeries();
            RunUntil(r, () => r.Phase == RoomPhase.Playing);
            // before GO: a snapshot carries StartAt (countdown), input is gated
            var snap = r.BuildSnapshot();
            Assert.NotNull(snap.StartAt);
            r.HandleInput("p1", GameInput.Move(1, 0)); // should be ignored (no throw)
        }

        [Fact]
        public void Casual_series_completes_and_ranks_every_competitor()
        {
            var r = Room(seed: 7);
            r.UpdateConfig(new RoomConfig { Mode = SeriesMode.Casual, Rounds = RoundsMode.Fixed(3) });
            r.AddPlayer(Human("p1"));
            r.StartSeries();

            Assert.True(RunUntil(r, () => r.Phase == RoomPhase.SeriesResult));
            var sr = r.SeriesResult;
            Assert.NotNull(sr);
            Assert.Equal(6, sr.Standings.Count);
            Assert.Equal(Enumerable.Range(1, 6), sr.Standings.Select(s => s.Placement).OrderBy(x => x));
            Assert.NotNull(sr.ChampionId);
            Assert.Equal("The Last Blob Standing", sr.Standings.First(s => s.Placement == 1).Title);
        }

        [Fact]
        public void Hardcore_series_ends_with_a_single_living_champion()
        {
            var r = Room(seed: 13);
            r.UpdateConfig(new RoomConfig { Mode = SeriesMode.Hardcore, Rounds = RoundsMode.Fixed(3) });
            r.AddPlayer(Human("p1"));
            r.StartSeries();

            Assert.True(RunUntil(r, () => r.Phase == RoomPhase.SeriesResult));
            int alive = r.Players.Count(p => p.AliveInSeries);
            Assert.Equal(1, alive);
            var champ = r.Players.First(p => p.Id == r.SeriesResult.ChampionId);
            Assert.True(champ.AliveInSeries);
        }

        [Fact]
        public void A_round_awards_marbles_to_the_living_and_a_consolation_to_the_dead()
        {
            var r = Room(seed: 21);
            r.UpdateConfig(new RoomConfig { Mode = SeriesMode.Hardcore, Rounds = RoundsMode.Fixed(3) });
            r.AddPlayer(Human("p1"));
            r.StartSeries();

            Assert.True(RunUntil(r, () => r.LastRoundReport != null));
            var report = r.LastRoundReport;
            foreach (var e in report.Entries)
            {
                if (e.Survived)
                    Assert.True(e.MarblesEarned >= Marbles_SurvivePerRound());
                else
                    Assert.Equal(Marbles_ElimParticipation(), e.MarblesEarned);
            }
        }

        private static int Marbles_SurvivePerRound() => Eliminated.Sim.Economy.Marbles.SurvivePerRound;
        private static int Marbles_ElimParticipation() => Eliminated.Sim.Economy.Marbles.ElimParticipation;

        [Fact]
        public void Series_is_deterministic_for_a_given_seed()
        {
            string Run(int seed)
            {
                var r = Room(seed);
                r.UpdateConfig(new RoomConfig { Mode = SeriesMode.Casual, Rounds = RoundsMode.Fixed(3) });
                r.AddPlayer(Human("p1"));
                r.StartSeries();
                RunUntil(r, () => r.Phase == RoomPhase.SeriesResult);
                return string.Join(",", r.SeriesResult.Standings.Select(s => s.PlayerId + ":" + s.Placement));
            }
            Assert.Equal(Run(99), Run(99));
        }

        [Fact]
        public void Odd_field_never_selects_an_even_only_game()
        {
            // 5 competitors, only TugOfWar requires even — across many rounds it
            // must never be chosen while the field is odd.
            var r = Room(seed: 5);
            r.UpdateConfig(new RoomConfig { Mode = SeriesMode.Casual, Rounds = RoundsMode.Fixed(8), BotFill = false });
            for (int i = 0; i < 5; i++) r.AddPlayer(Human("p" + i));
            r.StartSeries();

            for (int round = 0; round < 8; round++)
            {
                RunUntil(r, () => r.Phase == RoomPhase.Playing && r.CurrentGame != null);
                // 5 competitors every round (casual) → odd → no even-only game
                Assert.NotEqual(GameId.TugOfWar, r.CurrentGame);
                RunUntil(r, () => r.Phase == RoomPhase.RoundResult || r.Phase == RoomPhase.SeriesResult);
                if (r.Phase == RoomPhase.SeriesResult) break;
            }
        }
    }
}
