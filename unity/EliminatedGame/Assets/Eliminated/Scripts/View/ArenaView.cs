using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Eliminated.Sim.Powerups;
using Eliminated.Game.Accessibility;
using Eliminated.Game.Audio;
using Eliminated.Game.Net;
using Eliminated.Game.SimBridge;

namespace Eliminated.Game.View
{
    /// <summary>
    /// Builds the 2.5D top-down stage (camera, floor, light) and renders each
    /// snapshot: a pooled <see cref="PlayerView"/> per actor, plus lightweight
    /// game-specific props (boomerang rangs and pickups). All created in code so
    /// the slice runs with no hand-authored scene. Per-game view modules and real
    /// arenas arrive in Phase 3.
    /// </summary>
    public sealed class ArenaView : MonoBehaviour
    {
        private ISnapshotSource _sim;
        private int _themeIndex = -1; // ≥0 forces a specific floor theme (preview stations); -1 = random (live arena)
        private Camera _camera;
        private Vector3 _baseCamPos;   // rest position; ScreenFx shake offsets around this
        private readonly Dictionary<string, PlayerView> _players = new Dictionary<string, PlayerView>();
        private readonly List<GameObject> _props = new List<GameObject>(); // pooled cylinders (Disc)
        private int _propsUsed;
        private readonly List<GameObject> _boxes = new List<GameObject>(); // pooled cubes (Box)
        private int _boxesUsed;
        private readonly List<GameObject> _balls = new List<GameObject>(); // pooled spheres (Ball)
        private int _ballsUsed;

        // Powerup pickups are an identical MYSTERY on the ground (à la Boomerang Fu):
        // a hovering, hue-cycling "?" orb — the same for every kind. The good/bad/chaos
        // tell only arrives on PICKUP, as a reveal (icon + name, green/red/purple) that
        // floats over your blob — the "find out what you grabbed" payoff.
        private readonly List<Vector3> _pickupWorld = new List<Vector3>(); // orb "?" anchors this frame
        private struct Reveal { public string ActorId; public Vector3 World; public float Born; public string Text; public Color Color; }
        private readonly List<Reveal> _reveals = new List<Reveal>();
        private GUIStyle _orbStyle, _revealStyle, _bannerStyle;
        private const float RevealLife = 1.5f;
        // A bold, unmissable banner when YOU grab a powerup — spells out what it does.
        private string _myPickupText;
        private Color _myPickupColor = Color.white;
        private float _myPickupBorn = -10f;
        private const float MyPickupLife = 2.0f;

        // Arena theme. The live stage persists for the whole series, so the floor +
        // walls are re-themed each round (see MaybeAdvanceArena) rather than fixed at build.
        private Renderer _floorRenderer;            // live floor quad; re-skinned per round
        private Material _floorMat;                 // owned floor material (destroyed on re-theme)
        private readonly List<GameObject> _walls = new List<GameObject>(); // perimeter rim, rebuilt per theme
        private int _lastThemeRound = -1;           // round the live arena was last themed for

        // Caller doll: a real imported model (Resources/Models/doll) used by Red-Light
        // and Simon Says; falls back to the procedural doll until that asset is dropped in.
        private GameObject _doll;
        private bool _dollLoaded;
        private bool _dollUsed;
        private float _lastShoveSfx = -1f; // throttle: don't stack a pile of drums on a crowd shove
        private Renderer[] _dollRenderers;
        private MaterialPropertyBlock _dollMpb;

        public Camera Camera => _camera;

        // Camera framing knobs, shared by the live stage and offscreen preview baking.
        private const float CameraPitch = 52f; // degrees below horizontal
        private const float CameraDist = 45f;  // ortho, so this only affects clipping/framing origin

        public void Init(ISnapshotSource sim, int themeIndex = -1)
        {
            _sim = sim;
            _themeIndex = themeIndex;
            BuildStage();
        }

        /// <summary>Recompute orthographic size to frame the arena for a given aspect.</summary>
        private void ApplyFraming(float aspect)
        {
            // Fit the arena width, plus the foreshortened depth and headroom for upright characters.
            float needV = LogicalSpace.WorldHalfHeight * Mathf.Sin(CameraPitch * Mathf.Deg2Rad) + 2.2f;
            float needH = LogicalSpace.WorldHalfWidth / Mathf.Max(0.1f, aspect);
            _camera.orthographicSize = Mathf.Max(needV, needH) * 1.06f;
        }

        /// <summary>Re-frame the camera for an offscreen target of the given aspect (preview baking).</summary>
        public void Reframe(float aspect) => ApplyFraming(aspect);

        private void BuildStage()
        {
            // Camera: orthographic, tilted off straight-down so 2D character
            // sprites stand upright and read front-on (the way they do in the web
            // game). CameraPitch is the one knob: 90 = straight top-down (old look),
            // lower = more of a 3/4 view. No yaw, so sprites face the camera with no
            // per-frame billboarding (see PlayerView.RenderArt).
            var camGo = new GameObject("ArenaCamera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            // The 2D character rigs (animals + humanoids) layer their parts by Z-depth.
            // A tilted camera otherwise sorts transparent sprites by DISTANCE, which mixes
            // in height and reverses the layering — drawing the head/body over the face, so
            // every multi-part character looked "faceless". Sort transparent sprites by their
            // Z position instead, replicating the straight-on 2D layering the art was authored
            // for. (If faces are still hidden, flip the axis to (0,0,-1).)
            _camera.transparencySortMode = TransparencySortMode.CustomAxis;
            _camera.transparencySortAxis = new Vector3(0f, 0f, 1f);
            _camera.transform.rotation = Quaternion.Euler(CameraPitch, 0f, 0f);
            _camera.transform.position = -_camera.transform.forward * CameraDist; // look at arena center (origin)
            _baseCamPos = _camera.transform.position; // shake offsets are applied relative to this
            // Frame the arena for the current aspect ratio.
            float aspect = _camera.aspect > 0.01f ? _camera.aspect : (float)Screen.width / Mathf.Max(1, Screen.height);
            ApplyFraming(aspect);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.10f, 0.11f, 0.16f);
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = CameraDist * 2f + 50f;

            // Floor geometry only; the themed material + walls are bound by SetTheme
            // below (and re-bound each round for the live arena — see MaybeAdvanceArena).
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(floor.GetComponent<Collider>());
            floor.name = "Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(
                Constants.ArenaW * LogicalSpace.Scale, Constants.ArenaH * LogicalSpace.Scale, 1f);
            _floorRenderer = floor.GetComponent<Renderer>();

            BuildVignette();

            // Light. A soft flat ambient guarantees the unlit side of each player is
            // still legible under the Built-in pipeline (which has no URP lighting).
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.45f, 0.55f);

            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            light.intensity = 1.25f;

            // Skin the floor + walls. Preview stations keep their fixed theme; the live
            // arena starts on its current round's theme and re-themes as rounds advance.
            int round0 = _sim?.RoundIndex ?? 0;
            string theme = _themeIndex >= 0
                ? ArenaThemes.All[_themeIndex % ArenaThemes.All.Length]
                : ArenaThemes.ForRound(round0, _sim?.CurrentGame);
            SetTheme(theme);
            _lastThemeRound = round0;
        }

        /// <summary>(Re)skin the arena to a theme: bind the floor's themed material/texture and
        /// rebuild the perimeter rim so the floor and walls read as one place. Called once at
        /// build, then again whenever the live round advances (see <see cref="MaybeAdvanceArena"/>).</summary>
        private void SetTheme(string theme)
        {
            var floorTex = LoadFloorTexture(theme);
            Color floorTint = FloorTint(theme);
            var tiling = new Vector2(4f, 2.4f); // larger cells (was 6x4 → a tiny, moiré-prone grid)
            Material floorMat;
            if (_themeIndex >= 0)
            {
                // PREVIEW: a flat, UNLIT themed colour. The 16 offscreen stations are built
                // one-per-frame and bind the floor once; the first-built ones race the
                // regenerated .tga's import and bind a not-yet-ready texture, which the lit
                // Standard shader then washes to pure white. Unlit can't be lit to white, and
                // a solid colour can't lose a texture race — so every station is consistent.
                var unlit = Shader.Find("Unlit/Color") ?? ViewMaterials.Shared.shader;
                floorMat = new Material(unlit) { name = "EliminatedFloor" };
                if (floorMat.HasProperty("_Color")) floorMat.SetColor("_Color", floorTint);
            }
            else
            {
                // LIVE arena (a single view, no build race): the real lit, textured floor.
                floorMat = new Material(ViewMaterials.Shared.shader) { name = "EliminatedFloor" };
                if (floorTex != null)
                {
                    if (floorMat.HasProperty("_Color")) floorMat.SetColor("_Color", Color.white);
                    if (floorMat.HasProperty("_BaseColor")) floorMat.SetColor("_BaseColor", Color.white);
                    if (floorMat.HasProperty("_MainTex")) { floorMat.SetTexture("_MainTex", floorTex); floorMat.SetTextureScale("_MainTex", tiling); }
                    if (floorMat.HasProperty("_BaseMap")) { floorMat.SetTexture("_BaseMap", floorTex); floorMat.SetTextureScale("_BaseMap", tiling); }
                }
                else
                {
                    if (floorMat.HasProperty("_Color")) floorMat.SetColor("_Color", floorTint);
                    if (floorMat.HasProperty("_BaseColor")) floorMat.SetColor("_BaseColor", floorTint);
                }
                if (floorMat.HasProperty("_Glossiness")) floorMat.SetFloat("_Glossiness", 0.05f);
                if (floorMat.HasProperty("_Metallic")) floorMat.SetFloat("_Metallic", 0f);
            }
            // The floor must NEVER occlude the players/balloons/props standing on it. Disabling
            // its depth-write means the camera-facing sprites + floating props always draw on
            // top instead of randomly sinking behind the floor texture.
            if (floorMat.HasProperty("_ZWrite")) floorMat.SetFloat("_ZWrite", 0f);
            if (_floorMat != null) Destroy(_floorMat); // release the previous round's material
            _floorMat = floorMat;
            _floorRenderer.sharedMaterial = floorMat;

            // Rebuild the perimeter rim in the new theme's colours.
            for (int i = 0; i < _walls.Count; i++) if (_walls[i] != null) Destroy(_walls[i]);
            _walls.Clear();
            BuildWalls(theme);
        }

        /// <summary>A soft radial darkening overlay (clear centre → shaded edges), generated in
        /// code, that gives the flat tiled floor real depth — the polish the web arena gets from
        /// its vignette. Sits just above the floor and BEHIND the characters/decals (queue 2520),
        /// so it shades only the ground, never the players.</summary>
        private void BuildVignette()
        {
            const int N = 96;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ArenaVignette" };
            var px = new Color32[N * N];
            for (int yy = 0; yy < N; yy++)
                for (int xx = 0; xx < N; xx++)
                {
                    float dx = (xx + 0.5f) / N * 2f - 1f, dy = (yy + 0.5f) / N * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.41421356f;      // 0 centre → 1 corner
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.5f) / 0.5f)) * 0.45f; // clear middle, shaded rim
                    px[yy * N + xx] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply();

            var v = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(v.GetComponent<Collider>());
            v.name = "FloorVignette";
            v.transform.SetParent(transform, false);
            v.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            v.transform.position = new Vector3(0f, 0.03f, 0f);
            v.transform.localScale = new Vector3(
                Constants.ArenaW * LogicalSpace.Scale * 1.02f, Constants.ArenaH * LogicalSpace.Scale * 1.02f, 1f);
            var mat = new Material(Shader.Find("Unlit/Transparent") ?? ViewMaterials.Props.shader) { name = "VignetteMat" };
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            mat.renderQueue = 2520; // above the floor + floor-fills (2500), below decals (2600) and players (3000)
            v.GetComponent<Renderer>().sharedMaterial = mat;
        }

        /// <summary>A raised rim around the arena perimeter so the stage reads as an
        /// enclosed map with depth rather than a flat floating quad. The low walls
        /// catch the directional light for a subtle 2.5D edge.</summary>
        private void BuildWalls(string theme)
        {
            float halfW = LogicalSpace.WorldHalfWidth;
            float halfH = LogicalSpace.WorldHalfHeight;
            const float t = 0.45f; // thickness
            const float h = 0.9f;  // height
            var (wallColor, trimColor) = WallPalette(theme);

            // Wall bodies.
            AddWall(new Vector3(0f, h * 0.5f,  halfH + t * 0.5f), new Vector3(halfW * 2f + t * 2f, h, t), wallColor);
            AddWall(new Vector3(0f, h * 0.5f, -halfH - t * 0.5f), new Vector3(halfW * 2f + t * 2f, h, t), wallColor);
            AddWall(new Vector3( halfW + t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, halfH * 2f), wallColor);
            AddWall(new Vector3(-halfW - t * 0.5f, h * 0.5f, 0f), new Vector3(t, h, halfH * 2f), wallColor);

            // A bright accent coping along the top inner edge, in the theme's accent
            // color — ties the rim to the floor (neon line, teal grout, candy icing…).
            const float ct = 0.18f; // cap depth
            const float ch = 0.16f; // cap height
            float capY = h + ch * 0.5f;
            AddWall(new Vector3(0f, capY,  halfH), new Vector3(halfW * 2f + t * 2f, ch, ct), trimColor);
            AddWall(new Vector3(0f, capY, -halfH), new Vector3(halfW * 2f + t * 2f, ch, ct), trimColor);
            AddWall(new Vector3( halfW, capY, 0f), new Vector3(ct, ch, halfH * 2f), trimColor);
            AddWall(new Vector3(-halfW, capY, 0f), new Vector3(ct, ch, halfH * 2f), trimColor);
        }

        /// <summary>(wall body, accent coping) colors per arena theme, matched to the
        /// generated floor textures (see tools/ArtGen/gen_floors.py).</summary>
        private static (Color wall, Color trim) WallPalette(string theme)
        {
            switch (theme)
            {
                case "neon":  return (new Color(0.05f, 0.10f, 0.09f), new Color(1.00f, 0.18f, 0.53f)); // dark / hot-pink
                case "candy": return (new Color(0.83f, 0.38f, 0.60f), new Color(1.00f, 0.92f, 0.96f)); // pink / icing white
                case "toxic": return (new Color(0.07f, 0.20f, 0.13f), new Color(0.30f, 0.85f, 0.63f)); // dark / radioactive green
                case "beach": return (new Color(0.42f, 0.31f, 0.19f), new Color(0.85f, 0.74f, 0.52f)); // driftwood / pale sand
                case "haunt": return (new Color(0.16f, 0.13f, 0.25f), new Color(0.49f, 0.34f, 0.76f)); // dark purple / violet
                default:      return (new Color(0.11f, 0.24f, 0.19f), new Color(0.10f, 0.83f, 0.74f)); // courtyard: teal stone / teal
            }
        }

        /// <summary>Dark per-theme floor base color — the fallback albedo so the arena
        /// stays dark and themed even if the floor texture fails to load.</summary>
        // Solid per-theme floor colour (also the multiply tint when a texture binds). Kept
        // moderate so it reads as a clear themed floor yet can't be lit to white.
        private static Color FloorTint(string theme)
        {
            switch (theme)
            {
                case "neon":  return new Color(0.22f, 0.13f, 0.28f); // dark with a magenta hint
                case "candy": return new Color(0.66f, 0.34f, 0.50f); // pink
                case "toxic": return new Color(0.16f, 0.44f, 0.28f); // radioactive green
                case "beach": return new Color(0.52f, 0.44f, 0.28f); // sand
                case "haunt": return new Color(0.28f, 0.20f, 0.42f); // violet
                default:      return new Color(0.19f, 0.48f, 0.40f); // courtyard teal
            }
        }

        private void AddWall(Vector3 localPos, Vector3 size, Color color)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(w.GetComponent<Collider>());
            w.name = "Wall";
            w.transform.SetParent(transform, false);
            w.transform.localPosition = localPos;
            w.transform.localScale = size;
            var r = w.GetComponent<Renderer>();
            r.sharedMaterial = ViewMaterials.Shared;
            ViewMaterials.SetColor(r, new MaterialPropertyBlock(), color);
            _walls.Add(w); // tracked so SetTheme can rebuild the rim in a new theme
        }

        private void LateUpdate()
        {
            MaybeAdvanceArena();
            RenderSnapshot(_sim != null ? _sim.Latest : null, Time.deltaTime, true);
            ApplyShake();
        }

        /// <summary>The live stage persists for the whole series, so re-skin it whenever the
        /// round advances — each round should read as a different place. <see cref="ArenaThemes.ForRound"/>
        /// is collision-free for consecutive rounds, so this always changes the room visibly. The
        /// HUD intro announces the same room from the same inputs. Preview stations are untouched.</summary>
        private void MaybeAdvanceArena()
        {
            if (_themeIndex >= 0 || _sim == null) return; // preview stations keep their fixed theme
            int round = _sim.RoundIndex;
            if (round == _lastThemeRound) return;
            _lastThemeRound = round;
            SetTheme(ArenaThemes.ForRound(round, _sim.CurrentGame));
        }

        // Background music is no longer driven here — HudUi.UpdateMusic is the single music
        // director (it knows the screen/phase + final-round, which the arena snapshot doesn't).

        // Offset the live camera by the current ScreenFx shake (no-op while idle or
        // when "Reduce flashing & screen shake" is on, since the offset stays zero).
        // Guarded to the enabled live camera so offscreen preview rigs (disabled
        // cameras, manual Render) are never nudged. Scaled by zoom so the kick reads
        // the same regardless of arena framing / resolution.
        private void ApplyShake()
        {
            if (_camera == null || !_camera.enabled) return;
            var o = ScreenFx.ShakeOffset();
            if (o == Vector2.zero) { _camera.transform.position = _baseCamPos; return; }
            float mag = _camera.orthographicSize * 0.04f;
            _camera.transform.position = _baseCamPos
                + _camera.transform.right * (o.x * mag)
                + _camera.transform.up * (o.y * mag);
        }

        /// <summary>Render one snapshot now. Used by the live loop (via <see cref="LateUpdate"/>)
        /// and by offscreen preview baking, which supplies a fixed snapshot, a large dt to
        /// settle the spawn-pop, and <paramref name="playAudio"/> = false.</summary>
        public void RenderSnapshot(Snapshot snap, float dt, bool playAudio)
        {
            if (snap?.Actors != null)
            {
                var layout = BuildLayout(snap, dt);
                var seen = new HashSet<string>();
                foreach (var a in snap.Actors)
                {
                    Vector2? ov = null; float? fo = null; bool hidden = false, fall = false, lift = false;
                    if (layout != null)
                    {
                        if (layout.Pos.TryGetValue(a.Id, out var p))
                        {
                            ov = p;
                            if (layout.Facing.TryGetValue(a.Id, out var f)) fo = f;
                            lift = layout.Airborne.Contains(a.Id);
                        }
                        else if (layout.FallDeath.Contains(a.Id)) { fall = true; }
                        else if (layout.Hide.Contains(a.Id) || layout.HideUnlisted)
                        {
                            hidden = true;
                        }
                    }

                    if (hidden)
                    {
                        if (_players.TryGetValue(a.Id, out var hv) && hv.Root.activeSelf) hv.Root.SetActive(false);
                        seen.Add(a.Id);
                        continue;
                    }

                    string present = DisguisePresent(a); // borrowed face for everyone but the local you
                    if (!_players.TryGetValue(a.Id, out var view))
                    {
                        view = new PlayerView();
                        view.Root.transform.SetParent(transform, false);
                        view.Bind(a, ov, present);
                        _players[a.Id] = view;
                    }
                    view.Render(a, dt, ov, fo, fall, lift, present);
                    seen.Add(a.Id);
                }
                foreach (var kv in _players)
                    if (!seen.Contains(kv.Key)) kv.Value.Root.SetActive(false);
            }

            RenderGameProps(snap);
            if (playAudio) PlayFx(snap);
        }

        // Each snapshot's fx are produced once (the room drains them per tick), so
        // mapping them here fires each cue exactly once: the SFX always, plus screen
        // juice (camera shake / flash) on the discrete, impactful ones. Juice routes
        // through ScreenFx, so "Reduce flashing & screen shake" silences it. Only
        // one-shot events get juice — never per-tick states like Burn/Ring/Pickup,
        // which would strobe (and defeat the very setting we honor here).
        private void PlayFx(Snapshot snap)
        {
            if (snap?.Fx == null) return;
            var audio = AudioService.Instance;
            foreach (var fx in snap.Fx)
            {
                switch (fx.Kind)
                {
                    case EffectKind.Death:
                        audio?.Play("death");
                        ScreenFx.Shake(0.55f); ScreenFx.Flash(new Color(1f, 0.12f, 0.12f), 0.16f);
                        break;
                    case EffectKind.Shatter:
                        audio?.Play("shatter");
                        ScreenFx.Shake(0.7f); ScreenFx.Flash(Color.white, 0.22f);
                        break;
                    case EffectKind.Shove:
                        // A crowd shove emits one effect PER target hit, so a single lunge could
                        // stack several drums in one frame — an annoying clatter. Throttle to one
                        // (softer) cue per ~0.15s and ease the camera kick.
                        if (Time.unscaledTime - _lastShoveSfx >= 0.15f)
                        {
                            audio?.Play("drum", 0.4f);
                            _lastShoveSfx = Time.unscaledTime;
                        }
                        ScreenFx.Shake(0.28f);
                        break;
                    case EffectKind.Confetti:
                        audio?.Play("good", 0.7f);
                        ScreenFx.Flash(new Color(1f, 0.85f, 0.35f), 0.18f);
                        break;
                    case EffectKind.Pickup: audio?.Play("pickup", 0.8f); SpawnReveal(fx); break;
                    case EffectKind.Ring: audio?.Play("chime", 0.6f); break;
                    case EffectKind.Freeze: audio?.Play("catch", 0.7f); break;
                    case EffectKind.Thaw: audio?.Play("pickup", 0.6f); break;
                    case EffectKind.Throw: audio?.Play("throw", 0.6f); break;
                    case EffectKind.Catch: audio?.Play("catch", 0.6f); break;
                    case EffectKind.Burn: audio?.Play("bad", 0.5f); break;
                    case EffectKind.Spark: audio?.Play("blip", 0.25f); break;
                }
            }
        }

        // Team accents copied from the web (lib/client/render TEAM_COLORS).
        private static readonly Color TeamBlue = new Color(0.161f, 0.714f, 0.965f); // #29b6f6
        private static readonly Color TeamPink = new Color(1f, 0.435f, 0.612f);     // #ff6f9c

        private void RenderGameProps(Snapshot snap)
        {
            _propsUsed = 0; _boxesUsed = 0; _ballsUsed = 0; _dollUsed = false;
            _pickupWorld.Clear();
            switch (snap?.Data)
            {
                case Boomerang.BoomData boom:
                {
                    DrawPickups(boom.Pickups);
                    string myId = (_sim?.LocalPlayerIds != null && _sim.LocalPlayerIds.Count > 0) ? _sim.LocalPlayerIds[0] : null;
                    if (boom.Rangs != null)
                        foreach (var r in boom.Rangs)
                        {
                            // Tint each rang to its thrower's player colour, and ring YOUR own rang
                            // in white so you can pick it out of the volley (per web drawRang).
                            string ocid = FindCharId(snap, r.Owner);
                            Color wood = string.IsNullOrEmpty(ocid) ? new Color(0.553f, 0.333f, 0.141f) : Vivid(Palette.Body(ocid));
                            DrawBoomerang(r.X, r.Y, r.Spin, r.Big, wood, r.Owner == myId);
                        }
                    break;
                }

                case KingOfTheHill.KothData koth:
                {
                    // Lava floor (#3a0b02) + molten pools, GREEN islands w/ a glowing molten rim (per web drawLava/drawIsland).
                    Box(640f, 360f, 1320f, 760f, new Color(0.227f, 0.043f, 0.008f), 0.01f, 0.006f);
                    for (int k = 0; k < 26; k++) // web bakes 26 additive molten pools
                        Disc((k * 173.3f) % 1280f, (k * 121.7f) % 720f, 40f + (k % 4) * 22f, new Color(1f, 0.471f, 0.078f, 0.19f), 0.012f);
                    if (koth.Islands != null)
                        foreach (var i in koth.Islands)
                        {
                            Disc(i.X, i.Y, i.R + 16f, new Color(1f, 0.471f, 0.078f, 0.5f), 0.014f);   // molten halo bleeding into lava
                            Disc(i.X, i.Y, i.R + 5f, new Color(1f, 0.55f, 0.157f, 0.95f), 0.018f);    // glowing molten rim around the rock
                            Disc(i.X, i.Y, i.R, i.Final ? new Color(0.337f, 0.255f, 0.18f) : new Color(0.243f, 0.176f, 0.122f), 0.02f); // dark-brown rock
                        }
                    DrawPickups(koth.Pickups);
                    if (snap.Actors != null && koth.KingId != null)
                        foreach (var a in snap.Actors)
                            if (a.Alive && a.Id == koth.KingId) // bobbing golden crown over the current king
                            {
                                float cb = 1.95f + Mathf.Sin(Time.time * 3f) * 0.18f;
                                Ball(a.Pos.X, a.Pos.Y, 13f, new Color(1f, 0.84f, 0f), cb);
                                Ball(a.Pos.X, a.Pos.Y, 7f, new Color(1f, 0.95f, 0.55f), cb + 0.12f); // jewel highlight
                            }
                    break;
                }

                case Dodgeball.DodgeData dodge:
                {
                    // Two team-tinted halves + dashed centre line (web drawDodgeballFloor).
                    float mid = dodge.Mid;
                    Box(mid * 0.5f, 360f, mid, 720f, new Color(TeamBlue.r, TeamBlue.g, TeamBlue.b, 0.12f), 0.008f, 0.005f);
                    Box((mid + 1280f) * 0.5f, 360f, 1280f - mid, 720f, new Color(TeamPink.r, TeamPink.g, TeamPink.b, 0.12f), 0.008f, 0.005f);
                    Box(mid, 360f, 6f, 720f, new Color(1f, 1f, 1f, 0.55f), 0.04f, 0.02f);
                    DrawTeamRings(snap, LocalId());
                    if (dodge.Balls != null)
                        foreach (var b in dodge.Balls)
                            Ball(b.X, b.Y, 14f, b.State == "Flight" ? new Color(1f, 0.56f, 0f) : new Color(0.88f, 0.63f, 0.13f), 0.5f); // enum ToString() is "Flight"
                    DrawPickups(dodge.Pickups);
                    break;
                }

                case KeepyUppy.KeepyData keepy:
                    // A dark dusk backdrop so a bright floor theme can't wash the scene out white.
                    Box(640f, 360f, 1320f, 760f, new Color(0.04f, 0.05f, 0.11f, 0.85f), 0.012f, 0.008f);
                    if (keepy.Balloons != null)
                        foreach (var b in keepy.Balloons)
                        {
                            // Owner's body colour (the same id the player uses), made vivid so
                            // near-white characters (cow/sheep/koala) don't read as plain white.
                            string cid = FindCharId(snap, b.Owner);
                            if (string.IsNullOrEmpty(cid)) cid = b.Color;
                            Box(b.X, b.Y, 1.4f, 1.4f, new Color(0.92f, 0.92f, 0.95f, 0.9f), 1.6f, 0.82f); // long thin string up to the balloon
                            Ball(b.X, b.Y, 30f, Vivid(Palette.Body(cid)), 1.7f);
                            Disc(b.X, b.Y, 7f, new Color(0f, 0f, 0f, 0.22f), 0.02f);
                        }
                    DrawPickups(keepy.Pickups);
                    // The spike "pin" juts from a player while their spike is out (a.Progress 0..1),
                    // so you can see the dangerous moment to pop balloons (web drawSpikePin).
                    if (snap.Actors != null)
                        foreach (var a in snap.Actors)
                            if (a.Alive && a.Progress > 0f)
                            {
                                float reach = 30f + 30f * a.Progress;
                                float tx = a.Pos.X + Mathf.Cos(a.Facing) * reach, ty = a.Pos.Y + Mathf.Sin(a.Facing) * reach;
                                Bar(a.Pos.X, a.Pos.Y, tx, ty, 6f, new Color(1f, 1f, 1f, 0.85f), 0.1f, 0.95f);
                                Ball(tx, ty, 6f, new Color(0.85f, 0.95f, 1f), 0.98f); // bright tip
                            }
                    break;

                case MusicalChairs.McData mc:
                    if (mc.Chairs != null)
                    {
                        bool scramble = mc.Phase != null && mc.Phase.ToLowerInvariant() == "scramble";
                        foreach (var c in mc.Chairs) DrawChair(c.X, c.Y, c.Claimed, scramble);
                    }
                    // A pulsing red ring under anyone who's stood still too long — the "you're about
                    // to be eliminated" tell, so the keep-moving rule is taught in real time.
                    if (mc.Warn != null && mc.Warn.Count > 0 && snap.Actors != null)
                        foreach (var wv in mc.Warn)
                            foreach (var a in snap.Actors)
                                if (a.Id == wv.Id && a.Alive)
                                {
                                    float wp = 0.5f + 0.5f * Mathf.Sin(Time.time * 12f);
                                    Disc(a.Pos.X, a.Pos.Y + 12f, 40f, new Color(1f, 0.18f, 0.18f, 0.35f + wp * 0.3f), 0.014f);
                                    break;
                                }
                    DrawPickups(mc.Pickups);
                    break;

                case Mingle.MingleData mingle:
                {
                    Disc(mingle.PlatformX, mingle.PlatformY, mingle.PlatformR, new Color(0.227f, 0.169f, 0.369f), 0.012f);        // platform #3a2b5e
                    Disc(mingle.PlatformX, mingle.PlatformY, mingle.PlatformR * 0.66f, new Color(0.133f, 0.094f, 0.212f), 0.014f); // inner #221836
                    // Spinning carousel: alternating gold/pink spokes + orbiting rim seats, rotated by
                    // mingle.Spin, so the platform visibly TURNS while the music plays (the riders turn with it).
                    {
                        const int spokes = 8;
                        for (int k = 0; k < spokes; k++)
                        {
                            float ang = mingle.Spin + k * (6.2831853f / spokes);
                            float ex = mingle.PlatformX + Mathf.Cos(ang) * mingle.PlatformR * 0.97f;
                            float ey = mingle.PlatformY + Mathf.Sin(ang) * mingle.PlatformR * 0.97f;
                            Color sc = (k % 2 == 0) ? new Color(1f, 0.835f, 0.31f, 0.30f) : new Color(1f, 0.36f, 0.62f, 0.26f);
                            Bar(mingle.PlatformX, mingle.PlatformY, ex, ey, 26f, sc, 0.013f, 0.015f);
                        }
                        for (int k = 0; k < 4; k++)
                        {
                            float ang = mingle.Spin + k * (6.2831853f / 4f);
                            float rx = mingle.PlatformX + Mathf.Cos(ang) * mingle.PlatformR * 0.86f;
                            float ry = mingle.PlatformY + Mathf.Sin(ang) * mingle.PlatformR * 0.86f;
                            Disc(rx, ry, 9f, new Color(1f, 0.92f, 0.55f, 0.55f), 0.017f);
                        }
                    }
                    // One translucent pad per room: barely visible while milling (so the
                    // floor reads through), then clear GREEN (correct size) / RED (wrong)
                    // once the grouping phase starts. Alpha is honoured by the transparent
                    // prop material — no opaque white discs.
                    bool mActive = mingle.Phase == "Mingle" || mingle.Phase == "Flash";
                    if (mingle.Rooms != null)
                        foreach (var r in mingle.Rooms)
                        {
                            Color c = mActive
                                ? (r.Ok ? new Color(0.30f, 0.85f, 0.55f, 0.50f) : new Color(0.90f, 0.32f, 0.32f, 0.45f))
                                : new Color(0.78f, 0.80f, 0.92f, 0.12f); // barely-there while milling
                            Disc(r.X, r.Y, r.R, c, 0.016f);
                        }
                    break;
                }

                case PropHunt.PropData prop:
                    // Children's-toy props: decoys and disguised hiders share the same toy per Kind,
                    // so a hidden player is indistinguishable from a decoy (BuildLayout hides the player).
                    if (prop.Decoys != null)
                        foreach (var d in prop.Decoys) DrawToy(d.Kind, d.X, d.Y);
                    if (snap.Actors != null)
                        foreach (var a in snap.Actors)
                        {
                            if (!a.Alive) continue;
                            if (a.It) DrawSword(a.Pos.X, a.Pos.Y);                                 // the Seeker
                            else if (!string.IsNullOrEmpty(a.Carrying)) DrawToy(a.Carrying, a.Pos.X, a.Pos.Y); // disguised hider
                        }
                    break;

                case Tag.TagData tag:
                    DrawTeamRings(snap, LocalId()); // icy team 0 freezers, coral team 1 runners + YOU
                    DrawPickups(tag.Pickups);
                    if (snap.Actors != null)
                        foreach (var a in snap.Actors)
                        {
                            if (!a.Alive) continue;
                            // FREEZERS ("it") wear a bobbing frost crown so it's obvious who's hunting —
                            // a colour ring alone didn't tell you who freezes vs who runs.
                            if (a.Team == tag.FreezerTeamId && !a.Frozen)
                            {
                                float cb = Mathf.Sin(Time.time * 4f + a.Pos.X * 0.01f) * 0.14f;
                                Disc(a.Pos.X, a.Pos.Y + 12f, 30f, new Color(0.30f, 0.72f, 1f, 0.5f), 0.015f); // bright cold ring
                                Ball(a.Pos.X, a.Pos.Y - 6f, 7f, new Color(0.55f, 0.82f, 1f), 2.7f + cb);          // icy crown
                                Ball(a.Pos.X, a.Pos.Y - 6f, 11f, new Color(0.7f, 0.9f, 1f, 0.5f), 2.66f + cb);     // frosty glow
                            }
                            // Frozen runners get a clear icy encasement + frost shard ("thaw me!").
                            if (a.Frozen)
                            {
                                Disc(a.Pos.X, a.Pos.Y + 8f, 40f, new Color(0.62f, 0.86f, 1f, 0.42f), 0.018f); // ice pool
                                float fb = Mathf.Sin(Time.time * 3f + a.Pos.X * 0.01f) * 0.12f;
                                Ball(a.Pos.X, a.Pos.Y - 4f, 9f, new Color(0.78f, 0.93f, 1f, 0.9f), 2.5f + fb);  // frost shard above
                            }
                        }
                    break;

                case RedLightGreenLight.RlglData rl:
                {
                    float fx = rl.FinishX;
                    Box((fx + 1280f) * 0.5f, 360f, 1280f - fx, 720f, new Color(0.41f, 0.94f, 0.68f, 0.16f), 0.008f, 0.005f); // finish zone
                    Box(fx, 360f, 8f, 720f, new Color(0.41f, 0.94f, 0.68f, 0.9f), 0.05f, 0.03f);                            // finish line
                    float dx = fx + (1280f - fx) * 0.5f;
                    Doll(dx, 360f, rl.Red); // the watching doll (real model if present, shared with Simon)
                    break;
                }

                case TugOfWar.TugData tug:
                {
                    // Top-down tug: left/right team platforms, a dark central pit, and the rope
                    // running across the middle. The knot slides toward the WINNER. Players are
                    // staged in two rows on each ledge by TugLayout (web pullerStandX), so they
                    // line up ALONG the rope rather than stacked above/below it.
                    float rp = Mathf.Clamp(tug.RopePos, -1f, 1f);
                    Box(225f, 360f, 450f, 720f, new Color(0.416f, 0.318f, 0.227f), 0.02f, 0.011f);  // left platform #6a513a
                    Box(1055f, 360f, 450f, 720f, new Color(0.416f, 0.318f, 0.227f), 0.02f, 0.011f); // right platform
                    Box(640f, 360f, 380f, 720f, new Color(0.063f, 0.039f, 0.024f), 0.012f, 0.013f); // the pit (dark chasm)
                    float knotX = 640f - rp * 164f;
                    Box(640f, 360f, 1140f, 10f, new Color(0.792f, 0.631f, 0.353f), 0.12f, 0.5f);    // rope #caa15a (raised)
                    Color knotCol = Mathf.Abs(rp) > 0.45f ? new Color(1f, 0.322f, 0.322f) : new Color(1f, 0.835f, 0.31f);
                    Box(knotX, 360f, 24f, 32f, knotCol, 0.16f, 0.55f);                              // centre knot, slides to the winner
                    break;
                }

                case JumpRope.RopeData rope:
                {
                    // A bridge deck (walkway) the jumpers cross left→right; JumpLayout puts every
                    // jumper on ONE horizontal line by their Pos. The rope is a HORIZONTAL bar
                    // spanning the deck that sweeps UP in height (worldY) as Phase cycles — it
                    // rises overhead and comes back down to the planks (where it's deadly red).
                    Box(627f, 360f, 947f, 240f, new Color(0.36f, 0.26f, 0.16f), 0.02f, 0.011f);     // deck
                    int planks = Mathf.Clamp(rope.BridgeLen, 2, 40);
                    for (int k = 0; k < planks; k++)
                        Box(Mathf.Lerp(154f, 1101f, (k + 0.5f) / planks), 360f, 8f, 240f, new Color(0.27f, 0.19f, 0.11f), 0.025f, 0.013f); // plank seams
                    Box(77f, 360f, 154f, 280f, new Color(0.169f, 0.227f, 0.290f), 0.04f, 0.011f);   // start platform
                    Box(1190f, 360f, 179f, 280f, new Color(0.122f, 0.404f, 0.235f), 0.04f, 0.011f); // safe platform
                    Box(1101f, 360f, 6f, 280f, new Color(0.41f, 0.94f, 0.68f, 0.9f), 0.05f, 0.04f); // finish line
                    float ph = Mathf.Clamp01(rope.Phase);
                    bool atGround = ph < 0.12f || ph > 0.88f;
                    float midY = 0.35f + Mathf.Sin(ph * Mathf.PI) * 3.4f; // rope bows up overhead, back down to the deck
                    DrawRope(40f, 1240f, 360f, 0.35f, midY, atGround ? new Color(1f, 0.32f, 0.32f) : new Color(1f, 0.835f, 0.31f));
                    break;
                }

                case GlassBridge.GlassData glass:
                {
                    // A 2-lane glass bridge between the platforms. Each row: the SAFE side glows green
                    // once revealed, the SHATTERED side shows a dark red break, an inferred hole is
                    // dark, and untested panes are cloudy glass. The active player stands on the safe
                    // panes (see GlassLayout) so the path it took reads clearly.
                    Box(150f, 360f, 220f, 560f, new Color(0.18f, 0.16f, 0.24f), 0.02f, 0.011f);   // near platform
                    Box(1130f, 360f, 220f, 560f, new Color(0.18f, 0.16f, 0.24f), 0.02f, 0.011f);  // far platform
                    int rows = Mathf.Max(1, glass.Rows);
                    float spacing = 730f / rows;
                    float paneW = Mathf.Min(104f, spacing * 0.82f); // shrink so rows don't overlap (was a fixed 100)
                    float gpulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
                    for (int r = 0; r < rows; r++)
                    {
                        float gx = Mathf.Lerp(300f, 1030f, (r + 0.5f) / rows);
                        int safe = (glass.RevealedSides != null && r < glass.RevealedSides.Count) ? glass.RevealedSides[r] : -1;
                        int broke = (glass.BrokeSide != null && r < glass.BrokeSide.Count) ? glass.BrokeSide[r] : -1;
                        bool frontierRow = r == glass.Frontier && glass.Phase == "choose";
                        for (int side = 0; side < 2; side++)
                        {
                            float gy = 360f + (side == 0 ? -85f : 85f);
                            Color c = safe == side ? new Color(0.30f, 0.92f, 0.60f, 0.88f)       // revealed SAFE (green)
                                    : broke == side ? new Color(0.55f, 0.06f, 0.10f, 0.92f)      // SHATTERED (dark red)
                                    : safe == 1 - side ? new Color(0.03f, 0.02f, 0.06f, 0.92f)   // inferred hole (black)
                                    : new Color(0.60f, 0.85f, 1f, 0.42f);                        // untested cloudy glass
                            // The row being decided pulses gold under BOTH panes — "pick one of these".
                            if (frontierRow && safe < 0)
                                Box(gx, gy, paneW + 12f, 134f, new Color(1f, 0.86f, 0.2f, 0.22f + gpulse * 0.3f), 0.045f, 0.03f);
                            Box(gx, gy, paneW, 122f, c, 0.05f, 0.04f);
                            // a thin white rim so each pane reads as a distinct tile, not a blur
                            Box(gx, gy, paneW, 122f, new Color(1f, 1f, 1f, 0.10f), 0.055f, 0.05f);
                        }
                    }
                    break;
                }

                case RpsMinusOne.RpsData rps:
                {
                    // Focused 1v1: RpsLayout stages the two focal duelists at centre top/bottom and
                    // hides everyone else (the whole field shuffling between duels read as "chaos").
                    Box(640f, 360f, 6f, 420f, new Color(1f, 1f, 1f, 0.10f), 0.04f, 0.02f); // subtle divider only
                    // Reveal the focal duel's THROWS as hand icons so you can see what your rival
                    // picked (rock=orb, paper=slab, scissors=V) — and which one they KEPT on resolve.
                    RpsMinusOne.DuelView focal = FocalDuel(rps, LocalId());
                    if (focal != null)
                    {
                        DrawRpsThrows(470f, focal.AThrows, focal.AKeep, rps.Phase); // A (bottom player) — throws above it
                        DrawRpsThrows(250f, focal.BThrows, focal.BKeep, rps.Phase); // B (top player) — throws below it
                    }
                    // Gold "YOU" ring + bobbing orb on the local duelist so you instantly read
                    // which fighter is yours (1v1 has no team rings to lean on).
                    DrawYouMarker(snap, LocalId());
                    break;
                }

                case PresentSwap.PresentData _:
                {
                    // Sim seats everyone in an ellipse; a wrapped gift (pink ribbon #ff2e88, per the
                    // web's wrap) sits just in front of each player, facing the centre.
                    Disc(640f, 360f, 190f, new Color(0.16f, 0.10f, 0.22f), 0.012f); // centre rug
                    if (snap.Actors != null)
                        foreach (var a in snap.Actors)
                        {
                            if (!a.Alive) continue;
                            float dxp = 640f - a.Pos.X, dyp = 360f - a.Pos.Y;
                            float len = Mathf.Max(1f, Mathf.Sqrt(dxp * dxp + dyp * dyp));
                            float px = a.Pos.X + dxp / len * 58f, py = a.Pos.Y + dyp / len * 58f;
                            Box(px, py, 34f, 34f, new Color(0.45f, 0.34f, 0.62f), 0.5f, 0.25f); // box
                            Box(px, py, 36f, 7f, new Color(1f, 0.18f, 0.53f), 0.55f, 0.5f);     // ribbon h
                            Box(px, py, 7f, 36f, new Color(1f, 0.18f, 0.53f), 0.55f, 0.5f);     // ribbon v
                            Ball(px, py, 6f, new Color(1f, 0.35f, 0.65f), 0.62f);               // bow
                        }
                    break;
                }

                case ChutesAndLadders.ChutesData ch:
                {
                    // Centred SQUARE board (equal x/y pitch) so ladders read as clean diagonals
                    // instead of a squashed criss-cross. MUST match the sim CellCenter exactly so
                    // the climber players land on their drawn cells.
                    int cols = Mathf.Max(1, ch.Cols), rows = Mathf.Max(1, ch.Goal / cols);
                    float pitch = Mathf.Min(1060f / cols, 500f / rows);
                    float ox = 640f - pitch * cols * 0.5f, oyBottom = 360f + pitch * rows * 0.5f;
                    Vector2 Cell(int sq)
                    {
                        int s = Mathf.Clamp(sq, 1, cols * rows) - 1;
                        int row = s / cols, inRow = s % cols;
                        int col = row % 2 == 0 ? inRow : cols - 1 - inRow;
                        return new Vector2(ox + (col + 0.5f) * pitch, oyBottom - (row + 0.5f) * pitch);
                    }
                    for (int s = 1; s <= cols * rows; s++)
                    {
                        var p = Cell(s);
                        Box(p.x, p.y, pitch * 0.92f, pitch * 0.92f, s % 2 == 0 ? new Color(0.12f, 0.16f, 0.22f) : new Color(0.18f, 0.23f, 0.31f), 0.03f, 0.015f);
                        if (s == ch.Goal)
                        {
                            Box(p.x, p.y, pitch * 0.92f, pitch * 0.92f, new Color(0.41f, 0.94f, 0.68f, 0.55f), 0.05f, 0.04f); // green GOAL glow
                            Ball(p.x, p.y, pitch * 0.22f, new Color(0.41f, 0.94f, 0.68f), 0.12f);                            // flag marker
                        }
                    }
                    if (ch.Ladders != null)
                        foreach (var l in ch.Ladders)
                            if (l != null && l.Length >= 2) DrawLadder(Cell(l[0]), Cell(l[1])); // wooden rails + rungs
                    if (ch.Chutes != null)
                        foreach (var cv in ch.Chutes)
                        {
                            var cc = Cell(cv.Square);
                            var dl = new Vector2(cc.x - pitch * 0.62f, cc.y + pitch * 0.5f); // down-left fork arm
                            var dr = new Vector2(cc.x + pitch * 0.62f, cc.y + pitch * 0.5f); // down-right fork arm
                            DrawChute(cc, dl, dr, cv.Left, cv.Right);
                        }
                    // Pulse the local player's cell while they're at a fork (the key decision beat).
                    string myId = (_sim?.LocalPlayerIds != null && _sim.LocalPlayerIds.Count > 0) ? _sim.LocalPlayerIds[0] : null;
                    if (myId != null && ch.Climbers != null)
                        foreach (var cv in ch.Climbers)
                            if (cv.Id == myId && cv.Choosing >= 0)
                                Disc(Cell(cv.Square).x, Cell(cv.Square).y, pitch * 0.55f, new Color(0.69f, 0.42f, 0.90f, 0.5f), 0.045f);
                    break;
                }

                case SimonSays.SimonData simon:
                {
                    // The caller doll (real model if imported) presides over the crowd; a
                    // colour-coded command beacon on the floor shows the current order.
                    Doll(640f, 150f, simon.Freeze);
                    Color cmd = simon.Freeze ? new Color(0.40f, 0.70f, 1f)
                              : simon.Command == "head" ? new Color(0.41f, 0.94f, 0.68f)
                              : simon.Command == "nose" ? new Color(1f, 0.80f, 0.30f)
                              : simon.Command == "blink" ? new Color(0.80f, 0.40f, 0.90f)
                              : simon.Command == "flip" ? new Color(1f, 0.45f, 0.40f)
                              : simon.Command == "jump" ? new Color(0.30f, 0.85f, 0.90f)
                              : new Color(0.6f, 0.6f, 0.65f);
                    Disc(640f, 250f, 80f, cmd, 0.03f);     // command beacon between caller and crowd
                    Ball(640f, 250f, 22f, cmd, 1.0f);
                    break;
                }
            }
            for (int i = _propsUsed; i < _props.Count; i++) if (_props[i].activeSelf) _props[i].SetActive(false);
            for (int i = _boxesUsed; i < _boxes.Count; i++) if (_boxes[i].activeSelf) _boxes[i].SetActive(false);
            for (int i = _ballsUsed; i < _balls.Count; i++) if (_balls[i].activeSelf) _balls[i].SetActive(false);
            if (_doll != null && !_dollUsed && _doll.activeSelf) _doll.SetActive(false);
        }

        // A team-coloured accent ring under each player (freeze tag / dodgeball), per the
        // web's drawTeamRing — an oval at the actor's feet in the team colour.
        /// <summary>The local player's actor id (for "this is YOU" highlights), or null.</summary>
        private string LocalId() =>
            (_sim?.LocalPlayerIds != null && _sim.LocalPlayerIds.Count > 0) ? _sim.LocalPlayerIds[0] : null;

        // Round, soft role rings under each team player (icy blue = team 0, warm coral = team 1) —
        // replacing the old flat white rectangles — plus a bright pulsing "YOU" ring + a bobbing
        // marker over the local player so you can always pick yourself out of the crowd.
        private void DrawTeamRings(Snapshot snap, string myId)
        {
            if (snap?.Actors == null) return;
            foreach (var a in snap.Actors)
            {
                if (!a.Alive || a.Team < 0) continue;
                var c = a.Team == 0 ? TeamBlue : TeamPink;
                Disc(a.Pos.X, a.Pos.Y + 12f, 34f, new Color(c.r, c.g, c.b, 0.26f), 0.012f); // soft halo
                Disc(a.Pos.X, a.Pos.Y + 12f, 23f, new Color(c.r, c.g, c.b, 0.55f), 0.013f); // brighter core
            }
            if (!string.IsNullOrEmpty(myId)) DrawYouMarker(snap, myId);
        }

        // A bright pulsing ground ring + a bobbing orb over the local player's head, so "which
        // one am I?" is never a question (used by the team games and any role-based mode).
        private void DrawYouMarker(Snapshot snap, string myId)
        {
            Actor me = null;
            foreach (var a in snap.Actors) if (a.Id == myId && a.Alive) { me = a; break; }
            if (me == null) return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4.2f);
            var you = new Color(1f, 0.86f, 0.14f); // bright gold
            Disc(me.Pos.X, me.Pos.Y + 12f, 40f + pulse * 9f, new Color(you.r, you.g, you.b, 0.20f + pulse * 0.18f), 0.014f);
            Disc(me.Pos.X, me.Pos.Y + 12f, 29f, new Color(you.r, you.g, you.b, 0.6f), 0.016f);
            float bob = Mathf.Sin(Time.time * 3f) * 0.18f;
            Ball(me.Pos.X, me.Pos.Y, 11f, you, 2.75f + bob);          // floating marker orb
            Ball(me.Pos.X, me.Pos.Y, 5f, Color.white, 2.83f + bob);  // bright core
        }

        // ── Overhead player numbers (IMGUI overlay) ──────────────────────────
        // Every living player wears their lobby tag (#N) above their head, so a spoken
        // "Player N has been eliminated" maps to a body on screen — and yours, larger
        // and gold, answers "which one am I?" alongside the YOU marker. Drawn in screen
        // space (projected from each smoothed body) so the text stays crisp and evenly
        // sized regardless of distance. Live arena only: preview stations (_themeIndex ≥ 0)
        // and the menus draw nothing, and tags hold off until the round actually starts.
        private GUIStyle _numStyle;

        private void OnGUI()
        {
            if (_camera == null || _themeIndex >= 0 || _sim == null) return;
            if (!_sim.HasSeries || !_sim.PlayStarted || _sim.Phase != RoomPhase.Playing) return;
            var snap = _sim.Latest;
            if (snap?.Actors == null) return;

            if (_numStyle == null)
            {
                var font = Resources.Load<Font>("Fonts/Baloo2-Bold");
                _numStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                if (font != null) _numStyle.font = font;
                _numStyle.normal.textColor = Color.white; // tinted per-label via GUI.color
            }

            var locals = _sim.LocalPlayerIds;
            foreach (var a in snap.Actors)
            {
                if (!a.Alive || a.Number <= 0) continue;
                if (!_players.TryGetValue(a.Id, out var view) || view.Root == null || !view.Root.activeSelf) continue;
                Vector3 sp = _camera.WorldToScreenPoint(view.Root.transform.position + Vector3.up * 1.7f);
                if (sp.z <= 0f) continue; // behind the camera
                bool isLocal = locals != null && locals.Contains(a.Id);
                // 🥸 Disguise: to everyone but you, the number reads as the impersonated player's.
                int num = (!isLocal && !string.IsNullOrEmpty(a.DisguiseCharId) && a.DisguiseNumber > 0) ? a.DisguiseNumber : a.Number;
                DrawNumberTag(sp.x, Screen.height - sp.y, num, isLocal);
            }

            DrawMysteryOrbs();
            DrawReveals();
            DrawMyPickupBanner();
        }

        // The "?" floating over every pickup orb — identical for all, so the ground
        // never tells you good from bad. Projected from the anchors recorded in
        // DrawPowerupToken; bobbing pale glyph with a dark outline for legibility.
        private void DrawMysteryOrbs()
        {
            if (_pickupWorld.Count == 0) return;
            if (_orbStyle == null)
            {
                var font = Resources.Load<Font>("Fonts/Baloo2-Bold");
                _orbStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 20 };
                if (font != null) _orbStyle.font = font;
            }
            foreach (var wp in _pickupWorld)
            {
                Vector3 sp = _camera.WorldToScreenPoint(wp);
                if (sp.z <= 0f) continue;
                OutlinedGlyph(sp.x, Screen.height - sp.y, "?", _orbStyle, new Color(1f, 1f, 1f, 0.95f), 1.5f);
            }
        }

        // Pickup reveals: the icon + name that float up over your blob the instant you
        // grab an orb (green blessing / red curse / purple wildcard) — the payoff that
        // tells you what the mystery was. Rise + fade over RevealLife seconds.
        private void DrawReveals()
        {
            if (_reveals.Count == 0) return;
            if (_revealStyle == null)
            {
                var font = Resources.Load<Font>("Fonts/Baloo2-Bold");
                _revealStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 26 };
                if (font != null) _revealStyle.font = font;
            }
            float now = Time.unscaledTime;
            for (int i = _reveals.Count - 1; i >= 0; i--)
            {
                var rv = _reveals[i];
                float t = now - rv.Born;
                if (t >= RevealLife) { _reveals.RemoveAt(i); continue; }
                // Track the grabbing blob if it's still on stage; else stay where it was grabbed.
                Vector3 anchor = rv.World;
                if (rv.ActorId != null && _players.TryGetValue(rv.ActorId, out var pv) && pv.Root != null && pv.Root.activeSelf)
                    anchor = pv.Root.transform.position + Vector3.up * 1.7f;
                Vector3 sp = _camera.WorldToScreenPoint(anchor);
                if (sp.z <= 0f) continue;
                float k = t / RevealLife;
                float x = sp.x, y = Screen.height - sp.y - k * 50f; // rise
                float alpha = 1f - k * k;                            // ease-out fade
                var col = new Color(rv.Color.r, rv.Color.g, rv.Color.b, alpha);
                OutlinedGlyph(x, y, rv.Text, _revealStyle, col, 2f, alpha);
            }
        }

        // One centered glyph/string with a 4-way black outline (its own dark backdrop,
        // so it stays legible over any bright floor). Shared by "?" orbs and reveals.
        private void OutlinedGlyph(float x, float y, string s, GUIStyle style, Color fill, float outline, float alpha = 1f, float width = 240f)
        {
            float bw = width; float bh = style.fontSize + 10f;
            var r = new Rect(x - bw * 0.5f, y - bh * 0.5f, bw, bh);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f * alpha);
            GUI.Label(new Rect(r.x - outline, r.y, bw, bh), s, style);
            GUI.Label(new Rect(r.x + outline, r.y, bw, bh), s, style);
            GUI.Label(new Rect(r.x, r.y - outline, bw, bh), s, style);
            GUI.Label(new Rect(r.x, r.y + outline, bw, bh), s, style);
            GUI.color = fill;
            GUI.Label(r, s, style);
            GUI.color = prev;
        }

        // The identity to PRESENT for this actor: a disguised player wears a borrowed
        // face for everyone EXCEPT the local player(s) who actually are them. (In-process
        // play only — the online wire format doesn't carry DisguiseCharId, so a networked
        // client sees everyone undisguised; extend Net/Wire.cs for parity.)
        private string DisguisePresent(Actor a)
        {
            if (string.IsNullOrEmpty(a.DisguiseCharId)) return null;
            var locals = _sim?.LocalPlayerIds;
            return (locals != null && locals.Contains(a.Id)) ? null : a.DisguiseCharId;
        }

        // Spawn a pickup reveal from the sim's Pickup effect (fx.Tag = powerup key).
        // The effect is emitted at the collector's position, so the nearest blob IS
        // the player who grabbed it — we tag the reveal to them so it floats ALONG
        // with them (Boomerang-Fu style) rather than stranding on the ground spot.
        private void SpawnReveal(Effect fx)
        {
            string text = PowerupCatalog.RevealText(fx.Tag);
            Color col = Color.white;
            if (PowerupCatalog.TryGet(fx.Tag, out var meta)) ColorUtility.TryParseHtmlString(meta.ColorHex, out col);
            Vector3 world0 = LogicalSpace.ToWorld(fx.X, fx.Y) + Vector3.up * 1.6f;
            string actorId = null; float best = float.MaxValue;
            foreach (var kv in _players)
            {
                if (kv.Value?.Root == null || !kv.Value.Root.activeSelf) continue;
                float d = (kv.Value.Root.transform.position - world0).sqrMagnitude;
                if (d < best) { best = d; actorId = kv.Key; }
            }
            _reveals.Add(new Reveal { ActorId = actorId, World = world0, Born = Time.unscaledTime, Text = text, Color = col });

            // If it was YOU who grabbed it, slam up a bold banner that says what it DOES.
            var locals = _sim?.LocalPlayerIds;
            if (actorId != null && locals != null && locals.Contains(actorId))
            {
                _myPickupText = PowerupCatalog.RevealBanner(fx.Tag);
                _myPickupColor = col;
                _myPickupBorn = Time.unscaledTime;
            }
        }

        // The big "you grabbed X — it does Y" banner, centred and high so it can't be
        // missed in the scrum. Fades over MyPickupLife. Local-player pickups only.
        private void DrawMyPickupBanner()
        {
            if (_myPickupText == null) return;
            float t = Time.unscaledTime - _myPickupBorn;
            if (t >= MyPickupLife) { _myPickupText = null; return; }
            if (_bannerStyle == null)
            {
                var font = Resources.Load<Font>("Fonts/Baloo2-Bold");
                _bannerStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 34 };
                if (font != null) _bannerStyle.font = font;
            }
            float k = t / MyPickupLife;
            float alpha = Mathf.Clamp01(t < 0.12f ? t / 0.12f : 1f - Mathf.Pow(k, 3f)); // pop in, ease out
            float pop = t < 0.18f ? 1f + (0.18f - t) * 1.2f : 1f;                       // tiny overshoot on entry
            _bannerStyle.fontSize = Mathf.RoundToInt(34 * pop);
            float y = Screen.height * 0.20f;
            var col = new Color(_myPickupColor.r, _myPickupColor.g, _myPickupColor.b, alpha);
            OutlinedGlyph(Screen.width * 0.5f, y, _myPickupText, _bannerStyle, col, 2.5f, alpha, Mathf.Min(Screen.width - 40f, 900f));
        }

        // One "#N" tag centered above (x, y) in GUI space. The black outline gives each
        // glyph its own dark backdrop so it stays legible over any bright floor (white
        // text on the live arena is the known legibility trap); local is bigger and gold.
        private void DrawNumberTag(float x, float y, int number, bool isLocal)
        {
            int fs = isLocal ? 22 : 15;
            _numStyle.fontSize = fs;
            string s = "#" + number;
            float bw = 120f, bh = fs + 6f;
            var r = new Rect(x - bw * 0.5f, y - bh, bw, bh);
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, isLocal ? 0.9f : 0.78f);
            const float o = 1.5f;
            GUI.Label(new Rect(r.x - o, r.y, bw, bh), s, _numStyle);
            GUI.Label(new Rect(r.x + o, r.y, bw, bh), s, _numStyle);
            GUI.Label(new Rect(r.x, r.y - o, bw, bh), s, _numStyle);
            GUI.Label(new Rect(r.x, r.y + o, bw, bh), s, _numStyle);
            GUI.color = isLocal ? new Color(1f, 0.86f, 0.14f) : new Color(1f, 1f, 1f, 0.95f);
            GUI.Label(r, s, _numStyle);
            GUI.color = prev;
        }

        private void DrawPickups(List<PickupView> pickups)
        {
            if (pickups == null) return;
            foreach (var p in pickups)
                DrawPowerupToken(p.X, p.Y); // every orb identical — a mystery until grabbed (web-canon)
        }

        // Every orb is the SAME hovering mystery: a hue-cycling glowing sphere with a
        // pulsing halo and a "?" (drawn in OnGUI from the anchor recorded here). No
        // good/bad tell on the ground — grabbing one is a gamble, and the reveal that
        // floats over your blob is the payoff. (Was a green/red +/▾ badge.)
        private void DrawPowerupToken(float x, float y)
        {
            float ph = Time.time * 2.2f + (x * 0.013f + y * 0.017f); // per-token phase (no lockstep)
            float bob = Mathf.Sin(ph) * 0.14f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(ph * 1.4f);
            float hue = Mathf.Repeat(Time.time * 0.12f + (x * 0.0007f + y * 0.0009f), 1f); // slow rainbow, same rule for all
            Color body = Color.HSVToRGB(hue, 0.55f, 1f);
            Color glow = Color.HSVToRGB(hue, 0.35f, 1f);
            float wy = 0.62f + bob;

            Disc(x, y, 12f, new Color(0f, 0f, 0f, 0.30f), 0.02f);                                       // ground shadow
            Disc(x, y, 24f + pulse * 7f, new Color(glow.r, glow.g, glow.b, 0.20f + pulse * 0.18f), wy - 0.06f); // halo
            Ball(x, y, 18f, body, wy);                                                                   // orb body
            Ball(x, y, 18f, new Color(1f, 1f, 1f, 0.18f), wy + 0.03f);                                   // sheen
            _pickupWorld.Add(LogicalSpace.ToWorld(x, y) + new Vector3(0f, wy + 0.22f, 0f));              // "?" anchor (OnGUI)
        }

        // A prop sitting at/below this world height is a flat GROUND decal (islands, pads,
        // board tiles, lines, shadows): it renders BEHIND the character sprites. Anything
        // taller (balloons, crowns, the rope/knot, the doll, floating pickups, flying rangs)
        // is a raised prop that sorts AMONG the players. The very flattest fills (below
        // FloorFillMaxY) are full-arena floor replacements and drop a further tier so they
        // can't hide the decals standing on them.
        private const float FloorFillMaxY = 0.012f; // full-arena floor fills (lava, dusk, washes)
        private const float GroundLayerMaxY = 0.1f;  // decals that lie on the floor
        private static UnityEngine.Material PropMat(float worldY)
            => worldY < FloorFillMaxY ? ViewMaterials.PropsFloor
             : worldY <= GroundLayerMaxY ? ViewMaterials.PropsGround
             : ViewMaterials.Props;

        /// <summary>Draw a flat disc on the floor (the natural top-down primitive).</summary>
        private void Disc(float lx, float ly, float logicalRadius, Color color, float height)
        {
            GameObject go;
            if (_propsUsed < _props.Count) go = _props[_propsUsed];
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(go.GetComponent<Collider>());
                go.name = "Prop";
                go.transform.SetParent(transform, false);
                _props.Add(go);
            }
            _propsUsed++;
            if (!go.activeSelf) go.SetActive(true);
            go.GetComponent<Renderer>().sharedMaterial = PropMat(height); // ground decals sort behind players
            float d = LogicalSpace.WorldRadius(logicalRadius) * 2f;
            go.transform.localScale = new Vector3(d, 0.04f, d); // a thin disc (default cylinder is 2 units tall)
            go.transform.position = LogicalSpace.ToWorld(lx, ly) + new Vector3(0f, height, 0f);
            ViewMaterials.SetColor(go.GetComponent<Renderer>(), new MaterialPropertyBlock(), color);
        }

        /// <summary>A box prop with a logical XZ footprint (lw × ll), a world-space height/Y,
        /// and an optional Y rotation — for ropes, planks, panes, board tiles, pits, dolls.</summary>
        private void Box(float lx, float ly, float lw, float ll, Color color, float worldH, float worldY, float yRotDeg = 0f)
        {
            GameObject go;
            if (_boxesUsed < _boxes.Count) go = _boxes[_boxesUsed];
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.name = "Box";
                go.transform.SetParent(transform, false);
                _boxes.Add(go);
            }
            _boxesUsed++;
            if (!go.activeSelf) go.SetActive(true);
            go.GetComponent<Renderer>().sharedMaterial = PropMat(worldY); // ground decals sort behind players
            go.transform.position = LogicalSpace.ToWorld(lx, ly) + new Vector3(0f, worldY, 0f);
            go.transform.rotation = Quaternion.Euler(0f, yRotDeg, 0f);
            go.transform.localScale = new Vector3(lw * LogicalSpace.Scale, worldH, ll * LogicalSpace.Scale);
            ViewMaterials.SetColor(go.GetComponent<Renderer>(), new MaterialPropertyBlock(), color);
        }

        /// <summary>A sphere prop floating at a world-space height — balloons, heads.</summary>
        private void Ball(float lx, float ly, float logicalRadius, Color color, float worldY)
        {
            GameObject go;
            if (_ballsUsed < _balls.Count) go = _balls[_ballsUsed];
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.name = "Ball";
                go.transform.SetParent(transform, false);
                _balls.Add(go);
            }
            _ballsUsed++;
            if (!go.activeSelf) go.SetActive(true);
            go.GetComponent<Renderer>().sharedMaterial = PropMat(worldY); // ground decals sort behind players
            float d = LogicalSpace.WorldRadius(logicalRadius) * 2f;
            go.transform.localScale = new Vector3(d, d, d);
            go.transform.position = LogicalSpace.ToWorld(lx, ly) + new Vector3(0f, worldY, 0f);
            ViewMaterials.SetColor(go.GetComponent<Renderer>(), new MaterialPropertyBlock(), color);
        }

        /// <summary>A dotted line of small discs between two logical points — ladders, chutes.</summary>
        private void Line(float x1, float y1, float x2, float y2, float logicalThick, Color color, float worldY)
        {
            float dist = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            int n = Mathf.Clamp(Mathf.RoundToInt(dist / 26f), 2, 28);
            for (int k = 0; k <= n; k++)
            {
                float t = k / (float)n;
                Disc(Mathf.Lerp(x1, x2, t), Mathf.Lerp(y1, y2, t), logicalThick, color, worldY);
            }
        }

        // ── Per-game player staging ────────────────────────────────────────
        // Bespoke games (tug, jump, glass, rps, prop hunt) stage actors by game
        // STATE, not by raw sim position — the web renderer fully repositions them.
        // We compute a logical override (+ facing) per actor so the players line up
        // with the scene props. An actor with no entry keeps its sim position;
        // Hide removes a player entirely (disguised prop-hunt hiders, shown as a toy);
        // HideUnlisted hides everyone not explicitly placed (the RPS focal duel).
        private sealed class PlayerLayout
        {
            public readonly Dictionary<string, Vector2> Pos = new Dictionary<string, Vector2>();
            public readonly Dictionary<string, float> Facing = new Dictionary<string, float>();
            public readonly HashSet<string> Hide = new HashSet<string>();
            public readonly HashSet<string> FallDeath = new HashSet<string>(); // glass: drop through the glass, then vanish
            public readonly HashSet<string> Airborne = new HashSet<string>();   // jump rope: lift the player while mid-jump
            public bool HideUnlisted;
        }

        private PlayerLayout BuildLayout(Snapshot snap, float dt)
        {
            if (snap?.Actors == null) return null;
            switch (snap.Data)
            {
                case TugOfWar.TugData tug:    return TugLayout(snap, tug);
                case JumpRope.RopeData rope:  return JumpLayout(rope);
                case RpsMinusOne.RpsData rps: return RpsLayout(rps);
                case GlassBridge.GlassData g: return GlassLayout(snap, g, dt);
                case PropHunt.PropData _:     return PropHuntLayout(snap);
                default: return null;
            }
        }

        // Two staggered rows on each team's ledge, anchored back from the pit and
        // leaning toward the winner (web pullerStandX). Lines the teams up ALONG the
        // rope instead of stacking them vertically the way the raw sim lanes do. Once the
        // result locks (LoserTeam set), the losing team is DRAGGED off its ledge into the
        // central pit by tug.FallT and then plummets — a visible "pulled into the pit".
        private static PlayerLayout TugLayout(Snapshot snap, TugOfWar.TugData tug)
        {
            var L = new PlayerLayout();
            float rp = Mathf.Clamp(tug.RopePos, -1f, 1f);
            float lean = -rp * 95f;
            bool resolved = tug.LoserTeam >= 0;
            float ft = Mathf.Clamp01(tug.FallT);
            for (int t = 0; t < 2; t++)
            {
                var team = snap.Actors.Where(a => a.Team == t)
                                      .OrderBy(a => a.Id, System.StringComparer.Ordinal).ToList();
                float side = t == 0 ? -1f : 1f;
                float edge = t == 0 ? 450f : 830f;
                bool losing = resolved && t == tug.LoserTeam;
                for (int i = 0; i < team.Count; i++)
                {
                    float x = edge + side * (60f + i * 58f) + lean;
                    x = side < 0 ? Mathf.Clamp(x, 20f, 440f) : Mathf.Clamp(x, 840f, 1260f); // stay on the ledge, off the pit
                    float y = 360f + ((i % 2 == 0) ? -34f : 34f);
                    if (losing)
                    {
                        // First half of the beat: hauled to the pit. Second half: drop through it.
                        if (ft > 0.5f) { L.FallDeath.Add(team[i].Id); continue; } // plummet from the pit (no Pos override)
                        float slide = Mathf.Clamp01(ft / 0.5f);
                        float px = 640f + ((i % 2 == 0) ? -28f : 28f);   // bunch over the central chasm
                        float py = 360f + (((i / 2) % 2 == 0) ? -22f : 22f);
                        x = Mathf.Lerp(x, px, slide);
                        y = Mathf.Lerp(y, py, slide);
                    }
                    L.Pos[team[i].Id] = new Vector2(x, y);
                    L.Facing[team[i].Id] = t == 0 ? 0f : Mathf.PI; // face the rope
                }
            }
            return L;
        }

        // Every jumper on ONE horizontal line across the deck, placed by how far it
        // has crossed (posToX), with a stable per-id scatter so a clump doesn't stack.
        private static PlayerLayout JumpLayout(JumpRope.RopeData rope)
        {
            var L = new PlayerLayout();
            if (rope.Jumpers == null) return L;
            int bridgeLen = Mathf.Max(1, rope.BridgeLen);
            const float startX = 154f, finishX = 1101f, span = 947f;
            foreach (var jv in rope.Jumpers)
            {
                uint seed = Hash(jv.Id);
                float jx = ((seed % 7u) - 3f) * 4f;
                float x = jv.Crossed
                    ? finishX + 22f + ((seed % 5u) / 5f) * (1280f - finishX - 44f) // fanned out on the safe platform
                    : startX + (Mathf.Clamp(jv.Pos, 0, bridgeLen) / (float)bridgeLen) * span + jx;
                float y = 360f + (((seed >> 3) % 7u) - 3f) * 5f;
                L.Pos[jv.Id] = new Vector2(x, y);
                L.Facing[jv.Id] = 0f; // all cross toward the right
                if (jv.Airborne && !jv.Crossed) L.Airborne.Add(jv.Id); // mid-jump → lift the player
            }
            return L;
        }

        // Pick the duel to stage front-and-centre. Prefer the LOCAL player's own live
        // duel: otherwise a player whose duel isn't first on the list sees a stranger's
        // face-off on stage while their OWN throw controls sit in the HUD — it reads as
        // "not my turn" even though it is (the bug this fixes). Falls back to the first
        // live 1v1, then any live duel, then the first duel.
        private static RpsMinusOne.DuelView FocalDuel(RpsMinusOne.RpsData rps, string localId)
        {
            if (rps?.Duels == null || rps.Duels.Count == 0) return null;
            if (!string.IsNullOrEmpty(localId))
                foreach (var d in rps.Duels)
                    if (d.Status != "done" && (d.A == localId || d.B == localId)) return d;
            foreach (var d in rps.Duels) if (d.Status != "done" && !string.IsNullOrEmpty(d.B)) return d; // a real 1v1, not a bye
            foreach (var d in rps.Duels) if (d.Status != "done") return d;
            return rps.Duels[0];
        }

        // Show only the focal duel (the local player's own when they're in one): A bottom,
        // B top — everyone else hidden, so the preview reads as a clean face-off.
        private PlayerLayout RpsLayout(RpsMinusOne.RpsData rps)
        {
            var L = new PlayerLayout { HideUnlisted = true };
            var focal = FocalDuel(rps, LocalId());
            if (focal == null) return L;
            if (!string.IsNullOrEmpty(focal.A)) { L.Pos[focal.A] = new Vector2(640f, 533f); L.Facing[focal.A] = -Mathf.PI / 2f; }
            if (!string.IsNullOrEmpty(focal.B)) { L.Pos[focal.B] = new Vector2(640f, 187f); L.Facing[focal.B] = Mathf.PI / 2f; }
            return L;
        }

        // Glass crossing — faithful to the web: ONLY the active player is out on the bridge, standing
        // on the most-recently-cleared SAFE stone (squarely on a pane), about to pick the next row.
        // When they pick correctly the frontier advances and they hop onto the new safe pane; a wrong
        // pick drops them through the shattered glass (FallDeath). Everyone else waits in line on the
        // near platform. (The old version scattered "settled" players across mid-bridge stones and put
        // the guesser at y=360 between the two lanes — which is exactly the "floating, walked over a
        // hole" wrongness. One player on the bridge, always on a real pane, removes all of that.)
        private PlayerLayout GlassLayout(Snapshot snap, GlassBridge.GlassData glass, float dt)
        {
            var L = new PlayerLayout();
            foreach (var a in snap.Actors) if (!a.Alive) L.FallDeath.Add(a.Id); // shattered → plummet

            int rows = Mathf.Max(1, glass.Rows);
            int frontier = Mathf.Clamp(glass.Frontier, 0, rows);
            var rev = glass.RevealedSides;

            float StoneX(int r) => Mathf.Lerp(300f, 1030f, (Mathf.Clamp(r, 0, rows - 1) + 0.5f) / rows);
            float LaneY(int side) => 360f + (side == 0 ? -85f : 85f);       // side 0 = top lane, 1 = bottom
            int SafeOf(int r) => (rev != null && r >= 0 && r < rev.Count && rev[r] >= 0) ? rev[r] : 0;

            // The active player stands on the last cleared safe stone (or on the near platform if the
            // bridge hasn't been started). Hopping from stone frontier-1 → frontier reads as the
            // zig-zag step onto the pane they just chose.
            Vector2 activePos = frontier <= 0
                ? new Vector2(250f, 360f)
                : new Vector2(StoneX(frontier - 1), LaneY(SafeOf(frontier - 1)));

            // Waiting players queue on the near platform (tidy columns, facing the bridge).
            Vector2 QueueSlot(int rank)
            {
                int col = rank / 4, row = rank % 4;
                return new Vector2(Mathf.Min(70f + col * 56f, 248f), Mathf.Clamp(360f + (row - 1.5f) * 84f, 110f, 610f));
            }

            int wait = 0;
            foreach (var a in snap.Actors.OrderBy(x => x.Id, System.StringComparer.Ordinal))
            {
                if (!a.Alive) continue;                                      // falling → FallDeath
                if (a.Id == glass.ActiveId) { L.Pos[a.Id] = activePos; }     // the one crossing
                else { L.Pos[a.Id] = QueueSlot(wait++); }                    // the line, waiting their turn
                L.Facing[a.Id] = 0f;
            }
            return L;
        }

        // Disguised hiders render AS their toy prop, so hide the player itself.
        private static PlayerLayout PropHuntLayout(Snapshot snap)
        {
            var L = new PlayerLayout();
            foreach (var a in snap.Actors)
                if (a.Alive && !a.It && !string.IsNullOrEmpty(a.Carrying)) L.Hide.Add(a.Id);
            return L;
        }

        private static uint Hash(string s)
        {
            uint h = 2166136261u;
            if (s != null) foreach (char c in s) { h ^= c; h *= 16777619u; }
            return h;
        }

        private static string FindCharId(Snapshot snap, string id)
        {
            if (snap?.Actors != null && id != null)
                foreach (var a in snap.Actors) if (a.Id == id) return a.CharacterId;
            return null;
        }

        // Keep a character's hue but guarantee a readable, saturated colour — pale/near-white
        // characters (cow #eceff1, sheep #f5f0e6, koala #b0bec5) otherwise read as plain white.
        private static Color Vivid(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(h, Mathf.Max(s, 0.7f), Mathf.Clamp(v, 0.5f, 0.82f));
        }

        // ── Prop builders ────────────────────────────────────────────────
        // Caller-doll tuning. The real "Squid Game Doll" FBX (Resources/Models/doll) is used. FitDoll
        // AUTO-STANDS it upright and AUTO-SCALES it to DollHeight whatever the FBX's native size/axis,
        // and it keeps the FBX's own material colours. If she still looks wrong in the editor, nudge:
        //   DollPitch  — set 180 if she ends up UPSIDE-DOWN (auto-stand picked the wrong end).
        //   DollYaw    — 0/90/180/270 to turn her to FACE the crowd.
        //   DollHeight — overall size in world units (~2.3u = one blob).
        // Or set UseDollModel=false to fall back to the clean procedural doll.
        private const bool UseDollModel = true;
        private const float DollHeight = 4.6f; // world units tall — a looming caller (~2 blobs)
        private const float DollPitch = 0f;    // X° — flip to 180 if she stands upside-down
        private const float DollYaw = 180f;    // Y° — turn to face the crowd
        private float _dollFootY;              // model's foot height vs its root, after fitting

        // The caller doll. Uses the imported model at Resources/Models/doll if present —
        // auto-scaled to DollHeight and stood on the floor, so any FBX (whatever its
        // native units) just works — else falls back to the procedural doll. `alert`
        // glows it red (Red-Light) / icy (Simon freeze). dx/dy is its logical ground spot.
        private void Doll(float dx, float dy, bool alert)
        {
            if (UseDollModel && !_dollLoaded)
            {
                _dollLoaded = true;
                var prefab = Resources.Load<GameObject>("Models/doll");
                if (prefab != null)
                {
                    _doll = Instantiate(prefab, transform);
                    _doll.name = "CallerDoll";
                    _dollRenderers = _doll.GetComponentsInChildren<Renderer>(true);
                    _dollMpb = new MaterialPropertyBlock();
                    FitDoll(); // auto-stands the figure, scales to DollHeight, records the foot offset
                }
            }
            if (_doll == null) { DrawDoll(dx, dy, alert); return; } // procedural fallback until the model is imported

            _dollUsed = true;
            if (!_doll.activeSelf) _doll.SetActive(true);
            _doll.transform.position = LogicalSpace.ToWorld(dx, dy) - new Vector3(0f, _dollFootY, 0f); // feet on the floor
            // Do NOT overwrite the model's own material colours — the old code forced _Color = white,
            // which is exactly why she rendered ALL-WHITE. On an alert we only add an emissive GLOW
            // (red on RED-light / Simon-freeze); her real colours stay intact.
            if (_dollRenderers != null)
            {
                Color glow = alert ? new Color(0.7f, 0.06f, 0.10f) : Color.black;
                foreach (var r in _dollRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_dollMpb);
                    _dollMpb.SetColor("_EmissionColor", glow);
                    r.SetPropertyBlock(_dollMpb);
                }
            }
        }

        // Stand the model upright, scale it to DollHeight, and record its foot height — robust to ANY
        // FBX size/axis. Measures from SHARED-MESH bounds (always valid; a skinned mesh's world bounds
        // are stale right after Instantiate, which is why she came in giant), auto-rotates the longest
        // native axis to world-up (handles Z-up exports lying on their back), then applies the manual
        // DollPitch/DollYaw on top for fine-tuning facing / a head-up-vs-down flip.
        private void FitDoll()
        {
            if (_dollRenderers == null || _dollRenderers.Length == 0) return;
            _doll.transform.localScale = Vector3.one;
            _doll.transform.localRotation = Quaternion.identity;

            Bounds nat = DollMeshBounds(_doll.transform.worldToLocalMatrix); // native size, doll-local frame
            Vector3 s = nat.size;
            Quaternion stand =
                (s.y >= s.x && s.y >= s.z) ? Quaternion.identity                  // already Y-up
              : (s.z >= s.x && s.z >= s.y) ? Quaternion.Euler(90f, 0f, 0f)        // Z-up (Blender/Maya) → Y-up
              :                              Quaternion.Euler(0f, 0f, 90f);       // X-up → Y-up
            _doll.transform.localRotation = stand * Quaternion.Euler(DollPitch, DollYaw, 0f);

            Bounds w = DollMeshBounds(Matrix4x4.identity); // world bounds now (root is at identity-ish parent)
            float h = Mathf.Max(0.001f, w.size.y);
            _doll.transform.localScale = Vector3.one * (DollHeight / h);

            w = DollMeshBounds(Matrix4x4.identity);
            _dollFootY = w.min.y - _doll.transform.position.y;
        }

        // Combined AABB of every doll mesh, each transformed by <paramref name="toFrame"/> × the
        // renderer's localToWorld. Uses sharedMesh.bounds (valid even before a skinned mesh renders).
        private Bounds DollMeshBounds(Matrix4x4 toFrame)
        {
            bool has = false; Bounds acc = default;
            foreach (var r in _dollRenderers)
            {
                if (r == null) continue;
                Mesh m = (r is SkinnedMeshRenderer smr) ? smr.sharedMesh
                       : (r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null);
                if (m == null) continue;
                Bounds mb = m.bounds; Vector3 c = mb.center, e = mb.extents;
                Matrix4x4 mtx = toFrame * r.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                    var p = mtx.MultiplyPoint3x4(corner);
                    if (!has) { acc = new Bounds(p, Vector3.zero); has = true; } else acc.Encapsulate(p);
                }
            }
            return acc;
        }

        // The procedural Red-Light-Green-Light caller — "Younghee", the Squid Game doll:
        // orange pinafore over a mustard long-sleeve shirt, white socks + dark Mary-Janes,
        // a pale round head with a bowl-cut fringe and two ribboned pigtails, and eyes that
        // glare red while she's scanning (RED light / FREEZE). Reused as Simon's caller when
        // the imported FBX isn't set up. dx/dy is the ground spot; every part sits above the
        // ground-decal cutoff so the whole doll renders in FRONT of the crowd as one figure.
        private void DrawDoll(float dx, float dy, bool red)
        {
            var dress  = new Color(0.96f, 0.55f, 0.20f);   // orange pinafore
            var shirt  = new Color(0.96f, 0.82f, 0.34f);   // mustard long-sleeve shirt
            var skin   = new Color(1f, 0.86f, 0.70f);      // pale skin
            var hair   = new Color(0.18f, 0.12f, 0.10f);   // near-black hair
            var sock   = new Color(0.94f, 0.93f, 0.90f);   // white socks
            var shoe   = new Color(0.17f, 0.11f, 0.10f);   // dark shoes
            var ribbon = new Color(0.86f, 0.18f, 0.30f);   // red pigtail ribbons
            Color eye  = red ? new Color(1f, 0.10f, 0.22f) : new Color(0.12f, 0.09f, 0.16f);

            // Legs + shoes at the base (feet planted on the floor).
            Box(dx - 12f, dy + 10f, 12f, 12f, sock, 0.34f, 0.20f);  // left leg
            Box(dx + 12f, dy + 10f, 12f, 12f, sock, 0.34f, 0.20f);  // right leg
            Box(dx - 12f, dy + 17f, 17f, 15f, shoe, 0.14f, 0.16f);  // left shoe
            Box(dx + 12f, dy + 17f, 17f, 15f, shoe, 0.14f, 0.16f);  // right shoe

            // Mustard shirt: torso + long sleeves + little hands.
            Box(dx, dy, 40f, 28f, shirt, 0.42f, 0.80f);             // torso
            Box(dx - 27f, dy, 13f, 15f, shirt, 0.34f, 0.76f);       // left sleeve
            Box(dx + 27f, dy, 13f, 15f, shirt, 0.34f, 0.76f);       // right sleeve
            Ball(dx - 34f, dy, 7f, skin, 0.76f);                    // left hand
            Ball(dx + 34f, dy, 7f, skin, 0.76f);                    // right hand

            // Orange pinafore over the shirt: wide skirt + a bib panel up the chest.
            Box(dx, dy + 2f, 66f, 42f, dress, 0.56f, 0.46f);        // skirt (wide, low)
            Box(dx, dy, 34f, 24f, dress, 0.30f, 0.96f);             // bib over the shirt

            // Big round head.
            Ball(dx, dy, 31f, skin, 1.46f);

            // Hair: top/back mass, bowl-cut fringe across the brow, two ribboned pigtails.
            Ball(dx, dy - 9f, 32f, hair, 1.62f);                    // mass behind/over the top
            Box(dx, dy + 17f, 52f, 9f, hair, 0.12f, 1.56f);         // bowl-cut fringe in front
            Ball(dx - 31f, dy + 2f, 12f, hair, 1.34f);              // left pigtail
            Ball(dx + 31f, dy + 2f, 12f, hair, 1.34f);              // right pigtail
            Ball(dx - 31f, dy + 9f, 5f, ribbon, 1.46f);             // left ribbon
            Ball(dx + 31f, dy + 9f, 5f, ribbon, 1.46f);             // right ribbon

            // Face: eyes (red glare while scanning) + a small mouth.
            Ball(dx - 10f, dy + 14f, red ? 6f : 4.5f, eye, 1.50f);
            Ball(dx + 10f, dy + 14f, red ? 6f : 4.5f, eye, 1.50f);
            Box(dx, dy + 23f, 9f, 4f, new Color(0.82f, 0.34f, 0.32f), 0.06f, 1.44f); // mouth
        }

        // A rope as an arc of overlapping beads: anchored at both banks (endY) and bowing
        // up to midY in the centre — reads as a sweeping rope rather than a solid bar.
        private void DrawRope(float ax, float bx, float ly, float endY, float midY, Color c)
        {
            const int n = 46;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float wy = endY + (midY - endY) * Mathf.Sin(t * Mathf.PI);
                Ball(Mathf.Lerp(ax, bx, t), ly, 13f, c, wy);
            }
        }

        // A thick crescent (C-shaped) boomerang floated over the players, spun by the
        // rang's Spin and tinted to the thrower (per web drawRang). `mine` adds a bright
        // white halo so the local player's rang pops out of the volley.
        private void DrawBoomerang(float x, float y, float spin, bool big, Color wood, bool mine)
        {
            float r = big ? 26f : 17f;          // outer radius, matching the web rang
            float mid = r * 0.775f;             // centre of the ring band (web sweeps r → r*0.55)
            float thick = r * 0.27f;            // ball radius gives the band its thickness
            Disc(x, y, big ? 30f : 21f, new Color(0.227f, 0.122f, 0.051f, 0.6f), 0.05f); // ground shadow stays on the floor
            if (mine) Disc(x, y, big ? 34f : 24f, new Color(1f, 1f, 1f, 0.85f), 1.05f);   // "this one is yours" halo under the rang
            // Web sweeps the crescent from 0.15π to 1.15π; lay overlapping balls along
            // that arc so it reads as a curved boomerang, not a cross of planks.
            const int n = 12;
            for (int i = 0; i <= n; i++)
            {
                float a = spin + Mathf.PI * (0.15f + i / (float)n);
                Ball(x + Mathf.Cos(a) * mid, y + Mathf.Sin(a) * mid, thick, wood, 1.2f);
            }
        }

        // A real chair: seat pad + backrest + four legs. Mint when claimed, tan when
        // empty; a grab-me ring under empty chairs during the scramble.
        private void DrawChair(float x, float y, bool claimed, bool scramble)
        {
            // Saturated wood (was a pale tan that washed to white) / mint when claimed,
            // with darker legs + a TALL upright backrest so it reads as a chair, not a pad.
            Color seat = claimed ? new Color(0.36f, 0.84f, 0.60f) : new Color(0.55f, 0.37f, 0.19f);
            Color dark = claimed ? new Color(0.20f, 0.52f, 0.37f) : new Color(0.34f, 0.22f, 0.10f);
            if (scramble && !claimed) Disc(x, y, 48f, new Color(1f, 0.84f, 0.31f, 0.65f), 0.02f); // grab-me ring
            Box(x - 16f, y + 15f, 7f, 7f, dark, 0.24f, 0.12f);  // four legs
            Box(x + 16f, y + 15f, 7f, 7f, dark, 0.24f, 0.12f);
            Box(x - 16f, y - 6f, 7f, 7f, dark, 0.24f, 0.12f);
            Box(x + 16f, y - 6f, 7f, 7f, dark, 0.24f, 0.12f);
            Box(x, y + 4f, 44f, 38f, seat, 0.12f, 0.28f);       // seat slab
            Box(x, y - 18f, 8f, 8f, dark, 0.70f, 0.30f);        // backrest post (tall, at the back edge)
            Box(x + 14f, y - 18f, 8f, 8f, dark, 0.70f, 0.30f);
            Box(x - 14f, y - 18f, 8f, 8f, dark, 0.70f, 0.30f);
            Box(x, y - 18f, 48f, 11f, seat, 0.14f, 0.84f);      // top rail (wood, up high)
            if (claimed) Ball(x, y + 4f, 9f, new Color(0.6f, 1f, 0.82f), 0.7f); // ✅ claimed badge
        }

        // Children's-playground toys keyed to the six PropHunt disguise kinds, so a
        // hider and the decoys sharing its kind are visually identical.
        private void DrawToy(string kind, float x, float y)
        {
            switch (kind)
            {
                case "rock": // beach ball
                    Ball(x, y, 24f, new Color(1f, 0.322f, 0.322f), 0.55f);
                    Ball(x - 5f, y - 4f, 9f, new Color(1f, 1f, 1f, 0.9f), 0.74f);
                    break;
                case "crate": // alphabet block
                    Box(x, y, 42f, 42f, new Color(1f, 0.792f, 0.157f), 0.6f, 0.3f);
                    Box(x, y, 18f, 18f, new Color(0.937f, 0.325f, 0.314f), 0.62f, 0.62f);
                    break;
                case "barrel": // ring-stack
                    Box(x, y, 40f, 14f, new Color(0.149f, 0.776f, 0.855f), 0.16f, 0.12f);
                    Box(x, y, 30f, 12f, new Color(0.4f, 0.733f, 0.416f), 0.30f, 0.30f);
                    Box(x, y, 20f, 10f, new Color(1f, 0.655f, 0.149f), 0.44f, 0.50f);
                    Ball(x, y, 8f, new Color(0.937f, 0.325f, 0.314f), 0.66f);
                    break;
                case "jar": // balloon
                    Ball(x, y - 6f, 20f, new Color(0.925f, 0.251f, 0.478f), 1.1f);
                    Ball(x - 6f, y - 12f, 6f, new Color(1f, 1f, 1f, 0.9f), 1.2f);
                    Ball(x, y + 2f, 4f, new Color(0.925f, 0.251f, 0.478f), 0.5f);
                    break;
                case "bush": // rubber duck
                    Ball(x, y, 22f, new Color(1f, 0.933f, 0.345f), 0.5f);
                    Ball(x + 10f, y - 12f, 12f, new Color(1f, 0.933f, 0.345f), 0.85f);
                    Box(x + 22f, y - 12f, 12f, 7f, new Color(1f, 0.627f, 0f), 0.85f, 0.85f);
                    Ball(x + 12f, y - 16f, 3f, new Color(0.13f, 0.13f, 0.13f), 0.95f);
                    break;
                case "stool": // toy stool
                    Box(x, y, 44f, 18f, new Color(0.671f, 0.278f, 0.737f), 0.10f, 0.30f);
                    Box(x - 14f, y + 7f, 8f, 8f, new Color(0.482f, 0.122f, 0.635f), 0.28f, 0.14f);
                    Box(x + 14f, y + 7f, 8f, 8f, new Color(0.482f, 0.122f, 0.635f), 0.28f, 0.14f);
                    break;
                default:
                    Box(x, y, 40f, 40f, new Color(0.40f, 0.30f, 0.22f), 0.5f, 0.25f);
                    break;
            }
        }

        // RPS reveal: the two throws for one fighter (centred at x=640, given y), or just the kept
        // throw blown up on resolve. Nothing during the hidden PICK phase.
        private void DrawRpsThrows(float y, System.Collections.Generic.List<string> throws, string keep, string phase)
        {
            if (phase == "resolve" && !string.IsNullOrEmpty(keep)) { DrawRpsHand(640f, y, keep, true); return; }
            if (throws == null || throws.Count == 0) return;
            for (int i = 0; i < throws.Count; i++)
                DrawRpsHand(640f - (throws.Count - 1) * 30f + i * 60f, y, throws[i], false);
        }

        // A rock/paper/scissors hand icon: rock = a fist orb, paper = a flat slab, scissors = a V.
        private void DrawRpsHand(float x, float y, string t, bool kept)
        {
            Color c = kept ? new Color(1f, 0.86f, 0.2f) : new Color(0.92f, 0.94f, 1f);
            float s = kept ? 1.35f : 1f; const float wy = 0.95f;
            Disc(x, y, 22f * s, new Color(0f, 0f, 0f, 0.25f), 0.03f); // little shadow so it reads as a token
            switch (t)
            {
                case "R": Ball(x, y, 16f * s, c, wy); break;
                case "P": Box(x, y, 32f * s, 22f * s, c, 0.12f, wy); break;
                case "S":
                    Bar(x - 12f * s, y + 11f * s, x + 7f * s, y - 12f * s, 6f * s, c, 0.12f, wy);
                    Bar(x + 12f * s, y + 11f * s, x - 7f * s, y - 12f * s, 6f * s, c, 0.12f, wy);
                    break;
            }
        }

        // Seeker's blade: blade + guard + hilt, held out to the actor's right.
        private void DrawSword(float x, float y)
        {
            Box(x + 28f, y, 9f, 46f, new Color(0.85f, 0.86f, 0.92f), 0.1f, 0.5f);        // blade
            Box(x + 28f, y + 14f, 16f, 6f, new Color(0.79f, 0.63f, 0.29f), 0.1f, 0.55f); // guard
            Box(x + 28f, y + 26f, 22f, 8f, new Color(0.5f, 0.34f, 0.2f), 0.1f, 0.5f);    // hilt
        }

        /// <summary>A single rotated box laid between two logical points (a bar/rail/arm).</summary>
        private void Bar(float ax, float ay, float bx, float by, float thick, Color c, float worldH, float worldY)
        {
            float dx = bx - ax, dy = by - ay, len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f) return;
            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            Box((ax + bx) * 0.5f, (ay + by) * 0.5f, len, thick, c, worldH, worldY, ang);
        }

        // A real ladder: two parallel wooden rails with rungs between them.
        private void DrawLadder(Vector2 a, Vector2 b)
        {
            float dx = b.x - a.x, dy = b.y - a.y, len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1f) return;
            float nx = -dy / len * 13f, ny = dx / len * 13f;
            var rail = new Color(0.851f, 0.627f, 0.357f);
            var rung = new Color(0.725f, 0.498f, 0.243f);
            Bar(a.x + nx, a.y + ny, b.x + nx, b.y + ny, 6f, rail, 0.05f, 0.06f);
            Bar(a.x - nx, a.y - ny, b.x - nx, b.y - ny, 6f, rail, 0.05f, 0.06f);
            int n = Mathf.Clamp(Mathf.RoundToInt(len / 24f), 2, 14);
            for (int i = 1; i < n; i++)
            {
                float t = i / (float)n, cx = a.x + dx * t, cy = a.y + dy * t;
                Bar(cx + nx, cy + ny, cx - nx, cy - ny, 4f, rung, 0.045f, 0.058f);
            }
        }

        // A chute fork: a hazard-tinted square with two slide arms diverging to a pair
        // of outcome badges (unknown / back-to-start / abyss), per the web's drawChuteFork.
        // A chute forks from its square to the two NEIGHBOURING squares: a 50/50 gamble where
        // one side sends you back to the start and the other drops you into the abyss. Drawn to
        // the real neighbour cells, so it spans adjacent pieces and can never sit off the board.
        private void DrawChute(Vector2 from, Vector2 left, Vector2 right, int lo, int ro)
        {
            Disc(from.x, from.y, 30f, new Color(0.69f, 0.42f, 0.90f, 0.18f), 0.02f); // hazard mouth on the chute square
            Bar(from.x, from.y, left.x, left.y, 11f, ChuteColor(lo), 0.06f, 0.06f);   // slide to the left neighbour
            Bar(from.x, from.y, right.x, right.y, 11f, ChuteColor(ro), 0.06f, 0.06f); // slide to the right neighbour
            Ball(from.x, from.y, 10f, new Color(0.494f, 0.247f, 0.722f), 0.07f);      // fork node
            Ball(left.x, left.y, 15f, ChuteColor(lo), 0.07f);                         // left outcome (on its square)
            Ball(right.x, right.y, 15f, ChuteColor(ro), 0.07f);                       // right outcome
            Ball(left.x, left.y, 6f, new Color(1f, 1f, 1f, 0.9f), 0.10f);             // pips (icon stand-ins)
            Ball(right.x, right.y, 6f, new Color(1f, 1f, 1f, 0.9f), 0.10f);
        }

        private static Color ChuteColor(int outcome)
            => outcome == 1 ? new Color(1f, 0.302f, 0.427f)      // abyss #ff4d6d
             : outcome == 0 ? new Color(0.149f, 0.776f, 0.855f)  // back-to-start #26c6da
             : new Color(0.69f, 0.42f, 0.90f);                   // unknown #b06be6

        private static Texture2D LoadFloorTexture(string theme)
        {
            var tex = Resources.Load<Texture2D>("Art/floor_" + theme);
            if (tex != null) tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        /// <summary>Convert a screen point to logical arena coordinates (for aim).</summary>
        public bool TryScreenToLogical(Vector2 screen, out Vec2 logical)
        {
            logical = default;
            if (_camera == null) return false;
            var ray = _camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float enter))
            {
                logical = LogicalSpace.ToLogical(ray.GetPoint(enter));
                return true;
            }
            return false;
        }
    }
}
