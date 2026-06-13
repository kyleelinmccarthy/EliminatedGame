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
        private AudioSource _musicFade; // second loop source: the incoming track during a rotation crossfade
        private string _currentMusic; // the loop clip currently loaded (so SetMusic can no-op)
        private bool _ensuredListener;

        // Crossfade state (rotation only). When a DIFFERENT track is next in the playlist we blend
        // into it over CrossfadeDur instead of hard-cutting: _music fades out, _musicFade fades in,
        // then the two source references swap so _music is always the foreground loop.
        private const float CrossfadeDur = 3.0f; // seconds; tune for how gradual the lobby blend feels
        private bool _fading;
        private float _fadeT; // seconds elapsed into the current crossfade

        // Rotation state: non-null only while cycling several tracks (the editor lobby);
        // null means a single pinned looping track (every other screen).
        private string[] _playlist;
        private int _playlistIdx;
        private bool _rotating;         // true while cycling _playlist (kept across pins so we resume position)
        private float _trackElapsed;    // wall-clock seconds the current rotation track has played
        private float _notPlaying;      // consecutive seconds isPlaying read false (flicker/focus tolerance)
        // Which bucket the current loop belongs to. GAMEPLAY music — a track that is a
        // game MECHANIC (Mingle / Musical Chairs: you act when it stops) — is "game sound":
        // it rides sfxVolume alongside SFX + the announcer and IGNORES the music mute
        // (muting music must never hide a gameplay cue; only the sound/master mute can).
        // Everything else (menu, lobby, regular-round ambiance, final, results) is
        // "background music" on musicVolume, gated by the musicEnabled toggle. Set by
        // the caller per context, not inferred from the clip.
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

            _music = MakeMusicSource("Music", loop: true);
            // Second music source, used only to crossfade the editor-lobby rotation (Sinister
            // ↔ Pink Soldiers). It carries the incoming track while _music fades out.
            _musicFade = MakeMusicSource("MusicFade", loop: false);

            ApplyVolumes();
            // No track is started here — the HUD music director (HudUi.UpdateMusic) selects the
            // loop for the current screen on its first frame and whenever the screen changes.
        }

        /// <summary>The track currently loaded on the music source (so SetMusic can no-op).</summary>
        public string CurrentMusic => _currentMusic;

        // Create a 2D, non-autoplay music source parented under this service. Used by Init and as a
        // lazy fallback: an editor hot-reload mid-Play doesn't re-run Init, so a field added by the
        // recompile (e.g. _musicFade) can be null even though the older _music survived — callers
        // that need a source rebuild it through here rather than NRE.
        private AudioSource MakeMusicSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.loop = loop;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            return src;
        }

        /// <summary>Pin one looping track. <paramref name="gameMusic"/> picks the bucket:
        /// true → gameplay-mechanic music (rides sfxVolume, the "game sound" channel, and
        /// plays even when music is muted — it IS gameplay); false → background music
        /// (musicVolume, gated by the music toggle). No-op on the track swap if already
        /// on it, but the bucket/level are always refreshed first (the same track can
        /// move between buckets). Ignores null/empty + missing clips.</summary>
        public void SetMusic(string clip, bool gameMusic = false)
        {
            if (_music == null || string.IsNullOrEmpty(clip)) return;
            // Refresh the bucket + level first: a reused track (e.g. music_sinister on the
            // menu, then in a regular round) must re-level even when the clip doesn't change.
            _musicIsGame = gameMusic;
            _music.volume = MusicVol();
            if (!_rotating && clip == _currentMusic) return; // already pinned to this loop
            StopFade();           // leaving the rotation → drop any crossfade in progress
            _rotating = false;    // leave rotation mode (keep _playlist/_playlistIdx so we can resume it)
            _music.loop = true;   // a single track loops until SetMusic/SetMusicPlaylist changes it
            var loaded = Load(clip);
            if (loaded == null) return; // missing track → keep the current loop
            _currentMusic = clip;
            _music.clip = loaded;
            if (loaded.loadState != AudioDataLoadState.Loaded) loaded.LoadAudioData(); // ready before Play
            if (gameMusic || MusicEnabled) _music.Play();
        }

        /// <summary>Rotate the background music through <paramref name="tracks"/> in order,
        /// wrapping. A change to a DIFFERENT next track crossfades over CrossfadeDur (see
        /// BeginCrossfade/TickCrossfade); a repeated track (e.g. Pink Soldiers listed twice)
        /// hard-restarts at its seamless loop point. Used only for the editor lobby (Pink
        /// Soldiers ↔ Sinister). No-op if already rotating this exact set; SetMusic leaves rotation.</summary>
        public void SetMusicPlaylist(string[] tracks)
        {
            if (_music == null || tracks == null || tracks.Length == 0) return;
            if (_rotating && SamePlaylist(tracks)) return; // already cycling this set — let it keep going
            StopFade();                                    // (re)starting a set → no leftover crossfade
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

        // Start blending the rotation into <paramref name="name"/>: bring it up on the second
        // source (at volume 0) while Update fades _music out. Falls back to a hard load on a
        // missing clip so the rotation never stalls.
        private void BeginCrossfade(string name)
        {
            var clip = Load(name);
            if (clip == null) { _trackElapsed = 0f; return; } // missing → let the current track keep playing
            if (_musicFade == null) _musicFade = MakeMusicSource("MusicFade", loop: false); // hot-reload safety
            _musicFade.clip = clip;
            _musicFade.loop = false;
            _musicFade.volume = 0f;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            _currentMusic = name; // the incoming track is the one now "current"
            _musicFade.Play();
            _fading = true;
            _fadeT = 0f;
        }

        // Advance the in-progress crossfade. When it completes, the incoming source becomes the
        // foreground loop (sources swap) and the outgoing one is stopped.
        private void TickCrossfade()
        {
            _fadeT += Time.unscaledDeltaTime;
            float vol = MusicVol();
            float k = Mathf.Clamp01(_fadeT / CrossfadeDur);
            // Equal-power (sin/cos) blend so the combined loudness stays steady through the middle
            // of the fade rather than dipping the way a straight linear cross would.
            _music.volume = vol * Mathf.Cos(k * (0.5f * Mathf.PI));
            _musicFade.volume = vol * Mathf.Sin(k * (0.5f * Mathf.PI));
            if (k < 1f) return;

            _music.Stop();
            var spent = _music; _music = _musicFade; _musicFade = spent; // incoming becomes foreground
            _music.volume = vol;
            _fading = false;
            _fadeT = 0f;
            // The incoming track already played CrossfadeDur during the blend — keep elapsed aligned
            // to its playhead so its own end-of-loop crossfade fires while audio is still present.
            _trackElapsed = CrossfadeDur;
            _notPlaying = 0f;
        }

        // Abandon any crossfade in progress and silence the secondary source (leaving the rotation,
        // music toggled off, etc.). Safe to call when no fade is running.
        private void StopFade()
        {
            _fading = false;
            _fadeT = 0f;
            if (_musicFade != null && _musicFade.isPlaying) _musicFade.Stop();
        }

        private static bool MusicEnabled => SaveService.Current?.settings?.musicEnabled ?? true;

        // Should the loaded loop be audible right now? Background music obeys the music
        // toggle; gameplay-mechanic music (Mingle / Musical Chairs) always plays — the
        // track is a game cue, silenced only by the sound/master mute, never by 🎵.
        private bool LoopOn => _musicIsGame || MusicEnabled;

        // Level for the current loop, by bucket: gameplay music tracks the "game sound"
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
            if (!LoopOn) { if (_music.isPlaying) _music.Stop(); StopFade(); _trackElapsed = 0f; _notPlaying = 0f; return; }

            if (_rotating)
            {
                // Drive the rotation by WALL-CLOCK time, and ONLY while the editor is focused.
                // Tabbing out of Unity suspends the AudioSource (isPlaying reads false even though
                // the track isn't over); reacting to that is what made the song swap (or restart)
                // on tab-in. Freezing while unfocused + advancing on elapsed time makes tab-out/in
                // seamless — the song only changes when it has genuinely played out.
                if (Application.isFocused)
                {
                    if (_fading)
                    {
                        TickCrossfade(); // mid-blend: drive volumes, swap sources when it completes
                    }
                    else
                    {
                        var clip = _music.clip;
                        if (clip != null) _trackElapsed += Time.unscaledDeltaTime;
                        string next = _playlist[(_playlistIdx + 1) % _playlist.Length];
                        if (clip == null || _trackElapsed >= clip.length)
                        {
                            // Reached the end and the next entry is the SAME track (Pink Soldiers is
                            // listed twice for airtime): hard, seamless restart — crossfading a track
                            // with itself only phases. Different tracks take the crossfade branch
                            // below, which fires before we ever reach the clip's end.
                            _playlistIdx = (_playlistIdx + 1) % _playlist.Length;
                            LoadTrack(_playlist[_playlistIdx]);
                            _trackElapsed = 0f;
                            _notPlaying = 0f;
                            _music.Play();
                        }
                        else if (next != _currentMusic && _trackElapsed >= clip.length - CrossfadeDur)
                        {
                            // Nearing the end with a DIFFERENT track next → blend into it over
                            // CrossfadeDur instead of hard-cutting (the abrupt swap we're fixing).
                            _playlistIdx = (_playlistIdx + 1) % _playlist.Length;
                            BeginCrossfade(_playlist[_playlistIdx]);
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
        private AudioClip _spokenLine; // last stitched announcer line; freed when the next is built

        // Returns the spoken line's length in seconds (0 if nothing played), so callers can
        // hold off the next line until this one finishes instead of cutting it off.
        public float Speak(IReadOnlyList<string> clipNames, float volume = 1f)
        {
            if (_announce == null || clipNames == null || clipNames.Count == 0) return 0f;

            foreach (var src in _announce) src.Stop(); // cancel any line in flight

            // A numbered elimination call ("Player 387 has been eliminated", a numbered
            // wipe) is many clips, and female calls ride a touch louder than unity — so we
            // stitch the whole line into ONE clip and play it on a single source. The old
            // per-source scheduling silently drops every clip past the 4-source pool, which
            // would lop the first words off any line longer than four. A "@<ms>" token
            // inserts a silent beat (the pause before "…eliminated").
            float vol = Mathf.Clamp(volume * (SaveService.Current?.settings.sfxVolume ?? 1f), 0f, 1.6f);
            var line = StitchLine(clipNames);
            if (line != null)
            {
                if (_spokenLine != null) Destroy(_spokenLine);
                _spokenLine = line;
                var src = _announce[0];
                src.clip = line;
                src.volume = vol;
                src.PlayScheduled(AudioSettings.dspTime + 0.06);
                return line.length;
            }

            // Fallback (clip PCM unreadable): schedule clips on the pool. Non-wrapping index
            // so early clips aren't overwritten — a line longer than the pool loses its tail,
            // not its head. "@<ms>" beats still advance the schedule.
            double start = AudioSettings.dspTime + 0.06;
            double at = start;
            int s = 0;
            for (int i = 0; i < clipNames.Count; i++)
            {
                var name = clipNames[i];
                if (!string.IsNullOrEmpty(name) && name[0] == '@')
                { if (float.TryParse(name.Substring(1), out var ms)) at += ms / 1000.0; continue; }
                if (s >= _announce.Length) break;
                var clip = Load(name);
                if (clip == null) continue;
                var src = _announce[s++];
                src.clip = clip;
                src.volume = vol;
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                src.PlayScheduled(at);
                at += clip.length;
            }
            return (float)(at - start);
        }

        // Concatenate the named clips — plus "@<ms>" silent beats — into one mono AudioClip
        // at the bank's sample rate. Returns null if nothing could be read (caller falls back
        // to per-source scheduling). The clips import DecompressOnLoad, so GetData yields PCM.
        private AudioClip StitchLine(IReadOnlyList<string> clipNames)
        {
            int freq = 0, channels = 1, total = 0;
            var segs = new List<float[]>(clipNames.Count);
            for (int i = 0; i < clipNames.Count; i++)
            {
                var name = clipNames[i];
                if (string.IsNullOrEmpty(name)) continue;
                if (name[0] == '@') // silent beat, e.g. "@350" → 350 ms
                {
                    if (freq > 0 && int.TryParse(name.Substring(1), out int ms) && ms > 0)
                    {
                        int n = (int)((long)ms * freq / 1000) * channels;
                        if (n > 0) { segs.Add(new float[n]); total += n; }
                    }
                    continue;
                }
                var clip = Load(name);
                if (clip == null) continue;
                if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                if (freq == 0) { freq = clip.frequency; channels = Mathf.Max(1, clip.channels); }
                else if (clip.frequency != freq || clip.channels != channels) continue; // uniform bank; skip oddballs
                var buf = new float[clip.samples * clip.channels];
                if (!clip.GetData(buf, 0)) continue;
                segs.Add(buf);
                total += buf.Length;
            }
            if (total <= 0 || freq <= 0) return null;
            var data = new float[total];
            int o = 0;
            foreach (var seg in segs) { System.Array.Copy(seg, 0, data, o, seg.Length); o += seg.Length; }
            var outClip = AudioClip.Create("gm_line", total / channels, channels, freq, false);
            outClip.SetData(data, 0);
            return outClip;
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
            // Background music can be silenced independently of game SFX (one-shot voices
            // read sfxVolume in Play()). Stop the loop outright when disabled so it actually
            // goes quiet now; Update() then keeps it stopped while the flag is off. Gameplay
            // music (LoopOn) is exempt — it's a game cue, only the master mute silences it.
            if (_music == null) return;
            // During a rotation crossfade TickCrossfade owns both sources' volumes — don't slam the
            // outgoing one back to full here; it self-corrects next frame either way.
            if (!_fading) _music.volume = MusicVol(); // background → musicVolume; gameplay music → sfxVolume
            if (LoopOn) { if (_music.clip != null && !_music.isPlaying) _music.Play(); }
            else { if (_music.isPlaying) _music.Stop(); StopFade(); }
        }
    }
}
