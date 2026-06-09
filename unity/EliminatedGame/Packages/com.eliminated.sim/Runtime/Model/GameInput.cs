namespace Eliminated.Sim.Model
{
    /// <summary>
    /// A single player input for the active minigame. A flat struct with factory
    /// helpers (rather than a class hierarchy) keeps it cheap to send over the
    /// wire and easy to value-compare in tests. Mirrors the reference protocol's
    /// GameInput union (move/action/aim/choose/tap).
    /// </summary>
    public struct GameInput
    {
        public InputKind Kind;

        // Move
        public float Dx;
        public float Dy;

        // Action
        public string Name;  // "jump","throw","dash","pull","shove","spike"
        public bool On;      // press (true) / release (false)

        // Aim
        public float Angle;  // radians

        // Choose
        public string Value; // "left"/"right", rps throw, target id, …

        /// <summary>Optional client sequence number for de-duplication.</summary>
        public int Seq;

        public static GameInput Move(float dx, float dy, int seq = 0) => new GameInput
        {
            Kind = InputKind.Move,
            Dx = dx,
            Dy = dy,
            Seq = seq
        };

        public static GameInput Action(string name, bool on = true, int seq = 0) => new GameInput
        {
            Kind = InputKind.Action,
            Name = name,
            On = on,
            Seq = seq
        };

        public static GameInput Aim(float angle, int seq = 0) => new GameInput
        {
            Kind = InputKind.Aim,
            Angle = angle,
            Seq = seq
        };

        public static GameInput Choose(string value, int seq = 0) => new GameInput
        {
            Kind = InputKind.Choose,
            Value = value,
            Seq = seq
        };

        public static GameInput Tap(int seq = 0) => new GameInput
        {
            Kind = InputKind.Tap,
            Seq = seq
        };
    }
}
