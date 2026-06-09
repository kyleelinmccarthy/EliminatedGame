using System;
using System.Collections.Generic;
using Eliminated.Game.Accessibility;

namespace Eliminated.Game.Save
{
    /// <summary>Player-facing options, including accessibility settings.</summary>
    [Serializable]
    public sealed class GameSettings
    {
        public ColorblindMode colorblind = ColorblindMode.Normal;
        public bool subtitles = true;       // captions for Game Master VO / cues
        public bool reduceFlashAndShake = false;
        public float masterVolume = 1f;
        public float sfxVolume = 1f;
        public float musicVolume = 0.7f;
        public string locale = "en";
    }

    /// <summary>
    /// Locally-persisted player profile: identity, currency, unlocks, and
    /// settings. Saved as JSON in <c>Application.persistentDataPath</c> (Phase 6
    /// adds Steam Cloud sync of this same file).
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public string name = "Blob";
        public string characterId = "avocado";
        public int marbles = 0;
        public int seriesWon = 0;
        public int roundsSurvived = 0;
        public List<string> unlocked = new List<string>();
        public GameSettings settings = new GameSettings();

        public bool IsUnlocked(string id) => marbles >= 0 && (unlocked == null || unlocked.Contains(id));
    }
}
