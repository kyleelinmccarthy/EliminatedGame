using System.Collections.Generic;

namespace Eliminated.Sim.Localization
{
    /// <summary>
    /// Lightweight localization: code-embedded string tables keyed by locale,
    /// with English fallback and {0}-style argument formatting. Pure C# so it is
    /// unit-tested headlessly and shared by the sim and the Unity client. The
    /// Unity Localization package can replace these tables later (Phase 7) without
    /// changing call sites — they all go through <see cref="Get"/>.
    ///
    /// Game names are keyed "game.&lt;GameId&gt;"; UI/Game-Master strings under
    /// "ui."/"gm.". en/es/fr are complete; de/pt/ja localize the most visible
    /// strings and fall back to English elsewhere (starter translations — a native
    /// pass refines them).
    /// </summary>
    public static class Loc
    {
        public const string Default = "en";
        public static string Locale = Default;

        public static readonly string[] Locales = { "en", "es", "fr", "de", "pt-BR", "ja" };

        public static void SetLocale(string locale)
        {
            Locale = _tables.ContainsKey(locale) ? locale : Default;
        }

        public static bool HasLocale(string locale) => _tables.ContainsKey(locale);

        /// <summary>Localized string for the current locale (English fallback),
        /// with optional {0}/{1} argument formatting.</summary>
        public static string Get(string key, params object[] args)
        {
            string s = Lookup(Locale, key) ?? Lookup(Default, key) ?? key;
            return args != null && args.Length > 0 ? string.Format(s, args) : s;
        }

        public static string GetIn(string locale, string key, params object[] args)
        {
            string s = Lookup(locale, key) ?? Lookup(Default, key) ?? key;
            return args != null && args.Length > 0 ? string.Format(s, args) : s;
        }

        private static string Lookup(string locale, string key)
            => _tables.TryGetValue(locale, out var t) && t.TryGetValue(key, out var v) ? v : null;

        private static readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = new Dictionary<string, string>
            {
                ["ui.title"] = "ELIMINATED",
                ["ui.tagline"] = "A wholesome party game where everyone dies.",
                ["ui.play_solo_casual"] = "Solo vs Bots — Casual",
                ["ui.play_solo_hardcore"] = "Solo vs Bots — Hardcore (last blob standing)",
                ["ui.local_coop"] = "Local Co-op",
                ["ui.settings"] = "Settings",
                ["ui.quit"] = "Quit",
                ["ui.back_to_menu"] = "Back to Menu",
                ["ui.save_and_back"] = "Save & Back",
                ["ui.get_ready"] = "Get ready…",
                ["ui.reckoning"] = "The Reckoning",
                ["ui.series_over"] = "Series Over",
                ["ui.marbles"] = "Marbles",
                ["ui.round"] = "Round {0}",
                ["gm.game_intro"] = "Game Master: Game {0} — {1}.",
                ["gm.champion"] = "Game Master: {0} is the last blob standing!",
                ["game.RedLight"] = "Red Light, Green Light",
                ["game.Tag"] = "Freeze Tag",
                ["game.Mingle"] = "Mingle",
                ["game.GlassBridge"] = "Glass Stepping Stones",
                ["game.TugOfWar"] = "Tug of War",
                ["game.RpsMinusOne"] = "RPS Minus One",
                ["game.JumpRope"] = "Killer Jump Rope",
                ["game.Boomerang"] = "Boomerang Brawl",
                ["game.Dodgeball"] = "Dodgeball",
                ["game.MusicalChairs"] = "Musical Chairs",
                ["game.PresentSwap"] = "Secret Santa Sabotage",
                ["game.PropHunt"] = "Prop Hunt",
                ["game.ChutesAndLadders"] = "Chutes & Ladders",
                ["game.SimonSays"] = "Simon Says",
                ["game.KeepyUppy"] = "Keepy Uppy",
                ["game.KingOfTheHill"] = "King of the Lava Islands",
            },
            ["es"] = new Dictionary<string, string>
            {
                ["ui.title"] = "ELIMINADO",
                ["ui.tagline"] = "Un alegre juego de fiesta donde todos mueren.",
                ["ui.play_solo_casual"] = "En solitario vs Bots — Casual",
                ["ui.play_solo_hardcore"] = "En solitario vs Bots — Difícil (el último blob en pie)",
                ["ui.local_coop"] = "Cooperativo local",
                ["ui.settings"] = "Ajustes",
                ["ui.quit"] = "Salir",
                ["ui.back_to_menu"] = "Volver al menú",
                ["ui.save_and_back"] = "Guardar y volver",
                ["ui.get_ready"] = "Prepárate…",
                ["ui.reckoning"] = "El Ajuste de Cuentas",
                ["ui.series_over"] = "Serie terminada",
                ["ui.marbles"] = "Canicas",
                ["ui.round"] = "Ronda {0}",
                ["gm.game_intro"] = "Maestro del Juego: Juego {0} — {1}.",
                ["gm.champion"] = "Maestro del Juego: ¡{0} es el último blob en pie!",
                ["game.RedLight"] = "Luz roja, luz verde",
                ["game.Tag"] = "Pilla-pilla congelado",
                ["game.Mingle"] = "Mézclate",
                ["game.GlassBridge"] = "Puente de cristal",
                ["game.TugOfWar"] = "Tira y afloja",
                ["game.RpsMinusOne"] = "Piedra, papel o tijera menos uno",
                ["game.JumpRope"] = "Comba asesina",
                ["game.Boomerang"] = "Pelea de bumeranes",
                ["game.Dodgeball"] = "Balón prisionero",
                ["game.MusicalChairs"] = "Sillas musicales",
                ["game.PresentSwap"] = "Sabotaje de Amigo Invisible",
                ["game.PropHunt"] = "Caza de objetos",
                ["game.ChutesAndLadders"] = "Serpientes y escaleras",
                ["game.SimonSays"] = "Simón dice",
                ["game.KeepyUppy"] = "Mantén el globo",
                ["game.KingOfTheHill"] = "Rey de las islas de lava",
            },
            ["fr"] = new Dictionary<string, string>
            {
                ["ui.title"] = "ÉLIMINÉ",
                ["ui.tagline"] = "Un gentil jeu de fête où tout le monde meurt.",
                ["ui.play_solo_casual"] = "Solo contre des bots — Décontracté",
                ["ui.play_solo_hardcore"] = "Solo contre des bots — Difficile (dernier blob debout)",
                ["ui.local_coop"] = "Coop local",
                ["ui.settings"] = "Paramètres",
                ["ui.quit"] = "Quitter",
                ["ui.back_to_menu"] = "Retour au menu",
                ["ui.save_and_back"] = "Enregistrer et revenir",
                ["ui.get_ready"] = "Préparez-vous…",
                ["ui.reckoning"] = "Le Verdict",
                ["ui.series_over"] = "Série terminée",
                ["ui.marbles"] = "Billes",
                ["ui.round"] = "Manche {0}",
                ["gm.game_intro"] = "Maître du Jeu : Jeu {0} — {1}.",
                ["gm.champion"] = "Maître du Jeu : {0} est le dernier blob debout !",
                ["game.RedLight"] = "Un, deux, trois, soleil",
                ["game.Tag"] = "Jeu du gel",
                ["game.Mingle"] = "Mêlez-vous",
                ["game.GlassBridge"] = "Pont de verre",
                ["game.TugOfWar"] = "Tir à la corde",
                ["game.RpsMinusOne"] = "Pierre-papier-ciseaux moins un",
                ["game.JumpRope"] = "Corde à sauter mortelle",
                ["game.Boomerang"] = "Bagarre de boomerangs",
                ["game.Dodgeball"] = "Ballon prisonnier",
                ["game.MusicalChairs"] = "Chaises musicales",
                ["game.PresentSwap"] = "Sabotage du Père Noël secret",
                ["game.PropHunt"] = "Chasse aux objets",
                ["game.ChutesAndLadders"] = "Serpents et échelles",
                ["game.SimonSays"] = "Jacques a dit",
                ["game.KeepyUppy"] = "Garde le ballon",
                ["game.KingOfTheHill"] = "Roi des îles de lave",
            },
            // de / pt-BR / ja: localize the most visible strings; the rest falls back to English.
            ["de"] = new Dictionary<string, string>
            {
                ["ui.title"] = "ELIMINIERT",
                ["ui.settings"] = "Einstellungen",
                ["ui.quit"] = "Beenden",
                ["ui.back_to_menu"] = "Zurück zum Menü",
                ["ui.local_coop"] = "Lokaler Koop",
                ["ui.series_over"] = "Serie vorbei",
                ["game.RedLight"] = "Rotlicht, Grünlicht",
                ["game.TugOfWar"] = "Tauziehen",
                ["game.Dodgeball"] = "Völkerball",
                ["game.MusicalChairs"] = "Reise nach Jerusalem",
                ["game.KingOfTheHill"] = "König der Lavainseln",
            },
            ["pt-BR"] = new Dictionary<string, string>
            {
                ["ui.title"] = "ELIMINADO",
                ["ui.settings"] = "Configurações",
                ["ui.quit"] = "Sair",
                ["ui.back_to_menu"] = "Voltar ao menu",
                ["ui.local_coop"] = "Coop local",
                ["ui.series_over"] = "Série encerrada",
                ["game.RedLight"] = "Batatinha frita 1, 2, 3",
                ["game.TugOfWar"] = "Cabo de guerra",
                ["game.Dodgeball"] = "Queimada",
                ["game.MusicalChairs"] = "Dança das cadeiras",
                ["game.KingOfTheHill"] = "Rei das ilhas de lava",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["ui.title"] = "イリミネイテッド",
                ["ui.settings"] = "設定",
                ["ui.quit"] = "やめる",
                ["ui.back_to_menu"] = "メニューに戻る",
                ["ui.local_coop"] = "ローカル協力プレイ",
                ["ui.series_over"] = "シリーズ終了",
                ["game.RedLight"] = "だるまさんがころんだ",
                ["game.TugOfWar"] = "綱引き",
                ["game.Dodgeball"] = "ドッジボール",
                ["game.MusicalChairs"] = "イス取りゲーム",
                ["game.KingOfTheHill"] = "溶岩島の王",
            },
        };
    }
}
