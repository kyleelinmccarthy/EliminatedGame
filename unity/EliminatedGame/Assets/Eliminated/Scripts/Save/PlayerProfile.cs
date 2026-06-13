using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
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
        public float sfxVolume = 1f;        // "Game sound": SFX + announcer VO + gameplay music (Mingle/Musical Chairs)
        public float musicVolume = 0.7f;    // "Background music": menu/lobby/round ambiance loops
        public bool musicEnabled = true;    // on/off for BACKGROUND music only; SFX/VO + gameplay music stay on
        public string locale = "en";

        // Authoritative game server for Play Online. Defaults to a localhost dev
        // server (server/Eliminated.Server, `dotnet run -- 8080`); point it at a
        // hosted server or Unity-Relay bridge for real matches.
        public string serverUrl = "ws://localhost:8080/";

        // Remappable keyboard bindings (player 0). Arrow keys are always-on
        // alternates for movement, so a remap can never strand the player.
        public Key keyUp = Key.W;
        public Key keyDown = Key.S;
        public Key keyLeft = Key.A;
        public Key keyRight = Key.D;
        public Key keyAction = Key.Space;
        public Key keyDash = Key.LeftShift;

        public void ResetBindings()
        {
            keyUp = Key.W; keyDown = Key.S; keyLeft = Key.A; keyRight = Key.D;
            keyAction = Key.Space; keyDash = Key.LeftShift;
        }
    }

    /// <summary>
    /// Locally-persisted player profile: identity, currency, unlocks, and
    /// settings. Saved as JSON in <c>Application.persistentDataPath</c> (Phase 6
    /// adds Steam Cloud sync of this same file).
    /// </summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public string name = "Player";
        public string characterId = "avo";
        public int marbles = 0;
        public int seriesWon = 0;
        public int roundsSurvived = 0;
        public List<string> unlocked = new List<string>();
        public List<string> equipped = new List<string>(); // worn accessory ids, ≤1 per slot
        public GameSettings settings = new GameSettings();

        public bool IsUnlocked(string id) => marbles >= 0 && (unlocked == null || unlocked.Contains(id));
    }
}
