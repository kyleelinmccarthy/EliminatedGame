using System.Collections.Generic;
using System.Linq;

namespace Eliminated.Sim.Economy
{
    public struct CharacterDef
    {
        public string Id;
        public string Name;
        public int UnlockCost; // 0 = free
        public CharacterDef(string id, string name, int cost) { Id = id; Name = name; UnlockCost = cost; }
    }

    public struct AccessoryDef
    {
        public string Id;
        public string Slot; // head / eyes / neck / ear
        public int Price;
        public AccessoryDef(string id, string slot, int price) { Id = id; Slot = slot; Price = price; }
    }

    /// <summary>
    /// The cosmetic catalog: 38 blob characters (28 free + 10 unlockable) and the
    /// accessory wardrobe (head/eyes/neck/ear). Costs ported verbatim from the
    /// reference characters.ts / accessories.ts. Pure data + lookups so the client
    /// shop and the unlock economy share one source of truth.
    /// </summary>
    public static class Cosmetics
    {
        public static readonly CharacterDef[] Characters =
        {
            // ── Free roster (28) ──
            new CharacterDef("koala", "Koalamity", 0),
            new CharacterDef("aardvark", "Aard to Kill", 0),
            new CharacterDef("panther", "Purrgatory", 0),
            new CharacterDef("fox", "Foxic", 0),
            new CharacterDef("capybara", "Capybarely", 0),
            new CharacterDef("wizard", "Hocus Croakus", 0),
            new CharacterDef("rogue", "Hood Riddance", 0),
            new CharacterDef("bunny", "Hare Trigger", 0),
            new CharacterDef("pig", "Boar-ed to Death", 0),
            new CharacterDef("cat", "Meowderer", 0),
            new CharacterDef("mouse", "Plague Rat", 0),
            new CharacterDef("hamster", "Hamstrung", 0),
            new CharacterDef("ghost", "Ghosted", 0),
            new CharacterDef("slime", "Slime Crime", 0),
            new CharacterDef("avo", "Avocadon't", 0),
            new CharacterDef("egg", "Sir Eggbert", 0),
            new CharacterDef("berry", "Strawbarbara", 0),
            new CharacterDef("egg2", "Sir Nightshade", 0),
            new CharacterDef("brocc", "Grim Floret", 0),
            new CharacterDef("donut", "Detective Donut", 0),
            new CharacterDef("pickle", "Dill With It", 0),
            new CharacterDef("tomato", "Tomato Tony", 0),
            new CharacterDef("pine", "Princess Pineapple", 0),
            new CharacterDef("shroom", "Deathcap Dan", 0),
            new CharacterDef("sushi", "Raw Deal", 0),
            new CharacterDef("nana", "Banana Joe", 0),
            new CharacterDef("plum", "Sour Grapes", 0),
            new CharacterDef("orange", "Pulp Friction", 0),
            // ── Unlockable (10) ──
            new CharacterDef("blueberry", "Bruiseberry", 200),
            new CharacterDef("carrot", "Root of Evil", 250),
            new CharacterDef("dragonfruit", "Dragon Fruit Punch", 450),
            new CharacterDef("melon", "Watermelon Wanda", 500),
            new CharacterDef("goldegg", "Yolk's On You", 800),
            new CharacterDef("onigiri", "Rigor Rice", 1000),
            new CharacterDef("ninja", "Backstabber", 1200),
            new CharacterDef("sorcerer", "Hexecutioner", 1600),
            new CharacterDef("ghostpepper", "Reaper Pepper", 2000),
            new CharacterDef("cosmic", "Black Hole", 3000),
        };

        public static readonly AccessoryDef[] Accessories =
        {
            new AccessoryDef("beanie", "head", 120), new AccessoryDef("cap", "head", 150),
            new AccessoryDef("partyhat", "head", 180), new AccessoryDef("cowboy", "head", 280),
            new AccessoryDef("tophat", "head", 360), new AccessoryDef("crown", "head", 420),
            new AccessoryDef("glasses", "eyes", 140), new AccessoryDef("specs", "eyes", 160),
            new AccessoryDef("cateye", "eyes", 190), new AccessoryDef("rounds", "eyes", 240),
            new AccessoryDef("shades", "eyes", 260), new AccessoryDef("aviators", "eyes", 300),
            new AccessoryDef("bandana", "neck", 110), new AccessoryDef("bowtie", "neck", 220),
            new AccessoryDef("banana", "ear", 80), new AccessoryDef("flower", "ear", 90),
            new AccessoryDef("greenana", "ear", 100), new AccessoryDef("rose", "ear", 130),
            new AccessoryDef("bluebell", "ear", 150), new AccessoryDef("sunflower", "ear", 170),
            new AccessoryDef("spotnana", "ear", 190), new AccessoryDef("feather", "ear", 200),
        };

        public static readonly string[] Slots = { "head", "eyes", "neck", "ear" };

        public static IEnumerable<string> FreeCharacterIds =>
            Characters.Where(c => c.UnlockCost == 0).Select(c => c.Id);

        public static bool IsCharacter(string id) => Characters.Any(c => c.Id == id);
        public static bool IsAccessory(string id) => Accessories.Any(a => a.Id == id);

        /// <summary>Unified cost lookup across both catalogs; -1 if unknown.</summary>
        public static int Cost(string id)
        {
            foreach (var c in Characters) if (c.Id == id) return c.UnlockCost;
            foreach (var a in Accessories) if (a.Id == id) return a.Price;
            return -1;
        }

        /// <summary>A character is always owned if it's free; otherwise it (and any
        /// accessory) must be in the player's unlocked set.</summary>
        public static bool IsOwned(string id, ICollection<string> unlocked)
        {
            if (IsCharacter(id) && Cost(id) == 0) return true;
            return unlocked != null && unlocked.Contains(id);
        }

        public static string SlotOf(string accessoryId)
        {
            foreach (var a in Accessories) if (a.Id == accessoryId) return a.Slot;
            return null;
        }
    }
}
