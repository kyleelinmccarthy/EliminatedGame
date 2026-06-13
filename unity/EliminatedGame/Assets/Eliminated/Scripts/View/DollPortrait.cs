using UnityEngine;

namespace Eliminated.Game.View
{
    /// <summary>
    /// Renders the real "Squid Game Doll" FBX (Resources/Models/doll) to a small TRANSPARENT
    /// RenderTexture so the actual 3D model can be drawn flat in 2D UI — specifically the
    /// Chutes &amp; Ladders finish square, which is an IMGUI board where a live 3D object can't
    /// otherwise appear.
    ///
    /// Lazily created on first access. Fully defensive: if the model is missing or anything in
    /// the offscreen-render setup fails, <see cref="Texture"/> returns null and callers fall back
    /// to a procedural drawing — so this can only ever ADD the real model, never break the board.
    /// </summary>
    public sealed class DollPortrait : MonoBehaviour
    {
        private static DollPortrait _inst;
        private static bool _failed;
        private RenderTexture _rt;
        private Camera _cam;

        // The portrait lives on a normally-unused layer so the live arena camera never sees it and
        // the portrait camera renders ONLY the doll.
        private const int PortraitLayer = 31;

        /// <summary>The rendered doll texture, or null if unavailable (caller should fall back).</summary>
        public static Texture Texture
        {
            get
            {
                if (_failed) return null;
                if (_inst == null && Application.isPlaying)
                {
                    try { Bootstrap(); }
                    catch { _failed = true; Cleanup(); }
                }
                return _inst != null ? _inst._rt : null;
            }
        }

        private static void Cleanup()
        {
            if (_inst != null) { Destroy(_inst.gameObject); _inst = null; }
        }

        private static void Bootstrap()
        {
            var prefab = Resources.Load<GameObject>("Models/doll");
            if (prefab == null) { _failed = true; return; }

            var root = new GameObject("DollPortrait") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(root);
            root.transform.position = new Vector3(5000f, -5000f, 5000f); // far from the arena
            _inst = root.AddComponent<DollPortrait>();

            var doll = Instantiate(prefab, root.transform);
            doll.transform.localPosition = Vector3.zero;
            doll.transform.localRotation = Quaternion.identity;
            doll.transform.localScale = Vector3.one;
            SetLayer(doll, PortraitLayer);

            var rends = doll.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) { _failed = true; Cleanup(); return; }

            // Auto-stand + auto-scale from SHARED-MESH bounds (a skinned mesh's world bounds are stale
            // right after Instantiate — measuring those is what made her giant/sideways).
            Bounds nat = MeshBounds(rends, doll.transform.worldToLocalMatrix);
            Vector3 s = nat.size;
            Quaternion stand =
                (s.y >= s.x && s.y >= s.z) ? Quaternion.identity
              : (s.z >= s.x && s.z >= s.y) ? Quaternion.Euler(90f, 0f, 0f)   // Z-up → Y-up
              :                              Quaternion.Euler(0f, 0f, 90f);  // X-up → Y-up
            doll.transform.localRotation = stand * Quaternion.Euler(0f, 180f, 0f); // stand, then face the camera

            Bounds w = MeshBounds(rends, Matrix4x4.identity);
            float hgt = Mathf.Max(0.001f, w.size.y);
            doll.transform.localScale = Vector3.one * (2.4f / hgt); // ~2.4 units tall in the portrait
            w = MeshBounds(rends, Matrix4x4.identity);
            doll.transform.position += root.transform.position - w.center; // centre in frame

            var lightGo = new GameObject("DollPortraitLight");
            lightGo.transform.SetParent(root.transform, false);
            var lt = lightGo.AddComponent<Light>();
            lt.type = LightType.Directional;
            lt.transform.rotation = Quaternion.Euler(28f, -18f, 0f);
            lt.intensity = 1.2f;
            lt.cullingMask = 1 << PortraitLayer;

            var camGo = new GameObject("DollPortraitCam");
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 1.45f;
            cam.transform.position = root.transform.position + new Vector3(0f, 0f, -6f);
            cam.transform.rotation = Quaternion.identity; // look +Z toward the doll
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent
            cam.cullingMask = 1 << PortraitLayer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 40f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.enabled = false; // rendered manually
            _inst._cam = cam;

            _inst._rt = new RenderTexture(256, 320, 16, RenderTextureFormat.ARGB32) { name = "DollPortraitRT" };
            cam.targetTexture = _inst._rt;
            cam.Render();
        }

        // Re-render occasionally so the portrait stays valid if the RT is recreated by a device
        // reset; cheap (a tiny texture, every few frames).
        private void LateUpdate()
        {
            if (_cam != null && _rt != null && Time.frameCount % 8 == 0) _cam.Render();
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayer(t.gameObject, layer);
        }

        // Combined AABB of every mesh, transformed by toFrame × the renderer's localToWorld. Uses
        // sharedMesh.bounds (always valid, even before a skinned mesh first renders).
        private static Bounds MeshBounds(Renderer[] rends, Matrix4x4 toFrame)
        {
            bool has = false; Bounds acc = default;
            foreach (var r in rends)
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
    }
}
