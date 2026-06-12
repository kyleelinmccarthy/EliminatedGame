using System.Collections.Generic;
using UnityEngine;

namespace Eliminated.Game.View
{
    /// <summary>
    /// Generates a sprite for each worn accessory procedurally (a small software
    /// shape-painter into a Texture2D), so cosmetics show on the player with no art
    /// pack — porting the silhouettes of the web game's hats / eyewear / neckwear /
    /// ear pieces. These are clean first-draft silhouettes (rects, ellipses, rings,
    /// triangles); tune colors/shapes per id in <see cref="Draw"/>. Cached per id.
    /// </summary>
    public static class AccessoryArt
    {
        private const int S = 128; // canvas px (PPU = S → sprite is ~1 world unit)
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> _lensCache = new Dictionary<string, Sprite>();
        private static bool _suppressEyewearBridge; // GetLenses sets this so the built-in bridge is skipped

        public static Sprite Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_cache.TryGetValue(id, out var s)) return s;
            s = Build(id);
            _cache[id] = s;
            return s;
        }

        // Bridgeless eyewear for the menu preview, which lays a lens on each eye and draws its OWN
        // bridge — so the lens crops carry no built-in bridge stub. Non-eyewear is identical to Get.
        public static Sprite GetLenses(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_lensCache.TryGetValue(id, out var s)) return s;
            _suppressEyewearBridge = true;
            try { s = Build(id); } finally { _suppressEyewearBridge = false; }
            _lensCache[id] = s;
            return s;
        }

        private static Sprite Build(string id)
        {
            var p = new Painter(S);
            Draw(id, p);
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(p.Buf);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        }

        // ---- palette helpers ----
        private static Color Rgb(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);
        private static readonly Color Dark = Rgb(34, 30, 44);
        private static readonly Color Lens = Rgb(190, 235, 255, 0.30f);
        private static readonly Color Gold = Rgb(255, 206, 58);
        private static readonly Color Red = Rgb(226, 59, 78);

        // Every accessory is drawn centered on (64,64); y is up. PlayerView anchors the
        // sprite's center to a per-slot point on the character, so rough centering is fine.
        private static void Draw(string id, Painter p)
        {
            switch (id)
            {
                // ---------- HATS ----------
                case "tophat":
                    p.Ellipse(64, 52, 40, 9, Dark);       // brim
                    p.Rect(46, 52, 36, 46, Dark);          // crown
                    p.Rect(46, 60, 36, 9, Red);            // band
                    break;
                case "cap":
                    p.Dome(64, 60, 30, 26, Rgb(33, 150, 243));     // domed crown
                    p.Ellipse(92, 58, 24, 7, Rgb(21, 101, 192));   // forward brim
                    p.Circle(64, 84, 4, Rgb(21, 101, 192));        // top button
                    break;
                case "partyhat":
                    p.Tri(new Vector2(40, 50), new Vector2(88, 50), new Vector2(64, 104), Rgb(255, 79, 154));
                    p.Line(new Vector2(46, 66), new Vector2(82, 66), 4, Rgb(255, 255, 255, 0.8f));
                    p.Line(new Vector2(52, 82), new Vector2(76, 82), 4, Rgb(255, 255, 255, 0.8f));
                    p.Circle(64, 104, 7, Color.white);
                    break;
                case "cowboy":
                    p.Ellipse(64, 50, 42, 9, Rgb(141, 110, 99));  // wide brim
                    p.Rect(50, 52, 28, 30, Rgb(141, 110, 99));    // crown
                    p.Ellipse(64, 82, 14, 8, Rgb(141, 110, 99));  // crown top
                    p.Rect(50, 56, 28, 7, Rgb(91, 58, 34));       // band
                    break;
                case "crown":
                    p.Rect(44, 52, 40, 11, Gold);                  // band
                    for (int i = -2; i <= 2; i++)
                    {
                        float x = 64 + i * 16f;
                        p.Tri(new Vector2(x - 7, 62), new Vector2(x + 7, 62), new Vector2(x, 86), Gold);
                        p.Circle(x, 84, 3.5f, Red);                // jewel
                    }
                    break;
                case "beanie":
                    p.Dome(64, 60, 30, 26, Rgb(25, 211, 189));     // knit dome
                    p.Rect(34, 50, 60, 13, Rgb(0, 150, 136));      // folded cuff
                    break;

                // ---------- EYEWEAR ----------
                case "glasses": Eyewear(p, 14, Dark, Lens); break;
                case "specs":   SquareEyewear(p, Dark, Lens); break;
                case "cateye":  Eyewear(p, 13, Rgb(255, 79, 154), Lens, wings: true); break;
                case "rounds":  Eyewear(p, 14, Rgb(60, 50, 70), Rgb(36, 26, 51, 0.78f)); break;
                case "shades":  SquareEyewear(p, Dark, Rgb(36, 26, 51, 0.85f)); break;
                case "aviators":Eyewear(p, 15, Gold, Rgb(60, 50, 70, 0.7f), teardrop: true); break;
                case "eyepatch":
                    p.Line(new Vector2(8, 52), new Vector2(120, 80), 7, Dark);  // strap around the head
                    p.Ellipse(22, 64, 21, 24, Rgb(64, 58, 72));                 // leather edge (reads on dark fur)
                    p.Ellipse(22, 64, 18, 21, Dark);                            // patch over one eye
                    p.Ellipse(15, 70, 5, 6, Rgb(120, 120, 135));                // sheen
                    break;

                // ---------- NECKWEAR ----------
                case "bandana":
                    p.Tri(new Vector2(38, 70), new Vector2(90, 70), new Vector2(64, 40), Red);
                    p.Circle(48, 70, 4, Color.white); p.Circle(64, 60, 4, Color.white); p.Circle(80, 70, 4, Color.white);
                    break;
                case "bowtie":
                    p.Tri(new Vector2(64, 64), new Vector2(42, 78), new Vector2(42, 50), Rgb(255, 46, 136));
                    p.Tri(new Vector2(64, 64), new Vector2(86, 78), new Vector2(86, 50), Rgb(255, 46, 136));
                    p.Rect(59, 56, 10, 16, Rgb(156, 18, 80));
                    break;

                // ---------- BEHIND THE EAR ----------
                case "banana":   Banana(p, Rgb(255, 224, 88)); break;
                case "greenana": Banana(p, Rgb(124, 179, 66)); break;
                case "spotnana": Banana(p, Rgb(214, 175, 80)); p.Circle(60, 66, 3, Rgb(90, 60, 30)); p.Circle(70, 56, 3, Rgb(90, 60, 30)); break;
                case "flower":   Flower(p, Color.white, Gold); break;
                case "rose":     Flower(p, Rgb(229, 57, 53), Rgb(183, 28, 28)); break;
                case "bluebell": Flower(p, Rgb(128, 170, 255), Rgb(63, 81, 181)); break;
                case "sunflower":Flower(p, Gold, Rgb(93, 64, 55)); break;
                case "feather":
                    p.Line(new Vector2(50, 44), new Vector2(78, 92), 3, Rgb(120, 90, 60));      // spine
                    p.Ellipse(64, 68, 11, 26, Rgb(38, 198, 218, 0.92f));                         // vane
                    break;

                default: // unknown → a small gift token so nothing is invisible
                    p.Circle(64, 64, 20, Rgb(255, 79, 154));
                    p.Rect(60, 44, 8, 40, Gold);
                    break;
            }
        }

        private static void Eyewear(Painter p, float r, Color frame, Color lens, bool wings = false, bool teardrop = false)
        {
            float ly = 64f, lx = 42f;   // wide-set lenses to match the animals' wide eyes
            for (int s = -1; s <= 1; s += 2)
            {
                float cx = 64 + s * lx;
                if (teardrop) { p.Ellipse(cx, ly, r, r * 0.8f, lens); p.Ring(cx, ly, r, r - 3, frame); }
                else { p.Ring(cx, ly, r, r - 3, frame); p.Circle(cx, ly, r - 3, lens); }
                if (wings) p.Tri(new Vector2(cx + s * r * 0.6f, ly + r * 0.4f), new Vector2(cx + s * (r + 8), ly + r), new Vector2(cx + s * r, ly + r * 0.2f), frame);
            }
            if (!_suppressEyewearBridge)
                p.Line(new Vector2(64 - lx + r - 2, ly), new Vector2(64 + lx - r + 2, ly), 3, frame); // bridge (suppressed for the per-eye menu preview, which draws its own)
            p.Line(new Vector2(64 - lx - r, ly), new Vector2(64 - lx - r - 8, ly + 4), 3, frame);     // arms (outward to the ears)
            p.Line(new Vector2(64 + lx + r, ly), new Vector2(64 + lx + r + 8, ly + 4), 3, frame);
        }

        private static void SquareEyewear(Painter p, Color frame, Color lens)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float cx = 64 + s * 42f - 13f;   // wide-set lenses to match the animals' wide eyes
                p.Rect(cx, 53, 26, 22, frame);
                p.Rect(cx + 3, 56, 20, 16, lens);
            }
            if (!_suppressEyewearBridge)
                p.Line(new Vector2(35, 64), new Vector2(93, 64), 3, frame);   // bridge (suppressed for the per-eye menu preview, which draws its own)
            p.Line(new Vector2(9, 65), new Vector2(2, 67), 3, frame);     // arms (outward to the ears)
            p.Line(new Vector2(119, 64), new Vector2(126, 67), 3, frame);
        }

        private static void Banana(Painter p, Color c)
        {
            p.Line(new Vector2(48, 48), new Vector2(80, 84), 13, c); // fat diagonal body
            p.Circle(48, 48, 5, Rgb(90, 70, 40));                    // stem tip
        }

        private static void Flower(Painter p, Color petal, Color center)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                p.Circle(64 + Mathf.Cos(a) * 16f, 64 + Mathf.Sin(a) * 16f, 9f, petal);
            }
            p.Circle(64, 64, 9f, center);
        }
    }

    /// <summary>A minimal software rasterizer (alpha-over) into an RGBA buffer.</summary>
    internal sealed class Painter
    {
        public readonly Color32[] Buf;
        private readonly int _n;

        public Painter(int size) { _n = size; Buf = new Color32[size * size]; }

        private void Blend(int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= _n || y >= _n || c.a <= 0f) return;
            int i = y * _n + x;
            Color dst = Buf[i];
            float a = c.a, ia = 1f - a;
            Buf[i] = new Color(c.r * a + dst.r * ia, c.g * a + dst.g * ia, c.b * a + dst.b * ia, a + dst.a * ia);
        }

        public void Rect(float x, float y, float w, float h, Color c)
        {
            for (int yy = Mathf.RoundToInt(y); yy < Mathf.RoundToInt(y + h); yy++)
                for (int xx = Mathf.RoundToInt(x); xx < Mathf.RoundToInt(x + w); xx++) Blend(xx, yy, c);
        }

        public void Ellipse(float cx, float cy, float rx, float ry, Color c)
        {
            for (int yy = Mathf.FloorToInt(cy - ry); yy <= cy + ry; yy++)
                for (int xx = Mathf.FloorToInt(cx - rx); xx <= cx + rx; xx++)
                {
                    float dx = (xx - cx) / rx, dy = (yy - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) Blend(xx, yy, c);
                }
        }

        public void Circle(float cx, float cy, float r, Color c) => Ellipse(cx, cy, r, r, c);

        /// <summary>Upper half of an ellipse (flat bottom) — domes for caps/beanies.</summary>
        public void Dome(float cx, float cy, float rx, float ry, Color c)
        {
            for (int yy = Mathf.FloorToInt(cy); yy <= cy + ry; yy++)
                for (int xx = Mathf.FloorToInt(cx - rx); xx <= cx + rx; xx++)
                {
                    float dx = (xx - cx) / rx, dy = (yy - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) Blend(xx, yy, c);
                }
        }

        public void Ring(float cx, float cy, float ro, float ri, Color c)
        {
            for (int yy = Mathf.FloorToInt(cy - ro); yy <= cy + ro; yy++)
                for (int xx = Mathf.FloorToInt(cx - ro); xx <= cx + ro; xx++)
                {
                    float d = Mathf.Sqrt((xx - cx) * (xx - cx) + (yy - cy) * (yy - cy));
                    if (d <= ro && d >= ri) Blend(xx, yy, c);
                }
        }

        public void Tri(Vector2 a, Vector2 b, Vector2 d, Color c)
        {
            float minx = Mathf.Min(a.x, Mathf.Min(b.x, d.x)), maxx = Mathf.Max(a.x, Mathf.Max(b.x, d.x));
            float miny = Mathf.Min(a.y, Mathf.Min(b.y, d.y)), maxy = Mathf.Max(a.y, Mathf.Max(b.y, d.y));
            for (int yy = Mathf.FloorToInt(miny); yy <= maxy; yy++)
                for (int xx = Mathf.FloorToInt(minx); xx <= maxx; xx++)
                {
                    var pt = new Vector2(xx + 0.5f, yy + 0.5f);
                    float s1 = Sign(pt, a, b), s2 = Sign(pt, b, d), s3 = Sign(pt, d, a);
                    bool neg = s1 < 0 || s2 < 0 || s3 < 0, pos = s1 > 0 || s2 > 0 || s3 > 0;
                    if (!(neg && pos)) Blend(xx, yy, c);
                }
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) =>
            (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        public void Line(Vector2 a, Vector2 b, float th, Color c)
        {
            int steps = Mathf.CeilToInt((b - a).magnitude);
            for (int i = 0; i <= steps; i++)
            {
                var pt = Vector2.Lerp(a, b, steps == 0 ? 0f : (float)i / steps);
                Circle(pt.x, pt.y, th * 0.5f, c);
            }
        }
    }
}
