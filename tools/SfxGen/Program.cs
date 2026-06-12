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
                // NOTE: music.wav is NOT generated here any more — it is a sourced track
                // ("Pink Soldiers (Nisalo Remix)", first 25s, seamless loop). The
                // procedural MusicLoop() below is kept for reference/fallback but is
                // intentionally not written, so re-running SfxGen never clobbers it.
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
            // "Pink Soldiers"-style menace (Squid Game): the signature isn't a tune
            // that goes somewhere — it's a fast, hypnotic OSCILLATION that pivots on
            // one repeated note and alternates with a shifting neighbour (the real
            // theme does G# F G# F G# G G# G). That relentless wobble is the dread
            // (time running out) and the childlike sing-song at once. Swelling minor
            // strings drift in underneath. Inspired by the motif's shape, not copied —
            // our own pitches and harmony. Original procedural output, no license.
            const float A2 = 110f, A3 = 220f, E4 = 329.63f,
                        F4 = 349.23f, Gs4 = 415.30f, A4 = 440f;

            const float beat = 0.72f;             // ~83 BPM — driving, but no drum kit
            const float swing = 0.54f;            // subtle lilt; off-beat 8th lands a hair late
            const int beatsPerBar = 4, bars = 4;
            int beats = bars * beatsPerBar;       // 16
            int n = bars * beatsPerBar * 2;       // 32 eighths
            var buf = new float[(int)(beats * beat * Rate)];

            // The oscillating motif (0 = rest). Pivot A4 always on top, wobbling DOWN to
            // a neighbour: a wide tense one (F = the flat-6th; E in bar 3 for variety)
            // and — crucially — the half-step G#, the harmonic-minor leading tone. That
            // chromatic A↔G# rub is the eerie bite the whole-tone version was missing.
            // Hypnotic and unresolved over a hollow A drone — never sad, never one note.
            float[] mel =
            {
                A4, F4, A4, F4, A4, Gs4, A4, Gs4,   // bar 1
                A4, F4, A4, F4, A4, Gs4, A4, Gs4,   // bar 2
                A4, E4, A4, E4, A4, Gs4, A4, Gs4,   // bar 3 — wider hollow neighbour (E)
                A4, F4, A4, Gs4, A4, Gs4, A4, 0,    // bar 4 — mix, then breathe before the loop
            };

            // Layer 1 — the oscillating recorder lead, swung 8ths.
            for (int i = 0; i < n; i++)
            {
                if (mel[i] <= 0f) continue;
                bool offb = (i & 1) == 1;
                float t = (i / 2) * beat + (offb ? beat * swing : 0f);
                float dur = (offb ? beat * (1f - swing) : beat * swing) * 0.92f;
                Lead(buf, t, mel[i], dur, 0.22f);
            }

            // Layers 2-4 — a hollow open-fifth A drone (no third, so it never turns
            // happy or sad; the melody's F and G# colour it), restruck each half so the
            // strings "come in", plus a bass pedal and a soft per-bar boom.
            for (int s = 0; s < 2; s++)
            {
                float t0 = s * 2 * beatsPerBar * beat;          // start of this 2-bar half
                float secLen = 2 * beatsPerBar * beat;
                Pad(buf, t0, A3, secLen * 0.99f, 0.06f);
                Pad(buf, t0, E4, secLen * 0.99f, 0.045f);
                Tone(buf, t0, A2, secLen * 0.97f, Wave.Sine, 0.12f);  // bass pedal
            }
            for (int bar = 0; bar < bars; bar++)
                Tone(buf, bar * beatsPerBar * beat, A2 / 2f, beat * 0.8f, Wave.Sine, 0.16f);

            // Warmth + space: this is what stops the bed sounding like chiptune. A soft
            // low-pass rounds off the harsh synth edges, then a reverb tail turns the
            // dry tones into an atmosphere. The reverb runs on the loop doubled
            // end-to-end (we keep the second copy) so the tail wraps cleanly at the loop.
            LowPass(buf, 0.55f);
            buf = ReverbLoop(buf, 0.30f);

            // Shared Normalize only attenuates; this bed is well under full scale, so
            // scale it explicitly to a steady, audible background level (the music
            // source further scales by the saved musicVolume at runtime).
            float max = 1e-6f;
            foreach (var v in buf) max = Math.Max(max, Math.Abs(v));
            float gain = 0.5f / max;
            for (int i = 0; i < buf.Length; i++) buf[i] *= gain;
            return buf;
        }

        // Warm ocarina-ish lead: two slightly detuned voices (a touch of chorus to
        // kill the sterile "chiptune" purity), mostly fundamental with a soft octave
        // for warmth, plus a gentle vibrato that fades in (natural, not a wobble).
        private static void Lead(float[] buf, float startSec, float freq, float durSec, float gain)
        {
            int start = (int)(startSec * Rate);
            int len = (int)(durSec * Rate);
            const float attack = 0.03f, release = 0.09f;
            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if (idx < 0 || idx >= buf.Length) continue;
                float t = i / (float)Rate;
                // Vibrato fades in over ~0.25s so fast notes stay in tune.
                float vibDepth = 0.003f * Math.Min(1f, t / 0.25f);
                float f = freq * (1f + vibDepth * (float)Math.Sin(2 * Math.PI * 5f * t));
                float ph = f * t;
                float ph2 = f * 1.004f * t;     // detuned partner → chorus thickness
                float s = 0.6f * (float)Math.Sin(2 * Math.PI * ph)
                        + 0.5f * (float)Math.Sin(2 * Math.PI * ph2)
                        + 0.18f * (float)Math.Sin(2 * Math.PI * 2f * ph)   // soft octave
                        + 0.05f * (float)Math.Sin(2 * Math.PI * 3f * ph);  // faint edge
                float env = Math.Min(1f, t / attack);
                float rem = durSec - t;
                if (rem < release) env *= Math.Max(0f, rem / release);
                buf[idx] += s * gain * env;
            }
        }

        // One-pole low-pass (a in (0,1]; smaller = darker) — rounds off harsh edges.
        private static void LowPass(float[] buf, float a)
        {
            float prev = 0f;
            for (int i = 0; i < buf.Length; i++) { prev += a * (buf[i] - prev); buf[i] = prev; }
        }

        // Schroeder/Freeverb-style mono reverb: 4 damped feedback combs in parallel
        // into 2 series all-pass filters. Turns dry tones into an atmosphere.
        private static float[] Reverb(float[] dry, float wet)
        {
            int[] comb = { 1116, 1188, 1277, 1356 };
            float[] fb = { 0.84f, 0.82f, 0.80f, 0.78f };
            const float damp = 0.2f;
            int[] ap = { 556, 441 };
            const float apG = 0.5f;

            var cBuf = new float[comb.Length][];
            var cFilt = new float[comb.Length];
            var cIdx = new int[comb.Length];
            for (int c = 0; c < comb.Length; c++) cBuf[c] = new float[comb[c]];
            var aBuf = new float[ap.Length][];
            var aIdx = new int[ap.Length];
            for (int a = 0; a < ap.Length; a++) aBuf[a] = new float[ap[a]];

            var outp = new float[dry.Length];
            for (int i = 0; i < dry.Length; i++)
            {
                float input = dry[i] * 0.5f;
                float sum = 0f;
                for (int c = 0; c < comb.Length; c++)
                {
                    float d = cBuf[c][cIdx[c]];
                    sum += d;
                    cFilt[c] = d * (1f - damp) + cFilt[c] * damp;
                    cBuf[c][cIdx[c]] = input + cFilt[c] * fb[c];
                    if (++cIdx[c] >= comb[c]) cIdx[c] = 0;
                }
                float y = sum;
                for (int a = 0; a < ap.Length; a++)
                {
                    float bo = aBuf[a][aIdx[a]];
                    float yy = -y + bo;
                    aBuf[a][aIdx[a]] = y + bo * apG;
                    if (++aIdx[a] >= ap[a]) aIdx[a] = 0;
                    y = yy;
                }
                outp[i] = dry[i] * (1f - wet) + y * wet;
            }
            return outp;
        }

        // Reverb a seamless loop: run the loop twice through the reverb and keep the
        // second copy, so the tail from the end has already wrapped into the start.
        private static float[] ReverbLoop(float[] loop, float wet)
        {
            int nN = loop.Length;
            var dbl = new float[nN * 2];
            Array.Copy(loop, 0, dbl, 0, nN);
            Array.Copy(loop, 0, dbl, nN, nN);
            var rev = Reverb(dbl, wet);
            var outp = new float[nN];
            Array.Copy(rev, nN, outp, 0, nN);
            return outp;
        }

        // Soft sustained pad voice: two lightly detuned sines that swell in slowly
        // and hold — the ominous "strings" that drift in underneath the melody.
        private static void Pad(float[] buf, float startSec, float freq, float durSec, float gain)
        {
            int start = (int)(startSec * Rate);
            int len = (int)(durSec * Rate);
            const float attack = 0.4f, release = 0.3f;
            for (int i = 0; i < len; i++)
            {
                int idx = start + i;
                if (idx < 0 || idx >= buf.Length) continue;
                float t = i / (float)Rate;
                float s = (float)Math.Sin(2 * Math.PI * freq * t)
                        + 0.6f * (float)Math.Sin(2 * Math.PI * freq * 1.004f * t);
                float env = Math.Min(1f, t / attack);
                float rem = durSec - t;
                if (rem < release) env *= Math.Max(0f, rem / release);
                buf[idx] += s * gain * env;
            }
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
