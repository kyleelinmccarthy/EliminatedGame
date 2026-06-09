using UnityEngine;
using Eliminated.Game.SimBridge;
using Eliminated.Game.View;
using Eliminated.Game.Input;
using Eliminated.Game.UI;
using Eliminated.Game.Save;

namespace Eliminated.Game.App
{
    /// <summary>
    /// Single entry point for the vertical slice. Runs automatically before the
    /// first scene loads, so pressing Play in <em>any</em> scene boots the whole
    /// game with no hand-authored scene or prefab wiring. Phase 3 introduces real
    /// Boot/MainMenu/Game scenes and a UI Toolkit front end; this keeps the slice
    /// runnable today.
    /// </summary>
    public static class GameBootstrapper
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            SaveService.Load();

            var app = new GameObject("EliminatedApp");
            Object.DontDestroyOnLoad(app);

            var sim = app.AddComponent<SimRunner>();

            var arena = app.AddComponent<ArenaView>();
            arena.Init(sim);

            var input = app.AddComponent<LocalInputHub>();
            input.Init(sim, arena);

            var hud = app.AddComponent<HudUi>();
            hud.Init(sim);

            Debug.Log("[ELIMINATED] Booted vertical slice — open the menu and start a solo series.");
        }
    }
}
