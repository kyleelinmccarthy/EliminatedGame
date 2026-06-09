namespace Eliminated.Sim.Model
{
    /// <summary>The 16 minigames. String ids match the reference game's GameId.</summary>
    public enum GameId
    {
        RedLight,       // redlight
        Tag,            // tag
        Mingle,         // mingle
        GlassBridge,    // glassbridge
        TugOfWar,       // tugofwar
        RpsMinusOne,    // rpsminusone
        JumpRope,       // jumprope
        Boomerang,      // boomerang
        Dodgeball,      // dodgeball
        MusicalChairs,  // musicalchairs
        PresentSwap,    // present
        PropHunt,       // prophunt
        ChutesAndLadders, // chutesladders
        SimonSays,      // simonsays
        KeepyUppy,      // keepyuppy
        KingOfTheHill   // koth
    }

    /// <summary>Series death rule.</summary>
    public enum SeriesMode
    {
        /// <summary>Permadeath for the whole series; last blob standing wins.</summary>
        Hardcore,
        /// <summary>Respawn each round; win on points.</summary>
        Casual
    }

    /// <summary>Room/series state-machine phase.</summary>
    public enum RoomPhase
    {
        Lobby,
        Intro,
        Playing,
        RoundResult,
        SeriesResult
    }

    /// <summary>Kind of player input for the current game.</summary>
    public enum InputKind
    {
        Move,    // dx, dy in [-1, 1]
        Action,  // named action: jump/throw/dash/pull/shove/spike
        Aim,     // angle in radians
        Choose,  // discrete choice: "left"/"right", an rps throw, a target id
        Tap      // generic button press
    }

    /// <summary>View animation state for an actor (client rendering only).</summary>
    public enum AnimState
    {
        Idle,
        Run,
        Cheer,
        Dead,
        Fall
    }

    /// <summary>One-shot visual effect kinds emitted by the sim for the client.</summary>
    public enum EffectKind
    {
        Death,
        Confetti,
        Spark,
        Splat,
        Ring,
        Shatter,
        Pickup,
        Freeze,
        Thaw,
        Burn,
        Shove,
        Catch,
        Throw,
        Shake
    }
}
