using System;
using System.Collections.Generic;
using System.IO;

namespace Eliminated.Tools.ArtGen
{
    /// <summary>
    /// Generates the 6 themed arena floor textures as uncompressed 32-bit TGA
    /// images (our own procedural output — no third-party license). Themes/palettes
    /// match docs/GAME_DESIGN.md. Unity imports TGA natively.
    /// </summary>
    public static class Program
    {
        private const int Size = 256;
        private const int Cell = 32;

        private static readonly (string id, string ground, string ground2, string accent)[] Themes =
        {
            ("courtyard", "f7d9e3", "f3c6d6", "c98aa6"),
            ("neon",      "1b1f3b", "252a52", "00e5ff"),
            ("candy",     "ffe0ec", "ffd0e0", "c86fa0"),
            ("toxic",     "dff5d0", "cdebb6", "7cb342"),
            ("beach",     "ffe2bf", "ffd0a0", "ef9a5a"),
            ("haunt",     "2c2240", "352a4d", "7e57c2"),
        };

        public static void Main(string[] args)
        {
            string outDir = args.Length > 0
                ? args[0]
                : Path.Combine("..", "..", "unity", "EliminatedGame", "Assets", "Eliminated", "Resources", "Art");
            Directory.CreateDirectory(outDir);

            foreach (var t in Themes)
            {
                var px = Floor(Hex(t.ground), Hex(t.ground2), Hex(t.accent));
                string path = Path.Combine(outDir, $"floor_{t.id}.tga");
                WriteTga(path, px, Size, Size);
                Console.WriteLine($"  wrote {Path.GetFileName(path)}  ({Size}x{Size})");
            }
            using (var sw = new StreamWriter(Path.Combine(outDir, "ART_MANIFEST.md")))
            {
                sw.WriteLine("# Generated arena art (our own procedural output, no third-party license)");
                sw.WriteLine();
                sw.WriteLine("Produced by `tools/ArtGen`. 32-bit TGA, 256x256, themed checkerboard floors.");
                foreach (var t in Themes) sw.WriteLine($"- `floor_{t.id}.tga`");
            }
            Console.WriteLine($"Generated {Themes.Length} floor textures into {outDir}");
        }

        private static (byte r, byte g, byte b) Hex(string h) =>
            (Convert.ToByte(h.Substring(0, 2), 16), Convert.ToByte(h.Substring(2, 2), 16), Convert.ToByte(h.Substring(4, 2), 16));

        // Returns BGRA bytes, top-to-bottom.
        private static byte[] Floor((byte r, byte g, byte b) a, (byte r, byte g, byte b) b2, (byte r, byte g, byte b) accent)
        {
            var px = new byte[Size * Size * 4];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                bool alt = ((x / Cell) + (y / Cell)) % 2 == 0;
                var c = alt ? a : b2;
                // subtle accent grout lines on cell borders
                bool grout = (x % Cell < 2) || (y % Cell < 2);
                if (grout) c = accent;
                int i = (y * Size + x) * 4;
                px[i + 0] = c.b; px[i + 1] = c.g; px[i + 2] = c.r; px[i + 3] = 255;
            }
            return px;
        }

        private static void WriteTga(string path, byte[] bgra, int w, int h)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var bw = new BinaryWriter(fs);
            bw.Write((byte)0);   // id length
            bw.Write((byte)0);   // no color map
            bw.Write((byte)2);   // uncompressed true-color
            bw.Write((short)0); bw.Write((short)0); bw.Write((byte)0); // color map spec
            bw.Write((short)0); bw.Write((short)0); // x/y origin
            bw.Write((short)w); bw.Write((short)h);
            bw.Write((byte)32);  // bits per pixel
            bw.Write((byte)0x28); // descriptor: 8 alpha bits + top-left origin
            bw.Write(bgra);
        }
    }
}
