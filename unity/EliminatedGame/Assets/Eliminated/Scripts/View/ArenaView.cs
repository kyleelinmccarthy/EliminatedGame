using System.Collections.Generic;
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
    /// snapshot: a pooled <see cref="BlobView"/> per actor, plus lightweight
    /// game-specific props (boomerang rangs and pickups). All created in code so
    /// the slice runs with no hand-authored scene. Per-game view modules and real
    /// arenas arrive in Phase 3.
    /// </summary>
    public sealed class ArenaView : MonoBehaviour
    {
        private ISnapshotSource _sim;
        private Camera _camera;
        private readonly Dictionary<string, BlobView> _blobs = new Dictionary<string, BlobView>();
        private readonly List<GameObject> _props = new List<GameObject>();
        private int _propsUsed;

        public Camera Camera => _camera;

        public void Init(ISnapshotSource sim)
        {
            _sim = sim;
            BuildStage();
        }

        private void BuildStage()
        {
            // Camera: orthographic, looking straight down the arena.
            var camGo = new GameObject("ArenaCamera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = LogicalSpace.WorldHalfHeight * 1.08f;
            _camera.transform.position = new Vector3(0f, 25f, 0f);
            _camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.10f, 0.11f, 0.16f);
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100f;

            // Floor.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(floor.GetComponent<Collider>());
            floor.name = "Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(
                Constants.ArenaW * LogicalSpace.Scale, Constants.ArenaH * LogicalSpace.Scale, 1f);
            var fr = floor.GetComponent<Renderer>();
            fr.sharedMaterial = ViewMaterials.Shared;
            ViewMaterials.SetColor(fr, new MaterialPropertyBlock(), new Color(0.16f, 0.18f, 0.24f));

            // Light.
            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            light.intensity = 1.1f;
        }

        private void LateUpdate()
        {
            var snap = _sim != null ? _sim.Latest : null;
            float dt = Time.deltaTime;

            if (snap?.Actors != null)
            {
                var seen = new HashSet<string>();
                foreach (var a in snap.Actors)
                {
                    if (!_blobs.TryGetValue(a.Id, out var view))
                    {
                        view = new BlobView();
                        view.Root.transform.SetParent(transform, false);
                        view.Bind(a);
                        _blobs[a.Id] = view;
                    }
                    view.Render(a, dt);
                    seen.Add(a.Id);
                }
                foreach (var kv in _blobs)
                    if (!seen.Contains(kv.Key)) kv.Value.Root.SetActive(false);
            }

            RenderGameProps(snap);
            PlayFxAudio(snap);
        }

        // Each snapshot's fx are produced once (the room drains them per tick), so
        // mapping them to SFX here fires each cue exactly once.
        private void PlayFxAudio(Snapshot snap)
        {
            if (snap?.Fx == null || AudioService.Instance == null) return;
            foreach (var fx in snap.Fx)
            {
                switch (fx.Kind)
                {
                    case EffectKind.Death: AudioService.Instance.Play("death"); break;
                    case EffectKind.Shatter: AudioService.Instance.Play("shatter"); break;
                    case EffectKind.Pickup: AudioService.Instance.Play("pickup", 0.8f); break;
                    case EffectKind.Confetti: AudioService.Instance.Play("good", 0.7f); break;
                    case EffectKind.Ring: AudioService.Instance.Play("chime", 0.6f); break;
                    case EffectKind.Freeze: AudioService.Instance.Play("catch", 0.7f); break;
                    case EffectKind.Thaw: AudioService.Instance.Play("pickup", 0.6f); break;
                    case EffectKind.Throw: AudioService.Instance.Play("throw", 0.6f); break;
                    case EffectKind.Catch: AudioService.Instance.Play("catch", 0.6f); break;
                    case EffectKind.Shove: AudioService.Instance.Play("drum", 0.7f); break;
                    case EffectKind.Burn: AudioService.Instance.Play("bad", 0.5f); break;
                    case EffectKind.Spark: AudioService.Instance.Play("blip", 0.25f); break;
                }
            }
        }

        private static readonly Color YellowRang = new Color(1f, 0.96f, 0.5f);
        private static readonly Color IslandColor = new Color(0.55f, 0.45f, 0.32f);
        private static readonly Color LavaColor = new Color(0.9f, 0.25f, 0.05f);
        private static readonly Color ChairColor = new Color(0.85f, 0.7f, 0.4f);
        private static readonly Color RoomColor = new Color(0.4f, 0.55f, 0.85f);

        private void RenderGameProps(Snapshot snap)
        {
            _propsUsed = 0;
            switch (snap?.Data)
            {
                case Boomerang.BoomData boom:
                    DrawPickups(boom.Pickups);
                    if (boom.Rangs != null)
                        foreach (var r in boom.Rangs) Disc(r.X, r.Y, r.Big ? 30f : 16f, YellowRang, 0.35f);
                    break;

                case KingOfTheHill.KothData koth:
                    if (koth.Islands != null)
                        foreach (var i in koth.Islands)
                            Disc(i.X, i.Y, i.R, i.Final ? Color.Lerp(IslandColor, Palette.Safe, 0.4f) : IslandColor, 0.02f);
                    DrawPickups(koth.Pickups);
                    break;

                case Dodgeball.DodgeData dodge:
                    Disc(dodge.Mid, 360f, 6f, Color.white, 0.02f); // centerline marker
                    if (dodge.Balls != null)
                        foreach (var b in dodge.Balls)
                            Disc(b.X, b.Y, 15f, b.State == "flight" ? Color.red : YellowRang, 0.3f);
                    DrawPickups(dodge.Pickups);
                    break;

                case KeepyUppy.KeepyData keepy:
                    if (keepy.Balloons != null)
                        foreach (var b in keepy.Balloons) Disc(b.X, b.Y, 30f, Palette.Body(b.Color), 0.4f);
                    DrawPickups(keepy.Pickups);
                    break;

                case MusicalChairs.McData mc:
                    if (mc.Chairs != null)
                        foreach (var c in mc.Chairs) Disc(c.X, c.Y, 46f, c.Claimed ? Palette.Safe : ChairColor, 0.05f);
                    DrawPickups(mc.Pickups);
                    break;

                case Mingle.MingleData mingle:
                    Disc(mingle.PlatformX, mingle.PlatformY, mingle.PlatformR, new Color(0.5f, 0.5f, 0.6f), 0.03f);
                    if (mingle.Rooms != null)
                        foreach (var r in mingle.Rooms) Disc(r.X, r.Y, r.R, r.Ok ? Palette.Safe : RoomColor, 0.02f);
                    break;

                case PropHunt.PropData prop:
                    if (prop.Decoys != null)
                        foreach (var d in prop.Decoys) Disc(d.X, d.Y, 20f, new Color(0.6f, 0.5f, 0.4f), 0.1f);
                    break;

                case Tag.TagData tag:
                    DrawPickups(tag.Pickups);
                    break;

                case RedLightGreenLight.RlglData rl:
                    Disc(rl.FinishX, 360f, 10f, rl.Red ? Palette.Danger : Palette.Safe, 0.02f);
                    break;
            }
            for (int i = _propsUsed; i < _props.Count; i++)
                if (_props[i].activeSelf) _props[i].SetActive(false);
        }

        private void DrawPickups(List<PickupView> pickups)
        {
            if (pickups == null) return;
            foreach (var p in pickups)
            {
                bool good = System.Enum.TryParse<PowerupKind>(p.Kind, out var k) && PowerupEffects.IsGood(k);
                Disc(p.X, p.Y, 18f, Palette.Powerup(good), 0.25f);
            }
        }

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
                go.GetComponent<Renderer>().sharedMaterial = ViewMaterials.Shared;
                _props.Add(go);
            }
            _propsUsed++;
            if (!go.activeSelf) go.SetActive(true);
            float d = LogicalSpace.WorldRadius(logicalRadius) * 2f;
            go.transform.localScale = new Vector3(d, 0.04f, d); // a thin disc (default cylinder is 2 units tall)
            go.transform.position = LogicalSpace.ToWorld(lx, ly) + new Vector3(0f, height, 0f);
            ViewMaterials.SetColor(go.GetComponent<Renderer>(), new MaterialPropertyBlock(), color);
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
