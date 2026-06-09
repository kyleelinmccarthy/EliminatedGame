using System.Collections.Generic;
using UnityEngine;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Eliminated.Game.Accessibility;
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
        private SimRunner _sim;
        private Camera _camera;
        private readonly Dictionary<string, BlobView> _blobs = new Dictionary<string, BlobView>();
        private readonly List<GameObject> _props = new List<GameObject>();
        private int _propsUsed;

        public Camera Camera => _camera;

        public void Init(SimRunner sim)
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
        }

        private void RenderGameProps(Snapshot snap)
        {
            _propsUsed = 0;
            if (snap?.Data is Boomerang.BoomData boom)
            {
                if (boom.Rangs != null)
                    foreach (var r in boom.Rangs)
                        PlaceProp(r.X, r.Y, r.Big ? 30f : 16f, new Color(1f, 0.96f, 0.5f), 0.35f);
                if (boom.Pickups != null)
                    foreach (var p in boom.Pickups)
                        PlaceProp(p.X, p.Y, 22f, Palette.Safe, 0.2f);
            }
            for (int i = _propsUsed; i < _props.Count; i++)
                if (_props[i].activeSelf) _props[i].SetActive(false);
        }

        private void PlaceProp(float lx, float ly, float logicalRadius, Color color, float height)
        {
            GameObject go;
            if (_propsUsed < _props.Count) go = _props[_propsUsed];
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.name = "Prop";
                go.transform.SetParent(transform, false);
                go.GetComponent<Renderer>().sharedMaterial = ViewMaterials.Shared;
                _props.Add(go);
            }
            _propsUsed++;
            if (!go.activeSelf) go.SetActive(true);
            float d = LogicalSpace.WorldRadius(logicalRadius) * 2f;
            go.transform.localScale = Vector3.one * d;
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
