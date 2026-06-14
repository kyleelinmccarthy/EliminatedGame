#!/usr/bin/env python3
"""
Render the Game Master announcer voice bank with Kokoro (neural TTS, Apache-2.0).

The bank's vocabulary + voice assignments are owned by tools/VoiceGen (C#); this
script does NOT duplicate them. It reads them from `VoiceGen --dump-specs`
(key<TAB>text<TAB>M|F|W) so there is one source of truth.

Voice assignment for the shipped bank:
  * F (eliminations) and M (game reveals / Simon Says) -> the FEMALE PA announcer
  * W (the lone Front Man winner line)                 -> the MALE voice
i.e. the woman runs the whole PA; the man only announces the winner.

Defaults: announcer = af_kore, winner = am_adam (override with env
KOKORO_ANNOUNCER / KOKORO_WINNER). Model + voices default to ~/voicegen-assets
(override with KOKORO_MODEL / KOKORO_VOICES).

Usage:
  dotnet run --project tools/VoiceGen -- --dump-specs > specs.tsv
  python3 tools/VoiceGen/kokoro_bank.py specs.tsv <out-voice-dir>

Post-processing (trim -> peak-normalize -> edge-fade) mirrors VoiceGen so clips
stitch gaplessly and sit at an even level.
"""
import os, sys, wave, numpy as np
from kokoro_onnx import Kokoro

HOME = os.path.expanduser("~")
VG = os.environ.get("VG_ASSETS", f"{HOME}/voicegen-assets")
MODEL = os.environ.get("KOKORO_MODEL", f"{VG}/kokoro-v1.0.onnx")
VOICES = os.environ.get("KOKORO_VOICES", f"{VG}/voices-v1.0.bin")
ANNOUNCER = os.environ.get("KOKORO_ANNOUNCER", "af_kore")   # female PA: reveals + Simon + eliminations
WINNER = os.environ.get("KOKORO_WINNER", "am_adam")         # male Front Man: winner line only


def trim_silence(s, thresh=0.02, lead=0.035, tail=0.06, sr=24000):
    a = np.abs(s)
    idx = np.where(a >= thresh)[0]
    if idx.size == 0:
        return s
    start = max(0, idx[0] - int(lead * sr))
    end = min(len(s), idx[-1] + int(tail * sr) + 1)
    return s[start:end]


def normalize(s, peak=0.92):
    m = float(np.max(np.abs(s))) if s.size else 0.0
    return s * (peak / m) if m > 1e-6 else s


def fade_edges(s, fade=0.006, sr=24000):
    n = min(int(fade * sr), len(s) // 2)
    if n <= 0:
        return s
    ramp = np.linspace(0.0, 1.0, n, dtype=np.float32)
    s[:n] *= ramp
    s[-n:] *= ramp[::-1]
    return s


def write_wav(path, s, sr):
    pcm = (np.clip(s, -1.0, 1.0) * 32767.0).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(int(sr))
        w.writeframes(pcm.tobytes())


def main():
    if len(sys.argv) < 3:
        print(__doc__); sys.exit(2)
    specs_path, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)

    specs = []
    with open(specs_path, encoding="utf-8") as f:
        for ln in f:
            ln = ln.rstrip("\n")
            if not ln:
                continue
            key, text, voicetag = ln.split("\t")
            specs.append((key, text, voicetag))

    k = Kokoro(MODEL, VOICES)
    print(f"Kokoro bank: announcer={ANNOUNCER}  winner={WINNER}  ({len(specs)} clips) -> {out_dir}")

    ok = 0
    for i, (key, text, voicetag) in enumerate(specs):
        voice = WINNER if voicetag == "W" else ANNOUNCER
        samples, sr = k.create(text, voice=voice, speed=1.0, lang="en-us")
        s = np.asarray(samples, dtype=np.float32)
        s = trim_silence(s, sr=sr)
        s = normalize(s)
        s = fade_edges(s, sr=sr)
        write_wav(os.path.join(out_dir, key + ".wav"), s, sr)
        ok += 1
        if i % 50 == 0 or voicetag == "W":
            print(f"  [{i+1}/{len(specs)}] {key}  ({voice})  \"{text}\"")

    # manifest
    with open(os.path.join(out_dir, "VOICE_MANIFEST.md"), "w", encoding="utf-8") as m:
        m.write("# Game Master announcer voice bank\n\n")
        m.write("Rendered offline with **Kokoro** (neural TTS, Apache-2.0 — a build-time tool, not a\n")
        m.write("runtime/Unity dependency). The game plays/queues these WAVs at runtime (Announcer.cs).\n\n")
        m.write(f"- PA announcer (reveals, Simon Says, eliminations): `{ANNOUNCER}`\n")
        m.write(f"- Front Man winner line only: `{WINNER}`\n")
        m.write("- All Kokoro `af_*`/`am_*` voices are Apache-2.0 — safe to ship. Record in docs/ASSET_SOURCES.md.\n")
        m.write("- Regenerate: `dotnet run --project tools/VoiceGen -- --dump-specs > specs.tsv && \\\n")
        m.write("  python3 tools/VoiceGen/kokoro_bank.py specs.tsv <dir>`\n\n")
        m.write("16-bit PCM mono WAV. **F**/**M** = female PA announcer, **W** = male Front Man (winner).\n\n")
        for key, text, voicetag in specs:
            m.write(f"- `{key}.wav` — [{voicetag}] “{text}”\n")

    print(f"DONE  {ok}/{len(specs)} clips")


if __name__ == "__main__":
    main()
