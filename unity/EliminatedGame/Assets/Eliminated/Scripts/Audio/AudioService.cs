using System.Collections.Generic;
using UnityEngine;
using Eliminated.Game.Save;

namespace Eliminated.Game.Audio
{
    /// <summary>
    /// Plays the generated SFX (loaded from <c>Resources/Audio</c>) and the music
    /// loop through a small voice pool, honoring the saved master/SFX/music
    /// volumes. The clips are real WAVs produced by <c>tools/SfxGen</c> (our own
    /// procedural output — see Resources/Audio/SFX_MANIFEST.md). Phase 7 can swap
    /// in curated CC0 sets without changing call sites (everything goes through
    /// <see cref="Play"/>). A singleton so any system can fire a cue.
    /// </summary>
    public sealed class AudioService : MonoBehaviour
    {
        public static AudioService Instance { get; private set; }

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private AudioSource[] _voices;
        private int _voice;
        private AudioSource _music;

        public void Init()
        {
            Instance = this;

            _voices = new AudioSource[8];
            for (int i = 0; i < _voices.Length; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                _voices[i] = go.AddComponent<AudioSource>();
                _voices[i].playOnAwake = false;
            }

            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(transform, false);
            _music = musicGo.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;

            ApplyVolumes();
            var loop = Load("music");
            if (loop != null) { _music.clip = loop; _music.Play(); }
        }

        private AudioClip Load(string name)
        {
            if (_clips.TryGetValue(name, out var c)) return c;
            c = Resources.Load<AudioClip>("Audio/" + name);
            _clips[name] = c; // cache even nulls to avoid repeat lookups
            return c;
        }

        /// <summary>Fire a one-shot SFX by name (e.g. "death", "pickup", "win").</summary>
        public void Play(string name, float volume = 1f)
        {
            var clip = Load(name);
            if (clip == null) return;
            var src = _voices[_voice];
            _voice = (_voice + 1) % _voices.Length;
            float sfx = SaveService.Current?.settings.sfxVolume ?? 1f;
            src.PlayOneShot(clip, Mathf.Clamp01(volume * sfx));
        }

        /// <summary>Re-read saved volumes (call after the settings change).</summary>
        public void ApplyVolumes()
        {
            var s = SaveService.Current?.settings;
            float master = s?.masterVolume ?? 1f;
            AudioListener.volume = Mathf.Clamp01(master);
            if (_music != null) _music.volume = Mathf.Clamp01((s?.musicVolume ?? 0.7f));
        }
    }
}
