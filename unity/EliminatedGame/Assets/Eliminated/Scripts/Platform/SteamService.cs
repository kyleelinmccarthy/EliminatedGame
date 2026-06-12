using UnityEngine;
#if ELIMINATED_STEAM
using System;
using Steamworks;       // Facepunch.Steamworks (MIT) — imported under Assets/Plugins/
#endif

namespace Eliminated.Game.Platform
{
    /// <summary>
    /// Thin Steam wrapper (Facepunch.Steamworks). All Steam calls are behind the
    /// <c>ELIMINATED_STEAM</c> scripting-define symbol so non-Steam builds (and
    /// this repo before the plugin is imported) compile to a safe no-op. Enable by:
    /// (1) importing Facepunch.Steamworks under <c>Assets/Plugins/</c>,
    /// (2) adding a git-ignored <c>steam_appid.txt</c> for testing,
    /// (3) adding <c>ELIMINATED_STEAM</c> to Player → Scripting Define Symbols.
    /// Then features (achievements, cloud, rich presence, leaderboards) light up.
    /// See docs/IMPLEMENTATION_GUIDE.md Phase 6.
    /// </summary>
    public sealed class SteamService : MonoBehaviour
    {
        public static SteamService Instance { get; private set; }
        public static bool Available { get; private set; }

        /// <summary>The signed-in Steam persona name, or null when Steam isn't running.</summary>
        public string PlayerName =>
#if ELIMINATED_STEAM
            Available ? SteamClient.Name :
#endif
            null;

        /// <summary>The signed-in Steam id as a string, or null when Steam isn't running.</summary>
        public string SteamIdString =>
#if ELIMINATED_STEAM
            Available ? SteamClient.SteamId.ToString() :
#endif
            null;

        public void Init(uint appId)
        {
            Instance = this;
            Available = false;
#if ELIMINATED_STEAM
            try
            {
                SteamClient.Init(appId, asyncCallbacks: true);
                Available = true;
                Debug.Log($"[Steam] ready: {SteamClient.Name} ({SteamClient.SteamId})");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Steam] init failed (running without Steam): {e.Message}");
            }
#endif
        }

        /// <summary>Unlock an achievement by its API name.</summary>
        public void Unlock(string apiName)
        {
#if ELIMINATED_STEAM
            if (!Available) return;
            try { new Achievement(apiName).Trigger(); }
            catch (Exception e) { Debug.LogWarning($"[Steam] achievement '{apiName}': {e.Message}"); }
#endif
        }

        /// <summary>Submit a score to a named Steam leaderboard (best-kept).</summary>
        public async void SubmitLeaderboard(string name, int score)
        {
#if ELIMINATED_STEAM
            if (!Available) return;
            try
            {
                var board = await SteamUserStats.FindOrCreateLeaderboardAsync(
                    name, LeaderboardSort.Descending, LeaderboardDisplay.Numeric);
                if (board.HasValue) await board.Value.SubmitScoreAsync(score);
            }
            catch (Exception e) { Debug.LogWarning($"[Steam] leaderboard '{name}': {e.Message}"); }
#else
            await System.Threading.Tasks.Task.CompletedTask;
#endif
        }

        /// <summary>Advertise a joinable lobby code via Rich Presence (friend invites).</summary>
        public void SetRichPresence(string lobbyCode)
        {
#if ELIMINATED_STEAM
            if (!Available) return;
            SteamFriends.SetRichPresence("connect", lobbyCode);
            SteamFriends.SetRichPresence("status", string.IsNullOrEmpty(lobbyCode) ? "In menus" : $"In a room ({lobbyCode})");
#endif
        }

        private void Update()
        {
#if ELIMINATED_STEAM
            if (Available) SteamClient.RunCallbacks();
#endif
        }

        private void OnDestroy()
        {
#if ELIMINATED_STEAM
            if (Available) { SteamClient.Shutdown(); Available = false; }
#endif
        }
    }
}
