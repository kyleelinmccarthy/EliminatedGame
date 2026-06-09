namespace Eliminated.Sim.Core
{
    /// <summary>
    /// Generates shareable room codes. Uses an unambiguous alphabet (no O/0, I/1)
    /// like the reference makeRoomCode, but is seedable so codes are deterministic
    /// in tests.
    /// </summary>
    public static class RoomCode
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no ambiguous chars

        public static string Make(Rng rng, int length = -1)
        {
            int len = length > 0 ? length : Constants.RoomCodeLen;
            var chars = new char[len];
            for (int i = 0; i < len; i++)
                chars[i] = Alphabet[rng.NextInt(Alphabet.Length)];
            return new string(chars);
        }

        public static string Make(int seed, int length = -1) => Make(new Rng(seed), length);
    }
}
