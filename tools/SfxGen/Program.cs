using System;
using System.Collections.Generic;
using System.IO;

namespace Eliminated.Tools.SfxGen
{
    /// <summary>
    /// Procedural SFX + music generator. Writes 16-bit PCM mono WAVs (44.1 kHz)
    /// — our own output, no third-party license. Ported in spirit from the
    /// reference game's Web Audio synthesis.
    /// </summary>
    public static class Program
    {
        private const int Rate = 44100;

        public static void Main(string[] args)
        {
            string outDir = args.Length > 0
                ? args[0]
                : Path.Combine("..", "..", "unity", "EliminatedGame", "Assets", "Eliminated", "Resources", "Audio");
            Directory.CreateDirectory(outDir);

            var clips = new Dictionary<string, float[]>
            {
                ["blip"] = Sfx(b => Tone(b, 0, 520, 0.08f, Wave.Square, 0.35f)),
                ["click"] = Sfx(b => Tone(b, 0, 320, 0.05f, Wave.Square, 0.3f, 220)),
                ["good"] = Sfx(b => { Tone(b, 0, 523, 0.10f, Wave.Sine, 0.3f); Tone(b, 0.08f, 784, 0.14f, Wave.Sine, 0.3f); }, 0.25f),
                ["bad"] = Sfx(b => Tone(b, 0, 200, 0.25f, Wave.Saw, 0.32f, 80)),
                ["whoosh"] = Sfx(b => { Noise(b, 0, 0.18f, 0.25f); Tone(b, 0, 420, 0.18f, Wave.Triangle, 0.18f, 700); }),
                ["throw"] = Sfx(b => { Noise(b, 0, 0.16f, 0.22f); Tone(b, 0, 440, 0.16f, Wave.Triangle, 0.18f, 720); }),
                ["catch"] = Sfx(b => Tone(b, 0, 660, 0.08f, Wave.Sine, 0.3f, 880)),
                ["explode"] = Sfx(b => { Tone(b, 0, 120, 0.4f, Wave.Saw, 0.35f, 40); Noise(b, 0, 0.4f, 0.3f); }, 0.45f),
                ["beep"] = Sfx(b => Tone(b, 0, 880, 0.12f, Wave.Square, 0.32f)),
                ["alarm"] = Sfx(b => { Tone(b, 0, 440, 0.18f, Wave.Saw, 0.3f); Tone(b, 0.18f, 660, 0.18f, Wave.Saw, 0.3f); }, 0.4f),
                ["chime"] = Sfx(b => { Tone(b, 0, 660, 0.12f, Wave.Sine, 0.28f); Tone(b, 0.1f, 990, 0.18f, Wave.Sine, 0.28f); }, 0.3f),
                ["pickup"] = Sfx(b => { Tone(b, 0, 740, 0.07f, Wave.Square, 0.3f); Tone(b, 0.06f, 1100, 0.10f, Wave.Square, 0.3f); }, 0.18f),
                ["death"] = Sfx(b => { Tone(b, 0, 300, 0.5f, Wave.Saw, 0.34f, 60); Noise(b, 0, 0.5f, 0.25f); }, 0.55f),
                ["shatter"] = Sfx(b => Noise(b, 0, 0.3f, 0.3f), 0.32f),
                ["jump"] = Sfx(b => Tone(b, 0, 300, 0.12f, Wave.Sine, 0.3f, 600)),
                ["drum"] = Sfx(b => Noise(b, 0, 0.12f, 0.35f)),
                ["win"] = Sfx(b =>
                {
                    float[] notes = { 523, 659, 784, 1047 };
                    for (int i = 0; i < notes.Length; i++) Tone(b, i * 0.12f, notes[i], 0.3f, Wave.Triangle, 0.3f);
                }, 0.7f),
                ["music"] = MusicLoop(),
            };

            foreach (var kv in clips)
            {
                string path = Path.Combine(outDir, kv.Key + ".wav");
                WriteWav(path, kv.Value);
                Console.WriteLine($"  wrote {Path.GetFileName(path)}  ({kv.Value.Length} samples, {kv.Value.Length / (float)Rate:0.00}s)");
            }
            WriteManifest(outDir, clips);
            Console.WriteLine($"Generated {clips.Count} clips into {outDir}");
        }

        private enum Wave { Sine, Square, Saw, Triangle }

        private static float[] Sfx(Action<float[]> build, float seconds = 0.2f)
        {
            var buf = new float[(int)(seconds * Rate)];
            build(buf);
            Normalize(buf, 0.85f);
            return buf;
        }

        private static void Tone(float[] buf, float startSec, float freq, float durSec, Wave wave, float gain, float slideTo = 0f)
        {
            int start = (int)(startSec * Rate);
            int len = (int)(durSec * Rate);
            const float attack = 0.012f;
            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if (idx < 0 || idx >= buf.Length) continue;
                float t = i / (float)Rate;
                float f = slideTo > 0f ? freq * (float)Math.Pow(slideTo / freq, t / durSec) : freq;
                // phase via running integral (approx: f roughly constant within a sample)
                float phase = f * t;
                float s = Oscillate(wave, phase);
                float env = Math.Min(1f, t / attack) * (float)Math.Exp(-3.2f * (t / durSec));
                buf[idx] += s * gain * env;
            }
        }

        private static void Noise(float[] buf, float startSec, float durSec, float gain, float _filterHz = 0f)
        {
            int start = (int)(startSec * Rate);
            int len = (int)(durSec * Rate);
            var rng = new Random(12345 + start);
            float prev = 0f;
            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if (idx < 0 || idx >= buf.Length) continue;
                float t = i / (float)Rate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                prev = 0.6f * prev + 0.4f * white; // gentle low-pass so it reads as a burst, not hiss
                float env = (float)Math.Exp(-4f * (t / durSec));
                buf[idx] += prev * gain * env;
            }
        }

        private static float Oscillate(Wave wave, float phase)
        {
            float p = phase - (float)Math.Floor(phase);
            switch (wave)
            {
                case Wave.Sine: return (float)Math.Sin(2 * Math.PI * phase);
                case Wave.Square: return p < 0.5f ? 1f : -1f;
                case Wave.Saw: return 2f * p - 1f;
                case Wave.Triangle: return 1f - 4f * Math.Abs(p - 0.5f);
                default: return 0f;
            }
        }

        private static float[] MusicLoop()
        {
            // ~4s gentle arpeggio loop (A-ish scale), bass every 4th step.
            float[] scale = { 220f, 261.63f, 293.66f, 329.63f, 392f, 440f, 523.25f };
            float step = 0.26f;
            int steps = 16;
            var buf = new float[(int)(steps * step * Rate)];
            for (int i = 0; i < steps; i++)
            {
                float freq = scale[(i * 3) % scale.Length];
                Tone(buf, i * step, freq, 0.22f, Wave.Triangle, 0.12f);
                if (i % 4 == 0) Tone(buf, i * step, freq / 2f, 0.4f, Wave.Sine, 0.10f);
            }
            Normalize(buf, 0.7f);
            return buf;
        }

        private static void Normalize(float[] buf, float peak)
        {
            float max = 1e-6f;
            foreach (var s in buf) max = Math.Max(max, Math.Abs(s));
            float g = peak / max;
            if (g < 1f) for (int i = 0; i < buf.Length; i++) buf[i] *= g;
        }

        private static void WriteWav(string path, float[] samples)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int dataBytes = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);              // fmt chunk size
            w.Write((short)1);        // PCM
            w.Write((short)1);        // mono
            w.Write(Rate);
            w.Write(Rate * 2);        // byte rate
            w.Write((short)2);        // block align
            w.Write((short)16);       // bits
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            foreach (var s in samples)
            {
                int v = (int)(Math.Clamp(s, -1f, 1f) * short.MaxValue);
                w.Write((short)v);
            }
        }

        private static void WriteManifest(string dir, Dictionary<string, float[]> clips)
        {
            using var sw = new StreamWriter(Path.Combine(dir, "SFX_MANIFEST.md"));
            sw.WriteLine("# Generated audio (our own procedural output — no third-party license)");
            sw.WriteLine();
            sw.WriteLine("Produced by `tools/SfxGen` (re-run: `dotnet run --project tools/SfxGen -- <this-dir>`).");
            sw.WriteLine("16-bit PCM mono WAV @ 44.1 kHz.");
            sw.WriteLine();
            foreach (var kv in clips)
                sw.WriteLine($"- `{kv.Key}.wav` — {kv.Value.Length / (float)Rate:0.00}s");
        }
    }
}
