using System.Collections.Generic;
using UnityEngine;

namespace Eliminated.Game.View
{
    /// <summary>
    /// Builds a full-character menu thumbnail by reading every sprite part off the
    /// art prefab (texture + UV + tint + placement), so the picker shows the WHOLE
    /// assembled character — face included — not just the head. Each part's rect is
    /// computed from its sprite bounds through its transform, normalized into the
    /// character's overall bounds, and the parts are ordered back-to-front by world
    /// z (the face has the smallest z, so it draws last/on top). No render-to-texture.
    /// Characters with no art prefab return <see cref="Thumb.Has"/> = false. Cached.
    /// </summary>
    public static class CharacterPreview
    {
        public struct Part
        {
            public Texture2D Tex; // sprite sheet
            public Rect Uv;       // normalized sub-rect of the part's sprite
            public Color Tint;    // the SpriteRenderer tint (recolors)
            public Rect Norm;     // 0..1 placement within the character bounds (Y up)
        }

        public struct Thumb
        {
            public RenderTexture Rt; // real prefab render (preferred); when set, Parts is unused
            public Part[] Parts;     // hand-composite fallback, back-to-front draw order
            public float Aspect;     // width / height of the whole character
            public bool Flip;        // (composite only) mirror so it faces the same way as the animals
            public Rect HeadRect;    // the head shape's box (Norm, Y-up) — anchors hats, collars and the flower
            public Rect FaceRect;    // the facial-features box (Norm, Y-up) — anchors glasses to the real eyes
            public bool Has;
        }

        // The slime + Jovial-human prefabs face the opposite way from the MiMU animals,
        // so mirror them in the preview to keep the roster facing one direction.
        private static readonly HashSet<string> FlipIds =
            new HashSet<string> { "slime", "ninja", "rogue", "sorcerer", "clown" };

        private static readonly Dictionary<string, Thumb> _cache = new Dictionary<string, Thumb>();

        public static Thumb Get(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return default;
            if (_cache.TryGetValue(characterId, out var t)) return t;
            t = Build(characterId);
            _cache[characterId] = t;
            return t;
        }

        // Back-to-front rank by part name (limbs behind body behind head behind face).
        private static int NameRank(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("tail")) return -30;
            if (n.Contains("leg") || n.Contains("foot")) return -20;
            if (n.Contains("arm")) return -10;
            if (n.Contains("body")) return 0;
            if (n.Contains("head")) return 10;
            if (n.Contains("face")) return 20;
            return 5;
        }

        private static Thumb Build(string id)
        {
            var prefab = CharacterArt.Load(id);
            if (prefab == null) return default;

            var rends = prefab.GetComponentsInChildren<SpriteRenderer>(true);
            var list = new List<(SpriteRenderer sr, Vector2 min, Vector2 max, float z)>();
            bool hasB = false;
            Vector2 bmin = Vector2.zero, bmax = Vector2.zero;

            foreach (var sr in rends)
            {
                if (sr.sprite == null) continue;
                var pn = sr.gameObject.name.ToLowerInvariant();
                if (pn.Contains("shadow") || pn.Contains("weapon")) continue; // drop cast-shadow + held weapon

                var bnd = sr.sprite.bounds;
                var tr = sr.transform;
                Vector3 wc = tr.TransformPoint(bnd.center);                 // world center (handles the hierarchy)
                var ls = tr.lossyScale;
                Vector2 half = new Vector2(bnd.extents.x * Mathf.Abs(ls.x), bnd.extents.y * Mathf.Abs(ls.y));
                Vector2 mn = new Vector2(wc.x - half.x, wc.y - half.y);
                Vector2 mx = new Vector2(wc.x + half.x, wc.y + half.y);

                list.Add((sr, mn, mx, wc.z));
                if (!hasB) { bmin = mn; bmax = mx; hasB = true; }
                else { bmin = Vector2.Min(bmin, mn); bmax = Vector2.Max(bmax, mx); }
            }
            if (!hasB) return default;

            Vector2 size = bmax - bmin;
            if (size.x <= 1e-4f || size.y <= 1e-4f) return default;

            // Back-to-front: lower sortingOrder first (the Jovial parts layer this way);
            // ties break on world z, largest first (the MiMU rig is all sortingOrder 0 and
            // layers by z — face has the smallest z, so it draws last/on top).
            list.Sort((a, b) =>
            {
                int c = a.sr.sortingOrder.CompareTo(b.sr.sortingOrder);
                if (c != 0) return c;
                int zc = b.z.CompareTo(a.z);  // larger world z = further back (handles the MiMU rig)
                if (zc != 0) return zc;
                // Fallback for prefabs whose parts report sortingOrder 0 AND z 0 on the
                // asset (the Jovial humans): order by part name so body sits behind head.
                return NameRank(a.sr.gameObject.name).CompareTo(NameRank(b.sr.gameObject.name));
            });

            var parts = new Part[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                var sp = e.sr.sprite;
                var tx = sp.texture;
                // The placement rect below is derived from sp.bounds, which (for tight-mesh
                // sprites) hugs the OPAQUE art, not the full slice cell. Build the UV from the
                // SAME tight region so the quad is textured with exactly the pixels it covers —
                // otherwise a sprite whose art fills only a fraction of its cell (the cow's tall
                // Face cell, its offset Tail) squashes the whole transparent cell into the tight
                // quad and the art lands mangled/displaced. The cell's pivot maps to texture pixel
                // (rect.x + pivot.x*rect.w, rect.y + pivot.y*rect.h); bounds (in local units) offset
                // from there by pixelsPerUnit. For art that fills its cell (every working animal)
                // this equals the old full-cell rect within sub-pixel tolerance, so they are
                // unchanged; only under-filled cells (the cow) are corrected.
                float ppu = sp.pixelsPerUnit;
                var cell = sp.rect; var bc = sp.bounds.center; var be = sp.bounds.extents;
                float pxCx = cell.x + sp.pivot.x + bc.x * ppu;
                float pxCy = cell.y + sp.pivot.y + bc.y * ppu;
                var r = new Rect(pxCx - be.x * ppu, pxCy - be.y * ppu, be.x * 2f * ppu, be.y * 2f * ppu);
                parts[i] = new Part
                {
                    Tex = tx,
                    Uv = new Rect(r.x / tx.width, r.y / tx.height, r.width / tx.width, r.height / tx.height),
                    Tint = e.sr.color,
                    Norm = new Rect((e.min.x - bmin.x) / size.x, (e.min.y - bmin.y) / size.y,
                                    (e.max.x - e.min.x) / size.x, (e.max.y - e.min.y) / size.y),
                };
            }
            // Boxes (Norm, Y-up) for anchoring worn accessories per character. "Head" is the
            // whole head shape (ears/horns/hat included) — anchors hats (crown), collars (neck)
            // and the side flower. "Face" is the facial-features sprite — anchors glasses, which
            // must sit on the real eyes, not the ear-inflated head box. Fall back sensibly when a
            // rig lacks a part (the single-sprite slime, or the humanoids that have no Face).
            Rect headRect = new Rect(0.15f, 0.45f, 0.70f, 0.50f);
            int hi = -1, fi = -1;
            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i].sr.gameObject.name.ToLowerInvariant();
                if (hi < 0 && n.Contains("head")) hi = i;
                if (fi < 0 && n.Contains("face")) fi = i;
            }
            if (hi < 0) hi = fi;                 // no "head" part → use "face" as the head box too
            if (hi >= 0) headRect = parts[hi].Norm;
            Rect faceRect = fi >= 0 ? parts[fi].Norm : headRect; // glasses anchor; else reuse the head box

            // Force the wizard/pirate to use their ORIGINAL pack head sprite (loaded
            // straight from Resources) by overriding the head part — so the result is right
            // even if Unity never re-imported the prefab after the head swap. The full-canvas
            // pirhead is drawn into the prefab head's slot, so pirhead.png must keep the same
            // canvas footprint as the prefab head. (sorcerer = wizard, ninja = pirate.)
            if ((id == "sorcerer" || id == "ninja") && hi >= 0)
            {
                var oh = Resources.Load<Sprite>(id == "sorcerer" ? "Chars/wizhead" : "Chars/pirhead");
                if (oh != null && oh.texture != null)
                    parts[hi] = new Part { Tex = oh.texture, Uv = new Rect(0f, 0f, 1f, 1f),
                                           Tint = parts[hi].Tint, Norm = parts[hi].Norm };
            }

            return new Thumb { Parts = parts, Aspect = size.x / size.y, Flip = FlipIds.Contains(id), HeadRect = headRect, FaceRect = faceRect, Has = true };
        }
    }
}
