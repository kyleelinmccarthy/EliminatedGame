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
        private AudioSource[] _announce; // dedicated sources for the Game-Master announcer (see Speak)
        private AudioSource _music;
        private string _currentMusic; // the loop clip currently loaded (so SetMusic can no-op)
        private bool _ensuredListener;

        // Rotation state: non-null only while cycling several tracks (the editor lobby);
        // null means a single pinned looping track (every other screen).
        private string[] _playlist;
        private int _playlistIdx;
        private bool _rotating;         // true while cycling _playlist (kept across pins so we resume position)
        private float _trackElapsed;    // wall-clock seconds the current rotation track has played
        private float _notPlaying;      // consecutive seconds isPlaying read false (flicker/focus tolerance)
        // Which volume bucket the current loop belongs to. IN-GAME music (during a live
        // round) is "game sound" and rides sfxVolume alongside SFX + the announcer;
        // front-of-house (menu/lobby) music is "background music" on its own musicVolume.
        // The same track can serve both (e.g. music_sinister on the menu AND in regular
        // rounds), so this is set by the caller per context, not inferred from the clip.
        private bool _musicIsGame;

        // ---- Background music ----------------------------------------------------------
        // Usually one looping track plays at a time, chosen per screen/phase by the HUD music
        // director (HudUi.UpdateMusic): menu / lobby / regular rounds → "music_sinister";
        // Mingle & Musical Chairs → "music_danube"; final game → "music_creepy"; post-game
        // results → "music_accralate". SetMusic pins one looping track; SetMusicPlaylist cycles
        // several (used only for the EDITOR lobby — Pink Soldiers ↔ Sinister). Shipping tracks
        // are cleared for in-game use (Pixabay / incompetech CC-BY — see docs/ASSET_SOURCES.md).

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
                _voices[i].spatialBlend = 0f; // 2D — UI/SFX must not attenuate by position
            }

            // The announcer (Game-Master TTS clips) gets its own little pool, separate
            // from the round-robin SFX voices: a multi-clip line ("Game three." then
            // "Tug of war.") is scheduled gaplessly across these, and a new line stops
            // them so announcements never overlap or get chopped by a burst of SFX.
            _announce = new AudioSource[4];
            for (int i = 0; i < _announce.Length; i++)
            {
                var ago = new GameObject("Announce" + i);
                ago.transform.SetParent(transform, false);
                _announce[i] = ago.AddComponent<AudioSource>();
                _announce[i].playOnAwake = false;
                _announce[i].spatialBlend = 0f;
            }

            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(transform, false);
            _music = musicGo.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;

            ApplyVolumes();
            // No track is started here — the HUD music director (HudUi.UpdateMusic) selects the
            // loop for the current screen on its first frame and whenever the screen changes.
        }

        /// <summary>The track currently loaded on the music source (so SetMusic can no-op).</summary>
        public string CurrentMusic => _currentMusic;

        /// <summary>Pin one looping track. <paramref name="gameMusic"/> picks the volume
        /// bucket: true → in-game music (rides sfxVolume, the "game sound" channel);
        /// false → front-of-house background music (musicVolume). No-op on the track swap
        /// if already on it, but the bucket/level are always refreshed first (the same
        /// track can move between menu and gameplay). Ignores null/empty + missing clips.</summary>
        public void SetMusic(string clip, bool gameMusic = false)
        {
            if (_music == null || string.IsNullOrEmpty(clip)) return;
            // Refresh the bucket + level first: a reused track (e.g. music_sinister on the
            // menu, then in a regular round) must re-level even when the clip doesn't change.
            _musicIsGame = gameMusic;
            _music.volume = MusicVol();
            if (!_rotating && clip == _currentMusic) return; // already pinned to this loop
            _rotating = false;    // leave rotation mode (keep _playlist/_playlistIdx so we can resume it)
            _music.loop = true;   // a single track loops until SetMusic/SetMusicPlaylist changes it
            var loaded = Load(clip);
            if (loaded == null) return; // missing track → keep the current loop
            _currentMusic = clip;
            _music.clip = loaded;
            if (loaded.loadState != AudioDataLoadState.Loaded) loaded.LoadAudioData(); // ready before Play
            if (MusicEnabled) _music.Play();
        }

        /// <summary>Rotate the background music through <paramref name="tracks"/> in order,
        /// advancing when each finishes and wrapping. Used only for the editor lobby (Pink
        /// Soldiers ↔ Sinister). No-op if already rotating this exact set; SetMusic leaves rotation.</summary>
        public void SetMusicPlaylist(string[] tracks)
        {
            if (_music == null || tracks == null || tracks.Length == 0) return;
            if (_rotating && SamePlaylist(tracks)) return; // already cycling this set — let it keep going
            if (!SamePlaylist(tracks)) _playlistIdx = 0;   // a different set → start at the top
            _playlist = tracks;                            // (same set → keep _playlistIdx, resume where we left off)
            _rotating = true;
            _musicIsGame = false; // the rotation is the editor lobby — front-of-house background
            _music.volume = MusicVol();
            _trackElapsed = 0f;
            _notPlaying = 0f;
            _music.loop = false;  // advance through the set rather than loop one clip
            LoadTrack(_playlist[_playlistIdx]);
            if (MusicEnabled) _music.Play();
        }

        private bool SamePlaylist(string[] t)
        {
            if (_playlist == null || _playlist.Length != t.Length) return false;
            for (int i = 0; i < t.Length; i++) if (_playlist[i] != t[i]) return false;
            return true;
        }

        // Point the music source at a track (does not Play — the caller / Update does).
        private void LoadTrack(string name)
        {
            var clip = Load(name);
            if (clip == null) return; // missing file → keep whatever's loaded
            _currentMusic = name;
            _music.clip = clip;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        }

        private static bool MusicEnabled => SaveService.Current?.settings?.musicEnabled ?? true;

        // Level for the current loop, by bucket: in-game music tracks the "game sound"
        // (sfxVolume) channel with the SFX + announcer; background music has its own
        // musicVolume. Master is applied globally via AudioListener.volume on top.
        private float MusicVol()
        {
            var s = SaveService.Current?.settings;
            if (s == null) return _musicIsGame ? 1f : 0.7f;
            return Mathf.Clamp01(_musicIsGame ? s.sfxVolume : s.musicVolume);
        }

        private void Update()
        {
            // Nothing in a code-bootstrapped scene guarantees an AudioListener (the arena
            // camera doesn't add one), so without this audio can be silent until some
            // other camera happens to provide one. Ensure exactly one exists.
            if (!_ensuredListener)
            {
                _ensuredListener = true;
                if (FindFirstObjectByType<AudioListener>() == null)
                    gameObject.AddComponent<AudioListener>();
            }
            // Keep the current track alive (its audio data may not have decoded when it was set)
            // and honor the music toggle here — whichever AudioService owns the loop silences it,
            // so a stale Instance reference can never leave music audibly playing.
            if (_music == null || _music.clip == null) return;
            bool musicOn = MusicEnabled;
            if (!musicOn) { if (_music.isPlaying) _music.Stop(); _trackElapsed = 0f; _notPlaying = 0f; return; }

            if (_rotating)
            {
                // Drive the rotation by WALL-CLOCK time, and ONLY while the editor is focused.
                // Tabbing out of Unity suspends the AudioSource (isPlaying reads false even though
                // the track isn't over); reacting to that is what made the song swap (or restart)
                // on tab-in. Freezing while unfocused + advancing on elapsed time makes tab-out/in
                // seamless — the song only changes when it has genuinely played out.
                var clip = _music.clip;
                if (Application.isFocused)
                {
                    if (clip != null) _trackElapsed += Time.unscaledDeltaTime;
                    if (clip == null || _trackElapsed >= clip.length)
                    {
                        _playlistIdx = (_playlistIdx + 1) % _playlist.Length; // played out → next track
                        LoadTrack(_playlist[_playlistIdx]);
                        _trackElapsed = 0f;
                        _notPlaying = 0f;
                        _music.Play();
                    }
                    else if (_music.isPlaying) _notPlaying = 0f;
                    else
                    {
                        // Not finished but not playing: only nudge Play() after a sustained gap (boot
                        // decode / a real stall), never on a 1-frame blip — so nothing restarts mid-track.
                        _notPlaying += Time.unscaledDeltaTime;
                        if (_notPlaying >= 0.3f) { _music.Play(); _notPlaying = 0f; }
                    }
                }
            }
            else if (!_music.isPlaying)
            {
                _music.Play(); // pinned single loop → keep it alive
            }
        }

        // Several cues use real CC0 sounds sourced from OpenGameArt (rubberduck,
        // "100 CC0 SFX"; see Resources/Audio/oga/LICENSE.txt); the rest use our
        // generated WAVs. Both go through Play() so swapping is one line each.
        private static readonly Dictionary<string, string> Sourced = new Dictionary<string, string>
        {
            // Music is not routed here — the per-screen loops are chosen by the HUD via
            // SetMusic (sourced loops in Resources/Audio; see docs/ASSET_SOURCES.md), not SfxGen.
            { "shatter", "oga/glass_01" },
            { "chime", "oga/bell_02" },
            { "drum", "oga/slam_03" },
            { "catch", "oga/spring_07" },
        };

        private AudioClip Load(string name)
        {
            if (_clips.TryGetValue(name, out var c)) return c;
            string path = Sourced.TryGetValue(name, out var oga) ? oga : name;
            c = Resources.Load<AudioClip>("Audio/" + path);
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

        /// <summary>
        /// Speak a Game-Master announcer line: play one or more pre-rendered voice
        /// clips (names under <c>Resources/Audio</c>, e.g. "voice/game_03") back to
        /// back with no gap, on the dedicated announcer pool. A new call cancels the
        /// previous line outright (matching the web's <c>speechSynthesis.cancel()</c>),
        /// so reveals and eliminations never slur together. Missing clips are skipped.
        /// Honors the SFX volume; silenced by the master mute like every other cue.
        /// </summary>
        public void Speak(IReadOnlyList<string> clipNames, float volume = 1f)
        {
            if (_announce == null || clipNames == null || clipNames.Count == 0) return;

            foreach (var src in _announce) src.Stop(); // cancel any line in flight

            float vol = Mathf.Clamp01(volume * (SaveService.Current?.settings.sfxVolume ?? 1f));
            // Schedule on the DSP clock so consecutive clips are sample-accurate and
            // gapless. One source per clip (lines are short — 1-2 clips); if a line
            // ever exceeds the pool size it wraps and the tail isn't perfectly gapless.
            double at = AudioSettings.dspTime + 0.06; // small lead so the first clip starts cleanly
            for (int i = 0; i < clipNames.Count; i++)
            {
                var clip = Load(clipNames[i]);
                if (clip == null) continue;
                var src = _announce[i % _announce.Length];
                src.clip = clip;
                src.volume = vol;
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                src.PlayScheduled(at);
                at += clip.length;
            }
        }

        /// <summary>Stop any announcer line currently playing/queued (e.g. on mute).</summary>
        public void StopSpeak()
        {
            if (_announce == null) return;
            foreach (var src in _announce) src.Stop();
        }

        /// <summary>Re-read saved volumes (call after the settings change).</summary>
        public void ApplyVolumes()
        {
            var s = SaveService.Current?.settings;
            float master = s?.masterVolume ?? 1f;
            AudioListener.volume = Mathf.Clamp01(master);
            // Music can be silenced independently of game SFX (one-shot voices read
            // sfxVolume in Play()). Stop the loop outright when disabled so it actually
            // goes quiet now; Update() then keeps it stopped while the flag is off.
            if (_music == null) return;
            _music.volume = MusicVol(); // background → musicVolume; in-game music → sfxVolume
            bool musicOn = s?.musicEnabled ?? true;
            if (musicOn) { if (_music.clip != null && !_music.isPlaying) _music.Play(); }
            else if (_music.isPlaying) _music.Stop();
        }
    }
}
