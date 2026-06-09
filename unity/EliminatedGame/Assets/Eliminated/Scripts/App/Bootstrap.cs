using UnityEngine;
using Eliminated.Sim.Core;

namespace Eliminated.Game.App
{
    /// <summary>
    /// Placeholder entry point that proves the Unity client assembly can see the
    /// pure-C# simulation package (<c>Eliminated.Sim</c>). Phase 2 replaces this
    /// with the real boot/scene-flow director. Kept tiny on purpose (KISS).
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        private void Awake()
        {
            // If this logs, the sim package is wired into the Unity project.
            Debug.Log(
                $"[ELIMINATED] Simulation linked. Tick {Constants.TickHz}Hz, " +
                $"arena {Constants.ArenaW}x{Constants.ArenaH}, " +
                $"currency {Constants.Currency} {Constants.CurrencyIcon}.");
        }
    }
}
