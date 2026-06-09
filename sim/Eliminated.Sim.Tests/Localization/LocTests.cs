using Eliminated.Sim.Localization;
using Eliminated.Sim.Model;
using Eliminated.Sim.Room;
using Xunit;

namespace Eliminated.Sim.Tests.Localization
{
    public class LocTests
    {
        [Fact]
        public void English_is_the_default_and_resolves_keys()
        {
            Loc.SetLocale("en");
            Assert.Equal("Settings", Loc.Get("ui.settings"));
            Assert.Equal("Boomerang Brawl", Loc.Get("game.Boomerang"));
        }

        [Fact]
        public void Spanish_and_french_localize_core_strings()
        {
            Loc.SetLocale("es");
            Assert.Equal("Ajustes", Loc.Get("ui.settings"));
            Assert.Equal("Luz roja, luz verde", Loc.Get("game.RedLight"));

            Loc.SetLocale("fr");
            Assert.Equal("Paramètres", Loc.Get("ui.settings"));
            Assert.Equal("Tir à la corde", Loc.Get("game.TugOfWar"));
            Loc.SetLocale("en");
        }

        [Fact]
        public void Missing_translations_fall_back_to_english()
        {
            Loc.SetLocale("ja"); // only a subset is translated
            Assert.Equal("設定", Loc.Get("ui.settings"));      // localized
            Assert.Equal("Mingle", Loc.Get("game.Mingle"));    // falls back to English
            Loc.SetLocale("en");
        }

        [Fact]
        public void Unknown_locale_falls_back_to_default()
        {
            Loc.SetLocale("xx");
            Assert.Equal("en", Loc.Locale);
        }

        [Fact]
        public void Arguments_are_formatted()
        {
            Loc.SetLocale("en");
            Assert.Equal("Round 3", Loc.Get("ui.round", 3));
            Assert.Equal("Game Master: Game 2 — Dodgeball.", Loc.Get("gm.game_intro", 2, "Dodgeball"));
        }

        [Fact]
        public void Every_game_in_the_catalog_has_an_english_name()
        {
            foreach (GameId id in System.Enum.GetValues(typeof(GameId)))
            {
                string name = Loc.GetIn("en", "game." + id);
                Assert.NotEqual("game." + id, name); // i.e. a real translation exists, not the raw key
            }
        }

        [Fact]
        public void Unknown_key_returns_the_key()
        {
            Assert.Equal("nope.nope", Loc.GetIn("en", "nope.nope"));
        }
    }
}
