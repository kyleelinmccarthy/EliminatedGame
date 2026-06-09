using System;
using System.Collections.Generic;
using Eliminated.Sim.Core;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Games
{
    /// <summary>
    /// A single minigame round. The room drives it: <see cref="Start"/>, then
    /// <see cref="Tick"/> at fixed dt while feeding <see cref="OnInput"/>, until
    /// <see cref="IsDone"/>, then reads <see cref="Result"/>. Mirrors the
    /// reference Minigame interface (lib/server/games/Minigame.ts).
    /// </summary>
    public interface IMinigame
    {
        GameId Id { get; }

        /// <summary>Initialize actor positions and game state.</summary>
        void Start();

        /// <summary>Apply one player's input (ignored before play starts).</summary>
        void OnInput(string actorId, GameInput input);

        /// <summary>Advance the simulation by a fixed timestep.</summary>
        void Tick(float dt);

        /// <summary>True once the round has resolved.</summary>
        bool IsDone { get; }

        /// <summary>Final ranking and survivors. Valid once <see cref="IsDone"/>.</summary>
        RoundResult Result();

        /// <summary>A player left/was kicked: remove them (die in place).</summary>
        void Forfeit(string actorId);

        /// <summary>Build the client-facing snapshot for the current tick.</summary>
        Snapshot BuildSnapshot();
    }

    /// <summary>
    /// Everything a minigame needs from the room: its participants, deterministic
    /// RNG, pacing, and an effect sink. Ported from the reference GameContext.
    /// </summary>
    public sealed class GameContext
    {
        /// <summary>Actors entering the game (alive participants, bots included).</summary>
        public List<Actor> Actors = new List<Actor>();

        public Rng Rng;
        public bool FriendlyFire = true;

        public int RoundIndex;
        public int TotalRounds = -1;     // −1 = mystery / unknown
        public bool IsFinale;

        /// <summary>0..1 cull/difficulty strength for series pacing.</summary>
        public float Intensity = 0.3f;

        public bool Night;

        /// <summary>Hardcore finale: the game must end with exactly one survivor.</summary>
        public bool ForceSingleSurvivor;

        /// <summary>Optional one-shot effect sink (for the view).</summary>
        public Action<Effect> EmitFx;
    }
}
