using UnityEngine;
using Eliminated.Game.SimBridge;
using Eliminated.Game.View;
using Eliminated.Game.Input;
using Eliminated.Game.UI;
using Eliminated.Game.Audio;
using Eliminated.Game.Net;
using Eliminated.Game.Platform;
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
            // Guard against a duplicate app (e.g. a stale one persisted from a prior
            // session): never run two EliminatedApps — that doubles the HUD/sim/stage.
            if (GameObject.Find("EliminatedApp") != null) return;

            SaveService.Load();

            var app = new GameObject("EliminatedApp");
            Object.DontDestroyOnLoad(app);

            var sim = app.AddComponent<SimRunner>();
            var net = app.AddComponent<NetClient>();

            // The view/input/HUD read the simulation through one swappable router;
            // solo & local co-op point it at the in-process SimRunner, Play Online
            // points it at the NetClient. Switching modes is a single assignment.
            var router = new SessionRouter { Active = sim };

            var audio = app.AddComponent<AudioService>();
            audio.Init();

            var arena = app.AddComponent<ArenaView>();
            arena.Init(router);

            var input = app.AddComponent<LocalInputHub>();
            input.Init(router, arena);

            var touch = app.AddComponent<TouchControls>();
            touch.Init(router);

            var steam = app.AddComponent<SteamService>();
            steam.Init(480); // Spacewar test app id; replace with the real one for release

            var hud = app.AddComponent<HudUi>();
            hud.Init(sim, net, router);

            Debug.Log("[ELIMINATED] Booted vertical slice — open the menu and start a solo series.");
        }
    }
}
