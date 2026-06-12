using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Eliminated.Tools.VoiceGen
{
    /// <summary>
    /// Renders the Game Master announcer voice bank as 16-bit PCM mono WAVs using
    /// espeak-ng (a build-time tool only — see VoiceGen.csproj). Two robotic PA
    /// voices, matching the web build: a MALE announcer for game reveals (and
    /// Simon Says orders) and a FEMALE voice for eliminations. The clips are a
    /// small fixed vocabulary the runtime stitches together (Announcer.cs):
    /// "Game three." + "Tug of war.", or "Player eliminated." Each clip is
    /// silence-trimmed and peak-normalized so back-to-back playback is gapless and
    /// even in level.
    /// </summary>
    public static class Program
    {
        // espeak-ng voice + prosody. Flat, slightly slow, low — robotic PA, never
        // chirpy. Mirrors the web's rate 0.85 / pitch 0.7-0.8 Game Master delivery.
        private const string MaleVoice = "en-us+m3";
        private const string FemaleVoice = "en-us+f3";
        private const int MaleRate = 150, MalePitch = 32;     // wpm, 0..99
        private const int FemaleRate = 158, FemalePitch = 55;

        private enum V { Male, Female }

        public static int Main(string[] args)
        {
            string outDir = args.Length > 0
                ? args[0]
                : Path.Combine("..", "..", "unity", "EliminatedGame", "Assets", "Eliminated", "Resources", "Audio", "voice");
            Directory.CreateDirectory(outDir);

            if (!EspeakAvailable())
            {
                Console.Error.WriteLine(
                    "espeak-ng not found on PATH. Install it (e.g. `apt install espeak-ng`, `brew install espeak-ng`) " +
                    "and re-run. This is a build-time tool only; the shipped game just plays the generated WAVs.");
                return 1;
            }

            var clips = BuildSpecs();
            int ok = 0;
            foreach (var c in clips)
            {
                string path = Path.Combine(outDir, c.Key + ".wav");
                if (Render(c.Voice, c.Text, path, out string err))
                {
                    ok++;
                    Console.WriteLine($"  wrote {c.Key}.wav  [{(c.Voice == V.Male ? "M" : "F")}]  \"{c.Text}\"");
                }
                else
                {
                    Console.Error.WriteLine($"  FAILED {c.Key}: {err}");
                }
            }

            WriteManifest(outDir, clips);
            Console.WriteLine($"Generated {ok}/{clips.Count} announcer clips into {outDir}");
            return ok == clips.Count ? 0 : 2;
        }

        private readonly struct Spec
        {
            public readonly string Key;
            public readonly string Text;
            public readonly V Voice;
            public Spec(string key, string text, V voice) { Key = key; Text = text; Voice = voice; }
        }

        private static List<Spec> BuildSpecs()
        {
            var list = new List<Spec>();

            // --- Male announcer: round numbers --------------------------------------
            // "Game one." .. "Game twenty." The runtime plays one of these then the
            // game-name clip. 20 covers any realistic series length; past that the
            // runtime simply drops the number and announces the game name alone.
            string[] ones =
            {
                "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
                "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen",
                "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
            };
            for (int n = 1; n <= 20; n++)
                list.Add(new Spec($"game_{n:00}", $"Game {ones[n]}.", V.Male));
            list.Add(new Spec("final_game", "The final game.", V.Male));

            // --- Male announcer: the 16 game names ----------------------------------
            // Spoken names match the web's display names (RPS uses its spokenName
            // override). Keys mirror Announcer.GameKey(GameId).
            list.Add(new Spec("name_redlight", "Red light, green light.", V.Male));
            list.Add(new Spec("name_tag", "Freeze tag.", V.Male));
            list.Add(new Spec("name_mingle", "Mingle.", V.Male));
            list.Add(new Spec("name_glassbridge", "Glass stepping stones.", V.Male));
            list.Add(new Spec("name_tugofwar", "Tug of war.", V.Male));
            list.Add(new Spec("name_rps", "Rock, paper, scissors. Minus one.", V.Male));
            list.Add(new Spec("name_jumprope", "Killer jump rope.", V.Male));
            list.Add(new Spec("name_boomerang", "Boomerang brawl.", V.Male));
            list.Add(new Spec("name_dodgeball", "Dodgeball.", V.Male));
            list.Add(new Spec("name_musicalchairs", "Musical chairs.", V.Male));
            list.Add(new Spec("name_present", "Secret Santa sabotage.", V.Male));
            list.Add(new Spec("name_prophunt", "Prop hunt.", V.Male));
            list.Add(new Spec("name_chutesladders", "Chutes and ladders.", V.Male));
            list.Add(new Spec("name_simonsays", "Simon says.", V.Male));
            list.Add(new Spec("name_keepyuppy", "Keepy uppy.", V.Male));
            list.Add(new Spec("name_koth", "King of the lava islands.", V.Male));

            // --- Male announcer: arena room reveals ---------------------------------
            // "Welcome to <room>." played after the game name at the round intro. Keys
            // mirror Announcer."room_" + theme and the Loc "room.<theme>" display names.
            list.Add(new Spec("room_courtyard", "Welcome to The Courtyard.", V.Male));
            list.Add(new Spec("room_neon", "Welcome to Neon District.", V.Male));
            list.Add(new Spec("room_candy", "Welcome to Candy Kingdom.", V.Male));
            list.Add(new Spec("room_toxic", "Welcome to The Toxic Works.", V.Male));
            list.Add(new Spec("room_beach", "Welcome to Sunny Shores.", V.Male));
            list.Add(new Spec("room_haunt", "Welcome to Haunted Manor.", V.Male));

            // --- Male announcer: Simon Says orders ----------------------------------
            list.Add(new Spec("simon_head", "Simon says, pat your head.", V.Male));
            list.Add(new Spec("simon_nose", "Simon says, touch your nose.", V.Male));
            list.Add(new Spec("simon_blink", "Simon says, blink.", V.Male));
            list.Add(new Spec("simon_flip", "Simon says, flip.", V.Male));
            list.Add(new Spec("simon_jump", "Simon says, jump.", V.Male));
            list.Add(new Spec("simon_freeze", "Freeze! Touch nothing.", V.Male));

            // --- Female voice: eliminations -----------------------------------------
            list.Add(new Spec("elim_you", "You have been eliminated.", V.Female));
            list.Add(new Spec("elim_player", "Player eliminated.", V.Female));
            list.Add(new Spec("elim_players", "Players eliminated.", V.Female));

            // --- Female voice: per-player elimination, by lobby number --------------
            // "Player <N> has been eliminated." is stitched at runtime (Announcer.cs)
            // from these word clips, so any tag 1..456 can be called out without baking
            // a whole phrase for every number. Number words are rendered bare (no full
            // stop) so they flow mid-sentence; the closing "has been eliminated." carries
            // the falling intonation. Ones reuse the array above (1..19); tens + hundred
            // compose the rest (e.g. 387 → "three" "hundred" "eighty" "seven").
            string[] tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };
            list.Add(new Spec("num_player", "Player", V.Female));
            for (int n = 1; n <= 19; n++)
                list.Add(new Spec($"num_{n}", ones[n], V.Female));
            for (int t = 2; t <= 9; t++)
                list.Add(new Spec($"num_{t * 10}", tens[t], V.Female));
            list.Add(new Spec("num_hundred", "hundred", V.Female));
            list.Add(new Spec("num_elim", "has been eliminated.", V.Female));

            return list;
        }

        // ---- espeak-ng invocation + WAV post-processing ----------------------------

        private static bool EspeakAvailable()
        {
            try
            {
                using var p = Run("espeak-ng", new[] { "--version" }, out _, out _);
                return true;
            }
            catch { return false; }
        }

        private static bool Render(V voice, string text, string outPath, out string err)
        {
            err = null;
            string tmp = Path.GetTempFileName();
            string txt = Path.GetTempFileName();
            try
            {
                File.WriteAllText(txt, text);
                string vname = voice == V.Male ? MaleVoice : FemaleVoice;
                int rate = voice == V.Male ? MaleRate : FemaleRate;
                int pitch = voice == V.Male ? MalePitch : FemalePitch;
                // -z drops the trailing sentence pause; -f reads text from a file so
                // punctuation/quoting never reaches a shell.
                var argv = new[]
                {
                    "-v", vname, "-s", rate.ToString(), "-p", pitch.ToString(),
                    "-z", "-w", tmp, "-f", txt,
                };
                using (var p = Run("espeak-ng", argv, out string so, out string se))
                {
                    p.WaitForExit();
                    if (p.ExitCode != 0) { err = $"espeak exit {p.ExitCode}: {se}"; return false; }
                }

                var (samples, rateHz) = ReadWavMono(tmp);
                if (samples.Length == 0) { err = "espeak produced empty audio"; return false; }
                samples = TrimSilence(samples, 0.02f, (int)(0.035f * rateHz), (int)(0.06f * rateHz));
                Normalize(samples, 0.92f);
                FadeEdges(samples, (int)(0.006f * rateHz));
                WriteWavMono(outPath, samples, rateHz);
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
            finally { TryDelete(tmp); TryDelete(txt); }
        }

        private static Process Run(string exe, string[] argv, out string stdout, out string stderr)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in argv) psi.ArgumentList.Add(a);
            var p = Process.Start(psi);
            // Drain async to avoid pipe deadlock on larger output.
            stdout = p.StandardOutput.ReadToEnd();
            stderr = p.StandardError.ReadToEnd();
            return p;
        }

        // Trim leading/trailing samples below `thresh` (fraction of full scale),
        // keeping a short pad on each side so attacks/tails aren't clipped.
        private static float[] TrimSilence(float[] s, float thresh, int leadPad, int tailPad)
        {
            int n = s.Length;
            int start = 0, end = n - 1;
            while (start < n && Math.Abs(s[start]) < thresh) start++;
            while (end > start && Math.Abs(s[end]) < thresh) end--;
            if (start >= end) return s; // all-silent → leave as-is rather than empty
            start = Math.Max(0, start - leadPad);
            end = Math.Min(n - 1, end + tailPad);
            int len = end - start + 1;
            var outp = new float[len];
            Array.Copy(s, start, outp, 0, len);
            return outp;
        }

        private static void Normalize(float[] buf, float peak)
        {
            float max = 1e-6f;
            foreach (var v in buf) max = Math.Max(max, Math.Abs(v));
            float g = peak / max;
            for (int i = 0; i < buf.Length; i++) buf[i] *= g;
        }

        private static void FadeEdges(float[] buf, int fade)
        {
            fade = Math.Min(fade, buf.Length / 2);
            for (int i = 0; i < fade; i++)
            {
                float g = i / (float)fade;
                buf[i] *= g;
                buf[buf.Length - 1 - i] *= g;
            }
        }

        // Minimal RIFF/WAVE reader: walks chunks, decodes 16-bit (or 8/32f) PCM,
        // downmixes to mono. espeak emits canonical 16-bit mono, but be tolerant.
        private static (float[] samples, int rate) ReadWavMono(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 12 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F')
                throw new InvalidDataException("not a RIFF file");
            int rate = 22050, channels = 1, bits = 16;
            int pos = 12, dataPos = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int sz = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(b, body + 2);
                    rate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToInt16(b, body + 14);
                }
                else if (id == "data") { dataPos = body; dataLen = sz; }
                pos = body + sz + (sz & 1); // chunks are word-aligned
            }
            if (dataPos < 0) throw new InvalidDataException("no data chunk");
            dataLen = Math.Min(dataLen, b.Length - dataPos);

            int bytesPerSample = bits / 8;
            int frames = dataLen / (bytesPerSample * Math.Max(1, channels));
            var outp = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float acc = 0f;
                for (int c = 0; c < channels; c++)
                {
                    int o = dataPos + (f * channels + c) * bytesPerSample;
                    float v;
                    if (bits == 16) v = BitConverter.ToInt16(b, o) / 32768f;
                    else if (bits == 32) v = BitConverter.ToSingle(b, o);
                    else v = (b[o] - 128) / 128f; // 8-bit unsigned
                    acc += v;
                }
                outp[f] = acc / channels;
            }
            return (outp, rate);
        }

        private static void WriteWavMono(string path, float[] samples, int rate)
        {
            using var fs = new FileStream(path, FileMode.Create);
            using var w = new BinaryWriter(fs);
            int dataBytes = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + dataBytes);
            w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);   // PCM
            w.Write((short)1);   // mono
            w.Write(rate);
            w.Write(rate * 2);   // byte rate
            w.Write((short)2);   // block align
            w.Write((short)16);  // bits
            w.Write(new[] { 'd', 'a', 't', 'a' });
            w.Write(dataBytes);
            foreach (var s in samples)
                w.Write((short)(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        }

        private static void TryDelete(string p) { try { File.Delete(p); } catch { } }

        private static void WriteManifest(string dir, List<Spec> clips)
        {
            using var sw = new StreamWriter(Path.Combine(dir, "VOICE_MANIFEST.md"));
            sw.WriteLine("# Game Master announcer voice bank");
            sw.WriteLine();
            sw.WriteLine("Robotic PA voicelines rendered offline with **espeak-ng** (a build-time tool,");
            sw.WriteLine("not a runtime/Unity dependency). The game plays/queues these WAVs at runtime");
            sw.WriteLine("(see `Scripts/Audio/Announcer.cs`), mirroring the web build's browser-TTS Game");
            sw.WriteLine("Master. Speech-synth output is data, not a derivative of the synthesizer, so no");
            sw.WriteLine("license is inherited. Re-run: `dotnet run --project tools/VoiceGen -- <this-dir>`.");
            sw.WriteLine();
            sw.WriteLine("16-bit PCM mono WAV. **M** = male announcer (game reveals + Simon Says), **F** = female (eliminations).");
            sw.WriteLine();
            foreach (var c in clips)
                sw.WriteLine($"- `{c.Key}.wav` — [{(c.Voice == V.Male ? "M" : "F")}] “{c.Text}”");
        }
    }
}
