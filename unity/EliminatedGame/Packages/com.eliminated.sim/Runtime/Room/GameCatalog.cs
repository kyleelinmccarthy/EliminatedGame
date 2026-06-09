using System;
using System.Collections.Generic;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Room
{
    /// <summary>How hard a game thins the field — used for series pacing.</summary>
    public enum CullStrength { Low, Mid, High }

    /// <summary>Static metadata + factory for one minigame.</summary>
    public sealed class GameMeta
    {
        public GameId Id;
        public string Name;
        public string Icon;
        public int MinPlayers = 2;
        public bool RequiresEven;     // teams / 1v1 pairings need an even field
        public CullStrength Cull = CullStrength.Mid;
        public bool Finale;           // finale-only game (e.g. King of the Hill)
        public bool FinaleCapable;    // can be told to leave exactly one survivor
        public bool Nightable;        // eligible for hardcore night mode
        public Func<GameContext, IMinigame> Factory;
    }

    /// <summary>
    /// Registry of available minigames and their pacing metadata. New games
    /// register here as they are implemented (Phase 3 fills in the remaining 13).
    /// Mirrors the reference lib/shared/games.ts + registry.ts.
    /// </summary>
    public static class GameCatalog
    {
        private static readonly Dictionary<GameId, GameMeta> Meta = new Dictionary<GameId, GameMeta>
        {
            [GameId.RedLight] = new GameMeta
            {
                Id = GameId.RedLight, Name = "Red Light, Green Light", Icon = "🚦",
                MinPlayers = 2, RequiresEven = false, Cull = CullStrength.Mid,
                Factory = ctx => new RedLightGreenLight(ctx)
            },
            [GameId.TugOfWar] = new GameMeta
            {
                Id = GameId.TugOfWar, Name = "Tug of War", Icon = "🪢",
                MinPlayers = 2, RequiresEven = true, Cull = CullStrength.High,
                Factory = ctx => new TugOfWar(ctx)
            },
            [GameId.Boomerang] = new GameMeta
            {
                Id = GameId.Boomerang, Name = "Boomerang Brawl", Icon = "🪃",
                MinPlayers = 2, RequiresEven = false, Cull = CullStrength.Mid,
                FinaleCapable = true, Nightable = true,
                Factory = ctx => new Boomerang(ctx)
            },
            [GameId.Tag] = new GameMeta
            {
                Id = GameId.Tag, Name = "Freeze Tag", Icon = "❄️",
                MinPlayers = 2, RequiresEven = true, Cull = CullStrength.Mid,
                Nightable = true,
                Factory = ctx => new Tag(ctx)
            },
            [GameId.KingOfTheHill] = new GameMeta
            {
                Id = GameId.KingOfTheHill, Name = "King of the Lava Islands", Icon = "🌋",
                MinPlayers = 2, RequiresEven = false, Cull = CullStrength.High,
                Finale = true, FinaleCapable = true, Nightable = true,
                Factory = ctx => new KingOfTheHill(ctx)
            },
        };

        public static IReadOnlyCollection<GameId> Registered => Meta.Keys;
        public static bool IsRegistered(GameId id) => Meta.ContainsKey(id);
        public static GameMeta Of(GameId id) => Meta[id];
        public static IMinigame Create(GameId id, GameContext ctx) => Meta[id].Factory(ctx);
    }
}
