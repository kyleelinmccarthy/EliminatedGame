using System.Collections.Generic;
using UnityEngine;

namespace Eliminated.Game.View
{
    /// <summary>
    /// Maps a sim character id to a real art prefab sourced from imported Asset
    /// Store packs and copied into <c>Resources/Chars/</c>. Returns <c>null</c> for
    /// any character without art yet, so <see cref="PlayerView"/> falls back to the
    /// procedural player — letting us drop real art in one character at a time.
    /// Licenses for the packs are recorded in <c>docs/ASSET_SOURCES.md</c>.
    /// </summary>
    public static class CharacterArt
    {
        // sim character id  ->  Resources/Chars/<prefab> name.
        // Several roster ids can share a prefab until each gets bespoke art.
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "slime", "slime" },     // Slime Crime    — Layer Lab Casual Monsters (green slime)
            { "ninja", "pirate" },    // Backstabber    — Jovial Cute Characters (pirate)
            { "rogue", "ninja" },     // Sayonara  — Jovial Cute Characters (ninja, hooded)
            { "sorcerer", "wizard" }, // Hexecutioner   — Jovial Cute Characters (wizard)
            { "clown", "clown" },     // Last Laugh     — Jovial Cute Characters (clown)

            // --- MiMU animal pack, assembled by AnimalPrefabBuilder (Tools ▸
            // Eliminated ▸ Build Animal Prefabs). Until that menu item is run these
            // resolve to nothing → player fallback. ---
            { "panther", "blackcat" }, // Purrgatory — black cat
            { "cat", "cat" },          // Meowderer  — cat (Toon)
            { "capybara", "bear" },    // Capybarely — bear (brown, round)
            { "koala", "koala" },      // Koalamity  — grey bear, tail dropped
            { "aardvark", "aardvark" },// Aard to Kill — bear body + demon head, tan
            { "fox", "fox" },          // Foxic      — cat silhouette recolored orange
            // New roster critters (Cosmetics.Characters), prefab name == id.
            { "cow", "cow" }, { "owl", "owl" }, { "snowowl", "snowowl" },
            { "demon", "demon" }, { "devil", "devil" }, { "sheep", "sheep" },

            // --- Our own generated kawaii sprites (tools/ArtGen/gen_chars.py →
            // Art/Chars/*.png, prefabs assembled by GeneratedCharPrefabBuilder).
            // Single-sprite blob-with-a-face characters, same shape as the slime.
            // Prefab name == id except the frog wizard ("wizard" is the Jovial
            // HUMAN wizard prefab, owned by sorcerer above). ---
            { "wizard", "frogwizard" }, // Hocus Croakus — frog in a wizard hat
            // food / fruit / veg
            { "avo", "avo" }, { "egg", "egg" }, { "egg2", "egg2" }, { "goldegg", "goldegg" },
            { "berry", "berry" }, { "brocc", "brocc" }, { "donut", "donut" }, { "pickle", "pickle" },
            { "tomato", "tomato" }, { "pine", "pine" }, { "shroom", "shroom" }, { "sushi", "sushi" },
            { "nana", "nana" }, { "plum", "plum" }, { "orange", "orange" }, { "blueberry", "blueberry" },
            { "carrot", "carrot" }, { "dragonfruit", "dragonfruit" }, { "melon", "melon" },
            { "onigiri", "onigiri" }, { "ghostpepper", "ghostpepper" }, { "cosmic", "cosmic" },
            // remaining critters
            { "bunny", "bunny" }, { "pig", "pig" }, { "mouse", "mouse" },
            { "hamster", "hamster" }, { "ghost", "ghost" },
        };

        private static readonly Dictionary<string, GameObject> _cache = new Dictionary<string, GameObject>();
        private static readonly HashSet<string> _missing = new HashSet<string>();

        // Gap between the eyewear's two lens centres / texture width (lenses at 64 ± 42 of 128px).
        public const float EyewearLensFrac = (2f * 42f) / 128f;

        // Eye anchors (face-box Norm, Y-up: left eye, right eye) DETECTED from each character's
        // real face sprite — so glasses fit every character's own eye placement and spacing, with
        // no hand-tuned numbers. Detected against the same texture region the preview draws into
        // the face box (matches DrawThumbInRect's Uv), so it lines up with the on-screen face.
        private static readonly Dictionary<string, (Vector2 L, Vector2 R, float rad)?> _eyeCache =
            new Dictionary<string, (Vector2 L, Vector2 R, float rad)?>();

        // eL/eR: eye centres in face-box Norm (Y-up). eyeRad: average eye radius as a fraction of
        // the face-box width — so the glasses lenses can be sized to the character's actual eyes.
        public static bool TryEyes(string id, out Vector2 eL, out Vector2 eR, out float eyeRad)
        {
            eL = eR = default; eyeRad = 0.18f;
            if (string.IsNullOrEmpty(id)) return false;
            if (!_eyeCache.TryGetValue(id, out var c)) { c = DetectEyes(id); _eyeCache[id] = c; }
            if (c == null) return false;
            eL = c.Value.L; eR = c.Value.R; eyeRad = c.Value.rad; return true;
        }

        private static (Vector2 L, Vector2 R, float rad)? DetectEyes(string id)
        {
            var prefab = Load(id);
            if (prefab == null) return null;
            SpriteRenderer faceR = null, headR = null, biggest = null;
            foreach (var sr in prefab.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.sprite == null) continue;
                var n = sr.gameObject.name.ToLowerInvariant();
                if (n.Contains("shadow") || n.Contains("weapon")) continue;
                if (faceR == null && n.Contains("face")) faceR = sr;
                if (headR == null && n.Contains("head")) headR = sr;
                if (biggest == null || sr.sprite.bounds.size.sqrMagnitude > biggest.sprite.bounds.size.sqrMagnitude) biggest = sr;
            }
            // No face/head part (the single-sprite slime) → detect on the biggest sprite and map the
            // result into the default face box CharacterPreview falls back to (0.15,0.45,0.70,0.50).
            bool noFace = faceR == null && headR == null;
            var face = faceR != null ? faceR : (headR != null ? headR : biggest);
            if (face == null) return null;
            var sp = face.sprite; var tex = sp.texture as Texture2D;
            if (tex == null) return null;
            // Texture-pixel region (bottom-up) drawn into the face box — same math as the Uv in
            // HudUi.DrawThumbInRect, so detected fractions land on the rendered face.
            float ppu = sp.pixelsPerUnit;
            Rect cell = sp.rect; Vector3 bc = sp.bounds.center, be = sp.bounds.extents;
            int rx = Mathf.RoundToInt(cell.x + sp.pivot.x + bc.x * ppu - be.x * ppu);
            int ry = Mathf.RoundToInt(cell.y + sp.pivot.y + bc.y * ppu - be.y * ppu);
            int rw = Mathf.RoundToInt(be.x * 2f * ppu);
            int rh = Mathf.RoundToInt(be.y * 2f * ppu);
            rx = Mathf.Clamp(rx, 0, tex.width - 1); ry = Mathf.Clamp(ry, 0, tex.height - 1);
            rw = Mathf.Clamp(rw, 1, tex.width - rx); rh = Mathf.Clamp(rh, 1, tex.height - ry);
            if (rw < 6 || rh < 6) return null;
            Color32[] all;
            try { all = tex.GetPixels32(); } catch { return null; } // needs Read/Write-enabled texture
            int tw = tex.width;
            bool Dark(int x, int y) { var p = all[y * tw + x]; return p.a > 128 && p.r < 85 && p.g < 85 && p.b < 85; }
            int yLo = ry + Mathf.RoundToInt(rh * 0.28f); // eyes are in the upper part (above the nose/mouth)
            var seen = new bool[rw * rh];
            var blobs = new List<(int area, float cx, float cy, float rad, float fill)>();
            var stack = new Stack<int>();
            for (int sy0 = yLo; sy0 < ry + rh; sy0++)
                for (int sx0 = rx; sx0 < rx + rw; sx0++)
                {
                    if (seen[(sy0 - ry) * rw + (sx0 - rx)] || !Dark(sx0, sy0)) continue;
                    stack.Clear(); stack.Push(sx0 | (sy0 << 16)); seen[(sy0 - ry) * rw + (sx0 - rx)] = true;
                    int area = 0, mnx = int.MaxValue, mxx = int.MinValue, mny = int.MaxValue, mxy = int.MinValue;
                    double ax = 0, ay = 0;
                    while (stack.Count > 0)
                    {
                        int v = stack.Pop(); int px = v & 0xFFFF, py = (v >> 16) & 0xFFFF;
                        area++; ax += px; ay += py;
                        if (px < mnx) mnx = px; if (px > mxx) mxx = px; if (py < mny) mny = py; if (py > mxy) mxy = py;
                        for (int d = 0; d < 4; d++)
                        {
                            int nx = px + (d == 0 ? 1 : d == 1 ? -1 : 0);
                            int ny = py + (d == 2 ? 1 : d == 3 ? -1 : 0);
                            if (nx < rx || nx >= rx + rw || ny < yLo || ny >= ry + rh) continue;
                            int idx = (ny - ry) * rw + (nx - rx);
                            if (seen[idx] || !Dark(nx, ny)) continue;
                            seen[idx] = true; stack.Push(nx | (ny << 16));
                        }
                    }
                    // radius from the blob's full extent — robust for ring-shaped eyes (the owl's).
                    // fill = area / bbox: round pupils ≈0.6, ring eyes ≈0.3-0.5, thin outline arcs ≈0.15.
                    if (area > 25)
                    {
                        int bw = mxx - mnx + 1, bh = mxy - mny + 1;
                        blobs.Add((area, (float)(ax / area), (float)(ay / area), 0.5f * Mathf.Max(bw, bh), area / (float)(bw * bh)));
                    }
                }
            if (blobs.Count < 2) return null;
            // A hat (the frog wizard's) splits the dark body OUTLINE into two large, low-fill
            // C-shaped arcs near the cheeks that out-span the real eyes. Drop those arcs and keep
            // filled pupils (and round ring eyes); fall back to all blobs if too few survive so
            // hatless / ring-eyed faces (the owl) are never starved.
            var eyes = blobs.FindAll(b => b.fill >= 0.30f);
            if (eyes.Count < 2) eyes = blobs;
            eyes.Sort((a, b) => b.area.CompareTo(a.area));
            int take = Mathf.Min(5, eyes.Count);
            var l = eyes[0]; var r = eyes[0];
            for (int i = 0; i < take; i++) { if (eyes[i].cx < l.cx) l = eyes[i]; if (eyes[i].cx > r.cx) r = eyes[i]; }
            float radPx = 0.5f * (l.rad + r.rad);
            Vector2 eL2 = new Vector2((l.cx - rx) / rw, (l.cy - ry) / rh);
            Vector2 eR2 = new Vector2((r.cx - rx) / rw, (r.cy - ry) / rh);
            float rad = radPx / rw;
            if (noFace) // full-sprite (≈ whole character) space -> default face box
            {
                eL2 = new Vector2((eL2.x - 0.15f) / 0.70f, (eL2.y - 0.45f) / 0.50f);
                eR2 = new Vector2((eR2.x - 0.15f) / 0.70f, (eR2.y - 0.45f) / 0.50f);
                rad /= 0.70f;
            }
            return (eL2, eR2, rad);
        }

        /// <summary>The art prefab for a character, or null if it has none (→ player).</summary>
        public static GameObject Load(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            if (!Map.TryGetValue(characterId, out var res)) return null;
            if (_cache.TryGetValue(res, out var cached)) return cached;
            if (_missing.Contains(res)) return null;

            var prefab = Resources.Load<GameObject>("Chars/" + res);
            if (prefab == null) { _missing.Add(res); return null; }
            _cache[res] = prefab;
            return prefab;
        }
    }
}
