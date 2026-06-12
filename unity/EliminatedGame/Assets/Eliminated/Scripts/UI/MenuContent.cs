using System.Collections.Generic;
using Eliminated.Sim.Model;
using Eliminated.Sim.Localization;

namespace Eliminated.Game.UI
{
    /// <summary>
    /// Front-of-house copy ported verbatim from the web build (eliminated/lib/shared
    /// and the how-to-play / patch-notes pages). The voice is the product: wholesome
    /// childhood games reframed as lethal competition — mordant, self-aware, and
    /// cheerfully cruel. Game names + icons still come from <see cref="Eliminated.Sim.Room.GameCatalog"/>;
    /// this file adds the tagline / rules / controls / flavor that the catalog doesn't carry.
    /// </summary>
    public static class MenuContent
    {
        // ---- top-level voice strings (Landing / headers / empty states) ----
        // Loc-backed (English source lives in Loc.cs under "menu.*"); properties so a
        // language switch takes effect live. es/fr are translated; de/pt-BR/ja fall
        // back to English for this long-form flavor copy.
        public static string Tagline => Loc.Get("menu.tagline");
        public static string HeroBlurb => Loc.Get("menu.hero_blurb");
        public static string FooterLine => Loc.Get("menu.footer");

        public static string LeaderboardTitle => Loc.Get("menu.lb_title");
        public static string LeaderboardSubtitle => Loc.Get("menu.lb_subtitle");
        public static string LeaderboardEmpty => Loc.Get("menu.lb_empty");

        public static string HowToPlayTitle => Loc.Get("menu.htp_title");
        public static string HowToPlaySubtitle => Loc.Get("menu.htp_subtitle");
        public static string HowToPlayIntro => Loc.Get("menu.htp_intro");

        public static string HardcoreRule => Loc.Get("menu.hardcore_rule");
        public static string CasualRule => Loc.Get("menu.casual_rule");

        public static string PatchNotesTitle => Loc.Get("menu.patch_title");
        public static string PatchNotesSubtitle => Loc.Get("menu.patch_subtitle");

        public static string AccountTitle => Loc.Get("menu.account_title");
        public static string AccountGuestBlurb => Loc.Get("menu.account_guest");
        public static string AccountSteamBlurb => Loc.Get("menu.account_steam");

        public static string ControlsTitle => Loc.Get("menu.controls_title");
        public static string ControlsSubtitle => Loc.Get("menu.controls_subtitle");

        public static string CreditsTitle => Loc.Get("menu.credits_title");
        public static string CreditsSubtitle => Loc.Get("menu.credits_subtitle");

        // back-button subtitles, per the web pages
        public static string BackHowToPlay => Loc.Get("menu.back_htp");
        public static string BackPatchNotes => Loc.Get("menu.back_patch");

        // marbles-tier "titles" to lord over the deceased
        public static string TitleFor(int marbles, int crowns)
        {
            if (crowns >= 5 || marbles >= 1500) return Loc.Get("menu.tier_last");
            if (marbles >= 600) return Loc.Get("menu.tier_seasoned");
            if (marbles >= 250) return Loc.Get("menu.tier_mid");
            if (marbles >= 80) return Loc.Get("menu.tier_fodder");
            if (marbles > 0) return Loc.Get("menu.tier_compost");
            return Loc.Get("menu.tier_fresh");
        }

        // ---- per-game guide (tagline / rules / controls / one flavor line) ----
        public struct Guide
        {
            public string Tagline, Rules, Controls, Flavor;
            public Guide(string tagline, string rules, string controls, string flavor)
            { Tagline = tagline; Rules = rules; Controls = controls; Flavor = flavor; }
        }

        // Loc-backed per-game guide. English source lives in Loc.cs under
        // "guide.<GameId>.*"; es/fr are translated, de/pt-BR/ja fall back to English.
        public static Guide GuideFor(GameId id) => new Guide(
            Loc.Get($"guide.{id}.tagline"),
            Loc.Get($"guide.{id}.rules"),
            Loc.Get($"guide.{id}.controls"),
            Loc.Get($"guide.{id}.flavor"));

        // ---- changelog (newest first), ported from lib/shared/legal.ts ----
        public struct Patch
        {
            public string Version, Title, Date, Tag;
            public string[] Notes;
            public Patch(string version, string title, string date, string tag, string[] notes)
            { Version = version; Title = title; Date = date; Tag = tag; Notes = notes; }
        }

        public static readonly Patch[] Changelog =
        {
            new Patch("v1.3.0", "Bigger Body Count", "June 11, 2026", "Feature", new[]
            {
                "👥 TWELVE-PLAYER LOBBIES: online rooms now seat up to 12. More friends, more bots, more little corpses per round — the carnage scales.",
                "🤖 FULLER FIELDS: solo runs and short-handed casual rooms now bot-fill all the way to twelve, so you always get a proper crowd to outlive.",
                "Lobby maxed out at twelve? The \"Add Bot\" button steps aside so a host can't stuff in a thirteenth.",
            }),
            new Patch("v1.2.0", "Pull Up a Chair (The Gallery)", "June 8, 2026", "Feature", new[]
            {
                "👁 SPECTATE: don't feel like dying today? In the lobby, hit \"Spectate & bet instead\" to sit the whole run out. You never take the field, you never get culled — you just watch the little players suffer in peace.",
                "🎰 THE GALLERY: spectating isn't free entertainment, it's a casino. Bet your real saved Marbles on who wins it all — odds scale with the field (call it from a crowd of five and it pays 5×), settled the instant a champion is crowned.",
                "Open in BOTH Casual and Hardcore — and you're wagering your actual bank, not house chips. Your pick gets boxed up mid-run? You're warned to re-bet before the finale.",
                "Spectators don't count toward starting a match. A whole room of vultures and six bots? Now legal.",
            }),
            new Patch("v1.1.0", "Drip & The Dead Pool", "June 7, 2026", "Feature", new[]
            {
                "👒 ACCESSORIES: spend your Marbles dressing your player to die in style. Hats, eyewear, neckwear, and a little something behind the ear. Buy them in the lobby under \"Dress your player.\"",
                "Mix and match one item per slot — hat + shades + bandana + ear-flower all at once. Looking incredible offers exactly zero protection. As intended.",
                "☠️ THE DEAD POOL (Hardcore only): being eliminated is no longer just spectating. Bet the Marbles you earned this run on the last player standing and keep cashing in from the afterlife.",
                "Odds scale with the field; a five-player field pays 5×, the 1v1 final is even money. Your pick gets boxed up? You're warned to re-bet — or kiss the wager goodbye.",
                "Bots now show up with a little random drip of their own, because of course they do.",
            }),
            new Patch("v1.0.0", "We're Live (You're In Danger)", "June 5, 2026", "Launch", new[]
            {
                "Opened the doors to the public. Statistically, most of you will not leave through them.",
                "8-player real-time lobbies, a mystery gauntlet of childhood games, and a Hall of Players that remembers everything you've done.",
                "Added Patch Notes, a Privacy Policy, and Terms of Service — because going public means lawyers exist now. We're as surprised as you.",
            }),
            new Patch("v0.9.0", "Night Mode & The Finale", "May 28, 2026", "Feature", new[]
            {
                "Introduced Night Mode: random Hardcore rounds now happen in total darkness, so you can fail to see what kills you. You're welcome.",
                "Every gauntlet now ends on a proper finale — a last decisive round, and in Hardcore it doesn't stop until exactly one player is left. Dignity, in any case, is not provided.",
                "Lantern 🔦 powerup added to extend your field of view, and therefore your suffering.",
            }),
            new Patch("v0.8.2", "Powerups Are Now Slightly Less of a Trap", "May 19, 2026", "Balance", new[]
            {
                "Rebalanced the powerup spawn table. Roughly half of them still want you dead — that's a feature, not a bug.",
                "Red-glow powerups now glow a more honest shade of regret.",
                "Bots will now occasionally walk into the bad powerups too, in the interest of fairness and comedy.",
            }),
            new Patch("v0.8.0", "Fixed the Thing You Were Abusing", "May 9, 2026", "Bugfix", new[]
            {
                "Patched a desync that let a small number of players survive Red Light, Green Light by simply… not following the rules. You know who you are.",
                "Fixed boxes occasionally clapping for the wrong corpse.",
                "Marbles now persist correctly across a series. Your hoard is safe. Your friendships are not.",
            }),
        };

        // ---- Credits (the in-game attribution ledger) ----
        // One source of truth, kept in English on purpose: these are facts (author
        // names, license IDs, sources), not flavor copy — translating them would
        // misstate the license terms (and is exactly how the old es/fr credit player
        // drifted into claiming the music was procedurally generated). Only the page
        // chrome (title / subtitle / group headings) is localized, via the loc keys
        // on each group. Mirrors docs/ASSET_SOURCES.md — keep the two in sync. CC0 /
        // Pixabay assets don't require attribution; we credit them anyway. The CC-BY
        // entries (the caller doll, Kevin MacLeod) are the ones we are legally bound
        // to display, which is why this screen has to be legible.
        public struct Credit
        {
            public string Work, Author, License, Source;
            public Credit(string work, string author, string license, string source)
            { Work = work; Author = author; License = license; Source = source; }
        }

        public struct CreditGroup
        {
            public string HeadingKey;   // resolved through Loc at draw time so a language switch takes effect live
            public Credit[] Entries;
            public CreditGroup(string headingKey, Credit[] entries)
            { HeadingKey = headingKey; Entries = entries; }
        }

        public static readonly CreditGroup[] Credits =
        {
            new CreditGroup("menu.credits_audio", new[]
            {
                new Credit("Sound effects (100 CC0 SFX)", "rubberduck", "CC0 1.0", "OpenGameArt"),
            }),
            new CreditGroup("menu.credits_art", new[]
            {
                new Credit("Blocky character models", "Kenney (kenney.nl)", "CC0 1.0", "OpenGameArt"),
                new Credit("Caller doll — \"Squid Game Doll\"", "ihechiokoro123", "CC-BY 4.0", "Sketchfab"),
                new Credit("Casual Monsters (slime)", "LAYERLAB", "Asset Store EULA", "Unity Asset Store"),
                new Credit("Cute Characters pack", "Jovial Games", "Asset Store EULA", "Unity Asset Store"),
                new Credit("2D Animal Character Pack", "MiMU STUDIO", "Asset Store EULA", "Unity Asset Store"),
            }),
            new CreditGroup("menu.credits_music", new[]
            {
                new Credit("\"Sinister Music Box\" — menus, lobby & rounds", "Universfield", "Pixabay Content License", "Pixabay"),
                new Credit("Creepy suspense — the finale", "Cyberwave Orchestra", "Pixabay Content License", "Pixabay"),
                new Credit("Results & champion theme", "Kevin MacLeod / incompetech.com", "CC-BY 4.0", "incompetech"),
                new Credit("\"Blue Danube\" remix — Musical Chairs", "Trygve Larsen", "Pixabay Content License", "Pixabay"),
            }),
        };
    }
}
