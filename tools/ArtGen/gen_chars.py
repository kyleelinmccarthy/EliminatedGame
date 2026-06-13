#!/usr/bin/env python3
"""
Generates kawaii character sprites for every roster character that has no
Asset Store art: the food/fruit/veg gang, the leftover critters (bunny, pig,
mouse, hamster, ghost) and the frog wizard.

Style-matched to the imported packs (MiMU animals / Jovial cuties / LayerLab
slime): a soft rounded body, darker same-hue outline, gentle top-light
shading, big dark eyes with a white spec, small smile, blush. Single-sprite
characters — exactly how the slime prefab works in PlayerView/CharacterPreview.

Our own procedural output — no third-party assets. Dependency-free (stdlib
only). 256x256 RGBA PNG, SDF-rasterized with analytic anti-aliasing.

Faces sit ~0.6-0.7 of the sprite height so the generic accessory anchors in
PlayerView (hat above the crown, glasses at 0.66 H) land sensibly.

Usage:  python3 gen_chars.py [out_dir]
        default out_dir = unity/EliminatedGame/Assets/Eliminated/Art/Chars
"""
import math, struct, zlib, sys, os

S = 256  # canvas size; y grows DOWN (image space)

# ---- small helpers ----------------------------------------------------------
def clamp(v, lo, hi): return lo if v < lo else hi if v > hi else v
def lerp(a, b, t): return a + (b - a) * t
def lerp3(c1, c2, t): return (lerp(c1[0], c2[0], t), lerp(c1[1], c2[1], t), lerp(c1[2], c2[2], t))
def hexc(h): return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))
def lighten(c, f): return lerp3(c, (255, 255, 255), f)
def darken(c, f): return lerp3(c, (0, 0, 0), f)

EYE = (38, 32, 40)        # near-black (also satisfies CharacterArt eye detection)
MOUTH = (122, 56, 62)
WHITE = (255, 255, 255)
BLUSH = (245, 120, 140)

# ---- canvas -----------------------------------------------------------------
class Img:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.px = [0.0] * (w * h * 4)  # straight RGBA, 0..1

    def over(self, x, y, c, a):
        if a <= 0.0: return
        if a > 1.0: a = 1.0
        i = (y * self.w + x) * 4
        px = self.px
        pa = px[i + 3]
        na = a + pa * (1.0 - a)
        if na <= 1e-6: return
        r, g, b = c[0] / 255.0, c[1] / 255.0, c[2] / 255.0
        inv = pa * (1.0 - a)
        px[i]     = (r * a + px[i]     * inv) / na
        px[i + 1] = (g * a + px[i + 1] * inv) / na
        px[i + 2] = (b * a + px[i + 2] * inv) / na
        px[i + 3] = na

    def save_png(self, path):
        raw = bytearray()
        for y in range(self.h):
            raw.append(0)  # filter: none
            for x in range(self.w):
                i = (y * self.w + x) * 4
                raw += bytes((
                    int(clamp(self.px[i] * 255 + 0.5, 0, 255)),
                    int(clamp(self.px[i + 1] * 255 + 0.5, 0, 255)),
                    int(clamp(self.px[i + 2] * 255 + 0.5, 0, 255)),
                    int(clamp(self.px[i + 3] * 255 + 0.5, 0, 255))))
        def chunk(tag, data):
            c = tag + data
            return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c) & 0xFFFFFFFF)
        png = b"\x89PNG\r\n\x1a\n"
        png += chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, 6, 0, 0, 0))
        png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
        png += chunk(b"IEND", b"")
        with open(path, "wb") as f: f.write(png)

# ---- SDF shapes ---------------------------------------------------------------
class Shape:
    __slots__ = ("fn", "bbox")
    def __init__(self, fn, bbox): self.fn, self.bbox = fn, bbox

def circle(cx, cy, r):
    return Shape(lambda x, y: math.hypot(x - cx, y - cy) - r,
                 (cx - r, cy - r, cx + r, cy + r))

def ellipse(cx, cy, rx, ry):
    k = min(rx, ry)
    def fn(x, y):
        return (math.hypot((x - cx) / rx, (y - cy) / ry) - 1.0) * k
    return Shape(fn, (cx - rx, cy - ry, cx + rx, cy + ry))

def rot_ellipse(cx, cy, rx, ry, deg):
    ca, sa = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    k = min(rx, ry)
    def fn(x, y):
        dx, dy = x - cx, y - cy
        u, v = ca * dx + sa * dy, -sa * dx + ca * dy
        return (math.hypot(u / rx, v / ry) - 1.0) * k
    m = max(rx, ry)
    return Shape(fn, (cx - m, cy - m, cx + m, cy + m))

def capsule(ax, ay, bx, by, r):
    ex, ey = bx - ax, by - ay
    ll = ex * ex + ey * ey
    def fn(x, y):
        wx, wy = x - ax, y - ay
        t = clamp((wx * ex + wy * ey) / ll, 0.0, 1.0) if ll > 0 else 0.0
        return math.hypot(wx - ex * t, wy - ey * t) - r
    return Shape(fn, (min(ax, bx) - r, min(ay, by) - r, max(ax, bx) + r, max(ay, by) + r))

def rbox(cx, cy, hw, hh, rad):
    def fn(x, y):
        qx, qy = abs(x - cx) - (hw - rad), abs(y - cy) - (hh - rad)
        return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - rad
    return Shape(fn, (cx - hw, cy - hh, cx + hw, cy + hh))

def poly(pts, round=0.0):
    n = len(pts)
    def fn(x, y):
        d = (x - pts[0][0]) ** 2 + (y - pts[0][1]) ** 2
        s = 1.0
        j = n - 1
        for i in range(n):
            ex, ey = pts[j][0] - pts[i][0], pts[j][1] - pts[i][1]
            wx, wy = x - pts[i][0], y - pts[i][1]
            ll = ex * ex + ey * ey
            t = clamp((wx * ex + wy * ey) / ll, 0.0, 1.0) if ll > 0 else 0.0
            bx, by = wx - ex * t, wy - ey * t
            dd = bx * bx + by * by
            if dd < d: d = dd
            c1 = y >= pts[i][1]; c2 = y < pts[j][1]; c3 = ex * wy > ey * wx
            if (c1 and c2 and c3) or (not c1 and not c2 and not c3): s = -s
            j = i
        return s * math.sqrt(d) - round
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]
    return Shape(fn, (min(xs) - round, min(ys) - round, max(xs) + round, max(ys) + round))

def grow(shp, o):
    """Outset (o>0) / inset (o<0) a shape."""
    f = shp.fn
    b = shp.bbox
    return Shape(lambda x, y: f(x, y) - o, (b[0] - o, b[1] - o, b[2] + o, b[3] + o))

def union(*shapes):
    fns = [s.fn for s in shapes]
    bs = [s.bbox for s in shapes]
    bbox = (min(b[0] for b in bs), min(b[1] for b in bs),
            max(b[2] for b in bs), max(b[3] for b in bs))
    def fn(x, y):
        d = fns[0](x, y)
        for f in fns[1:]:
            v = f(x, y)
            if v < d: d = v
        return d
    return Shape(fn, bbox)

def sunion(k, *shapes):
    """Smooth union (blobby joins)."""
    fns = [s.fn for s in shapes]
    bs = [s.bbox for s in shapes]
    bbox = (min(b[0] for b in bs) - k, min(b[1] for b in bs) - k,
            max(b[2] for b in bs) + k, max(b[3] for b in bs) + k)
    def fn(x, y):
        d = fns[0](x, y)
        for f in fns[1:]:
            b = f(x, y)
            h = clamp(0.5 + 0.5 * (b - d) / k, 0.0, 1.0)
            d = b + (d - b) * h - k * h * (1.0 - h)
        return d
    return Shape(fn, bbox)

def cut(a, b):
    """a minus b."""
    fa, fb = a.fn, b.fn
    return Shape(lambda x, y: max(fa(x, y), -fb(x, y)), a.bbox)

def intersect(a, b):
    fa, fb = a.fn, b.fn
    return Shape(lambda x, y: max(fa(x, y), fb(x, y)), a.bbox)

def below(shp, ylevel):  # keep y >= ylevel (lower part of the canvas)
    f = shp.fn
    b = shp.bbox
    return Shape(lambda x, y: max(f(x, y), ylevel - y), (b[0], max(b[1], ylevel), b[2], b[3]))

def above(shp, ylevel):  # keep y <= ylevel
    f = shp.fn
    b = shp.bbox
    return Shape(lambda x, y: max(f(x, y), y - ylevel), (b[0], b[1], b[2], min(b[3], ylevel)))

# ---- rasterizer ----------------------------------------------------------------
def render(img, shp, fill, outline=None, ow=0.0, alpha=1.0, soft=1.4):
    """Paint a shape with AA. `fill` is (r,g,b) or fn(x,y)->(r,g,b).
    `outline` paints a rim of width `ow` OUTSIDE the fill edge."""
    pad = ow + soft + 1.0
    x0 = max(0, int(shp.bbox[0] - pad)); y0 = max(0, int(shp.bbox[1] - pad))
    x1 = min(img.w - 1, int(shp.bbox[2] + pad) + 1); y1 = min(img.h - 1, int(shp.bbox[3] + pad) + 1)
    callable_fill = callable(fill)
    fn = shp.fn
    for y in range(y0, y1 + 1):
        fy = y + 0.5
        for x in range(x0, x1 + 1):
            d = fn(x + 0.5, fy)
            if d > ow + soft: continue
            if outline is not None and ow > 0.0:
                co = clamp(0.5 - (d - ow) / soft, 0.0, 1.0)
                if co > 0.0: img.over(x, y, outline, co * alpha)
            cb = clamp(0.5 - d / soft, 0.0, 1.0)
            if cb > 0.0:
                img.over(x, y, fill(x, y) if callable_fill else fill, cb * alpha)

def glow(img, cx, cy, rx, ry, color, a):
    """Soft elliptical blob (highlights, blush, shading)."""
    x0 = max(0, int(cx - rx)); y0 = max(0, int(cy - ry))
    x1 = min(img.w - 1, int(cx + rx) + 1); y1 = min(img.h - 1, int(cy + ry) + 1)
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            t = math.hypot((x + 0.5 - cx) / rx, (y + 0.5 - cy) / ry)
            if t >= 1.0: continue
            f = 1.0 - t
            img.over(x, y, color, a * f * f)

def vgrad(base, y0, y1, up=0.16, dn=0.10):
    """Vertical body shading: lighter top, slightly darker bottom."""
    top, bot = lighten(base, up), darken(base, dn)
    span = max(1.0, y1 - y0)
    def fn(x, y):
        return lerp3(top, bot, clamp((y - y0) / span, 0.0, 1.0))
    return fn

def body(img, shp, base, ow=5.0, out_f=0.52, up=0.16, dn=0.10, alpha=1.0):
    """Standard body pass: gradient fill + darker same-hue outline."""
    render(img, shp, vgrad(base, shp.bbox[1], shp.bbox[3], up, dn),
           outline=darken(base, out_f), ow=ow, alpha=alpha)

def sheen(img, shp_bbox, a=0.16):
    """Soft top-left highlight inside a body bbox."""
    x0, y0, x1, y1 = shp_bbox
    w, h = x1 - x0, y1 - y0
    glow(img, x0 + w * 0.36, y0 + h * 0.26, w * 0.30, h * 0.22, WHITE, a)

# ---- the face ------------------------------------------------------------------
def face(img, cx, cy, s=1.0, sclera=False, mouth="smile", blush=True, brows=None,
         eye=EYE, gap=26.0, mouth_c=MOUTH):
    """Kawaii face. Eyes at (cx +/- gap*s, cy)."""
    ex = gap * s
    for sx in (-1, 1):
        px = cx + sx * ex
        if sclera:
            render(img, circle(px, cy, 17.0 * s), WHITE)
            render(img, ellipse(px, cy, 9.5 * s, 12.5 * s), eye)
            render(img, circle(px + 3.5 * s, cy - 4.0 * s, 3.6 * s), WHITE)
        else:
            render(img, ellipse(px, cy, 11.0 * s, 15.0 * s), eye)
            render(img, circle(px + 4.0 * s, cy - 5.0 * s, 4.2 * s), WHITE)
        if brows:  # ('angry'|'sad', color)
            kind, bc = brows
            tilt = 8.0 * s if kind == "angry" else -8.0 * s
            render(img, capsule(px - 10 * s * sx, cy - 24 * s - (tilt if sx < 0 else 0),
                                px + 10 * s * sx, cy - 24 * s - (tilt if sx > 0 else 0), 3.4 * s), bc)
    my = cy + 25.0 * s
    if mouth == "smile":
        render(img, below(circle(cx, my, 11.0 * s), my), mouth_c)
    elif mouth == "ooo":
        render(img, circle(cx, my, 7.0 * s), mouth_c)
    elif mouth == "line":
        render(img, capsule(cx - 8 * s, my, cx + 8 * s, my, 3.0 * s), mouth_c)
    if blush:
        glow(img, cx - 46 * s, cy + 15 * s, 16 * s, 10 * s, BLUSH, 0.42)
        glow(img, cx + 46 * s, cy + 15 * s, 16 * s, 10 * s, BLUSH, 0.42)

# ================================================================================
#  CHARACTERS
# ================================================================================
def egg_shape(cx=128, cy=148):
    return sunion(30, ellipse(cx, cy + 14, 82, 86), ellipse(cx, cy - 36, 60, 64))

def ch_egg(img):
    e = egg_shape()
    body(img, e, hexc("f7f1e4"), out_f=0.38)
    sheen(img, e.bbox, 0.22)
    face(img, 128, 118)

def ch_goldegg(img):
    e = egg_shape()
    body(img, e, hexc("efbe3f"), out_f=0.45)
    sheen(img, e.bbox, 0.30)
    # shine streaks
    render(img, rot_ellipse(96, 78, 5, 26, 24), WHITE, alpha=0.55)
    render(img, rot_ellipse(116, 64, 3.4, 14, 24), WHITE, alpha=0.55)
    # sparkles
    for (sx, sy, sr) in ((178, 92, 9), (62, 148, 7)):
        render(img, union(capsule(sx - sr, sy, sx + sr, sy, 1.8), capsule(sx, sy - sr, sx, sy + sr, 1.8)), WHITE, alpha=0.9)
    face(img, 128, 118)

def ch_egg2(img):
    # Sir Nightshade is an eggplant (aubergine): glossy deep-purple teardrop body
    # with a leafy green calyx + stem, not a black egg.
    bulb = sunion(36, ellipse(128, 170, 80, 82), ellipse(128, 98, 46, 56))
    purple = hexc("6a2d8f")
    body(img, bulb, purple, ow=5.5, out_f=0.5, up=0.22, dn=0.14)
    sheen(img, bulb.bbox, 0.12)
    # glossy aubergine-skin specular streaks down the left belly (clear of the face)
    render(img, rot_ellipse(74, 168, 7, 44, 8), WHITE, alpha=0.20)
    render(img, rot_ellipse(86, 150, 3.2, 20, 8), WHITE, alpha=0.30)
    # green calyx: a star of pointed sepals fanning over the shoulders
    leaf = hexc("5aa83e")
    sepals = [((104, 62), (126, 58), (60, 98)), ((110, 58), (130, 56), (92, 108)),
              ((118, 56), (138, 56), (128, 110)), ((126, 56), (146, 58), (164, 108)),
              ((130, 58), (152, 62), (196, 98))]
    for (a, b, tip) in sepals:
        render(img, poly([a, b, tip], round=9), vgrad(leaf, 56, 110),
               outline=darken(leaf, 0.45), ow=3)
    # short stem nub poking up from the calyx centre
    stem = hexc("728a3c")
    render(img, capsule(128, 66, 123, 26, 9), vgrad(stem, 26, 66),
           outline=darken(stem, 0.4), ow=3)
    face(img, 128, 126, sclera=True)

def ch_avo(img):
    pear = sunion(36, ellipse(128, 168, 80, 76), ellipse(128, 92, 56, 56))
    body(img, pear, hexc("5d7a2c"), ow=5.5, out_f=0.5)
    flesh = grow(pear, -14)
    render(img, flesh, vgrad(hexc("cfe096"), flesh.bbox[1], flesh.bbox[3], 0.12, 0.06))
    pit = circle(128, 188, 32)
    render(img, pit, vgrad(hexc("9a6438"), 156, 220), outline=darken(hexc("9a6438"), 0.35), ow=3)
    glow(img, 119, 178, 12, 9, WHITE, 0.4)
    sheen(img, pear.bbox, 0.10)
    face(img, 128, 108)

def ch_berry(img):
    b = poly([(46, 86), (210, 86), (128, 234)], round=36)
    body(img, b, hexc("e83550"), ow=5)
    # seeds
    for (sx, sy) in ((78, 120), (178, 120), (60, 152), (196, 152), (95, 185), (161, 185), (128, 208)):
        render(img, rot_ellipse(sx, sy, 3.2, 5.0, 10 if sx < 128 else -10), hexc("f8e9ae"), alpha=0.95)
    sheen(img, b.bbox, 0.12)
    # leafy calyx
    leaf = hexc("46a44b")
    for (x0, x1, tip) in ((70, 110, (62, 36)), (105, 145, (128, 28)), (140, 180, (192, 36))):
        render(img, poly([(x0, 70), (x1, 70), tip], round=8), vgrad(leaf, 20, 78),
               outline=darken(leaf, 0.45), ow=3.5)
    render(img, capsule(128, 44, 128, 24, 6), darken(leaf, 0.2))
    face(img, 128, 128)

def ch_brocc(img):
    stalk = capsule(128, 150, 128, 212, 38)
    body(img, stalk, hexc("cde09a"), out_f=0.42)
    crown = sunion(26, circle(78, 110, 42), circle(128, 84, 52), circle(178, 110, 42),
                   circle(98, 146, 38), circle(158, 146, 38))
    body(img, crown, hexc("3f7d33"), out_f=0.5, up=0.20)
    # florets texture
    for (fx, fy, fr) in ((78, 102, 13), (118, 72, 14), (158, 86, 12), (185, 116, 11), (100, 132, 11), (146, 130, 12)):
        render(img, circle(fx, fy, fr), lighten(hexc("3f7d33"), 0.18), alpha=0.8)
    face(img, 128, 116, sclera=True)

def ch_donut(img):
    dough = circle(128, 140, 95)
    body(img, dough, hexc("e8b066"), ow=5)
    # wavy frosting over the top half
    def frost_fn(x, y):
        edge = 168 + 11 * math.sin((x - 128) * 0.085)
        return max(dough.fn(x, y) + 6, y - edge)
    frost = Shape(frost_fn, (33, 45, 223, 182))
    fr = hexc("ef6a9e")
    render(img, frost, vgrad(fr, 45, 182, 0.14, 0.06), outline=darken(fr, 0.35), ow=3)
    # sprinkles (kept clear of the face)
    spr = [(70, 84, 30, "fff5a0"), (96, 62, -20, "8ce0f0"), (160, 62, 15, "fff5a0"),
           (188, 86, -35, "b8f08a"), (62, 124, 80, "8ce0f0"), (196, 124, 70, "fff5a0"),
           (128, 52, 0, "b8f08a")]
    for (sx, sy, ang, col) in spr:
        render(img, rot_ellipse(sx, sy, 9, 3.2, ang), hexc(col), outline=darken(hexc(col), 0.25), ow=1.5)
    face(img, 128, 112)

def ch_pickle(img):
    p = capsule(128, 88, 128, 196, 56)
    body(img, p, hexc("6f9c3c"), ow=5)
    for (sx, sy, sr) in ((96, 64, 6), (164, 80, 5), (88, 150, 6), (170, 168, 6), (118, 230, 5), (150, 222, 4)):
        render(img, circle(sx, sy, sr), darken(hexc("6f9c3c"), 0.22), alpha=0.7)
    sheen(img, p.bbox, 0.14)
    render(img, capsule(128, 30, 128, 44, 9), darken(hexc("6f9c3c"), 0.3))  # top nub
    face(img, 128, 116)

def ch_tomato(img):
    t = ellipse(128, 152, 92, 84)
    body(img, t, hexc("e84a38"), ow=5)
    sheen(img, t.bbox, 0.16)
    leaf = hexc("3f8f3f")
    for ang in (-60, -20, 20, 60):
        a = math.radians(ang - 90)
        tipx, tipy = 128 + math.cos(a) * 52, 86 + math.sin(a) * 30 + 14
        render(img, poly([(128 - 14, 78), (128 + 14, 78), (tipx, tipy)], round=6),
               vgrad(leaf, 60, 110), outline=darken(leaf, 0.4), ow=3)
    render(img, capsule(128, 50, 128, 72, 7), darken(leaf, 0.15))
    face(img, 128, 134)

def ch_pine(img):
    pb = hexc("f2b13c")
    bod = ellipse(128, 158, 76, 88)
    def fill(x, y):
        base = vgrad(pb, 70, 246)(x, y)
        u, v = x + y, x - y
        du = abs(((u + 24) % 48) - 24); dv = abs(((v + 24) % 48) - 24)
        if min(du, dv) < 3.0: base = darken(base, 0.18)
        return base
    render(img, bod, fill, outline=darken(pb, 0.5), ow=5)
    sheen(img, bod.bbox, 0.10)
    leaf = hexc("3f9447")
    for (x0, x1, tip) in ((96, 126, (76, 18)), (113, 143, (128, 6)), (130, 160, (180, 18))):
        render(img, poly([(x0, 76), (x1, 76), tip], round=7), vgrad(leaf, 6, 84),
               outline=darken(leaf, 0.45), ow=3.5)
    # princess tiara
    gold = hexc("f6cf4e")
    render(img, poly([(106, 70), (150, 70), (146, 52), (138, 64), (128, 46), (118, 64), (110, 52)], round=2),
           gold, outline=darken(gold, 0.35), ow=2.5)
    render(img, circle(128, 44, 4.5), hexc("ff7ab8"))
    face(img, 128, 150)

def ch_shroom(img):
    stem = capsule(128, 152, 128, 204, 42)
    body(img, stem, hexc("f2e7d2"), out_f=0.35)
    cap = above(circle(128, 118, 94), 152)
    capc = hexc("d8453c")
    render(img, cap, vgrad(capc, 24, 152), outline=darken(capc, 0.45), ow=5)
    render(img, capsule(40, 150, 216, 150, 5), darken(capc, 0.3))  # cap rim
    for (sx, sy, sr) in ((84, 92, 15), (150, 60, 17), (188, 116, 12), (118, 126, 9)):
        render(img, circle(sx, sy, sr), hexc("f7efe2"), alpha=0.95)
    face(img, 128, 186, s=0.85)

def ch_sushi(img):
    rice = rbox(128, 184, 86, 54, 42)
    body(img, rice, hexc("f5f1e6"), out_f=0.32)
    for (rx, ry) in ((76, 168), (108, 198), (152, 172), (180, 200), (128, 222)):
        render(img, rot_ellipse(rx, ry, 6, 3, 25), darken(hexc("f5f1e6"), 0.12), alpha=0.8)
    sal = hexc("f08652")
    slab = rbox(128, 110, 92, 42, 30)
    def fill(x, y):
        base = vgrad(sal, 68, 152)(x, y)
        if ((x - y + 200) % 46) < 8: base = lighten(base, 0.28)
        return base
    render(img, slab, fill, outline=darken(sal, 0.4), ow=4.5)
    face(img, 128, 112)

def ch_nana(img):
    # chunky banana: shallow crescent (a wide outer circle minus a far-off cut)
    bod = cut(circle(150, 130, 100), circle(250, 130, 120))
    nb = hexc("f6d33f")
    body(img, bod, nb, ow=5)
    # brown stub tips at the crescent's true endpoints (x=178, y=34/226)
    tip = hexc("7a5230")
    render(img, rot_ellipse(176, 38, 12, 8, -55), tip, outline=darken(tip, 0.3), ow=2.5)
    render(img, rot_ellipse(176, 222, 12, 8, 55), tip, outline=darken(tip, 0.3), ow=2.5)
    sheen(img, (52, 60, 110, 200), 0.12)
    face(img, 90, 122, s=0.82, gap=21)

def ch_plum(img):
    p = ellipse(128, 152, 88, 84)
    body(img, p, hexc("8e5bb0"), ow=5)
    render(img, capsule(128, 74, 124, 112, 4.5), darken(hexc("8e5bb0"), 0.25), alpha=0.8)  # cleft
    sheen(img, p.bbox, 0.14)
    leaf = hexc("4aa045")
    render(img, rot_ellipse(160, 62, 26, 11, -28), vgrad(leaf, 44, 82), outline=darken(leaf, 0.4), ow=3)
    render(img, capsule(128, 48, 128, 70, 6), hexc("6a4a30"))
    face(img, 128, 134)

def ch_orange(img):
    o = circle(128, 152, 90)
    body(img, o, hexc("f59b29"), ow=5)
    for (sx, sy) in ((78, 110, ), (170, 104,), (62, 168,), (190, 172,), (104, 220,), (152, 226,)):
        render(img, circle(sx, sy, 2.4), darken(hexc("f59b29"), 0.25), alpha=0.5)
    sheen(img, o.bbox, 0.15)
    leaf = hexc("3f8f3f")
    render(img, rot_ellipse(162, 58, 27, 12, -25), vgrad(leaf, 40, 78), outline=darken(leaf, 0.4), ow=3)
    render(img, capsule(128, 46, 128, 66, 6), hexc("6a4a30"))
    face(img, 128, 134)

def ch_blueberry(img):
    b = circle(128, 152, 88)
    body(img, b, hexc("5b6cc0"), ow=5)
    sheen(img, b.bbox, 0.20)
    # star calyx on the crown
    calyx = hexc("36418a")
    pts = []
    for i in range(10):
        a = math.radians(i * 36 - 90)
        r = 22 if i % 2 == 0 else 9
        pts.append((128 + math.cos(a) * r, 80 + math.sin(a) * r * 0.7))
    render(img, poly(pts, round=2), calyx, alpha=0.9)
    face(img, 128, 138)

def ch_carrot(img):
    c = poly([(60, 78), (196, 78), (128, 236)], round=30)
    body(img, c, hexc("f08828"), ow=5)
    for (y, x0, x1) in ((130, 96, 134), (162, 110, 146), (192, 118, 142)):
        render(img, capsule(x0, y, x1, y, 3), darken(hexc("f08828"), 0.22), alpha=0.6)
    sheen(img, c.bbox, 0.10)
    leaf = hexc("4aa045")
    for (x0, x1, tip) in ((92, 122, (70, 14)), (113, 143, (128, 4)), (134, 164, (186, 14))):
        render(img, poly([(x0, 68), (x1, 68), tip], round=6), vgrad(leaf, 2, 76),
               outline=darken(leaf, 0.45), ow=3)
    face(img, 128, 118)

def ch_dragonfruit(img):
    pink = hexc("ec4f93")
    bod = ellipse(128, 152, 80, 92)
    # scale spikes with green tips (under the body)
    for (ang, ln) in ((-135, 34), (-90, 38), (-45, 34), (-160, 28), (-20, 28), (160, 30), (20, 30), (135, 30), (45, 30)):
        a = math.radians(ang)
        bx, by = 128 + math.cos(a) * 64, 152 + math.sin(a) * 76
        tx, ty = 128 + math.cos(a) * (64 + ln), 152 + math.sin(a) * (76 + ln)
        nx, ny = -math.sin(a) * 13, math.cos(a) * 13
        render(img, poly([(bx - nx, by - ny), (bx + nx, by + ny), (tx, ty)], round=4),
               pink, outline=darken(pink, 0.4), ow=3)
        render(img, circle(tx, ty, 6.5), hexc("7ac74f"))
    body(img, bod, pink, ow=5)
    inner = ellipse(128, 152, 54, 66)
    render(img, inner, vgrad(hexc("f8f2e4"), 86, 218, 0.05, 0.06))
    for (sx, sy) in ((104, 110), (152, 106), (90, 150), (166, 152), (108, 196), (148, 198), (128, 215)):
        render(img, circle(sx, sy, 2.6), hexc("2c2630"), alpha=0.9)
    face(img, 128, 138, s=0.9)

def ch_melon(img):
    slice_ = below(circle(128, 116, 102), 96)
    rind = hexc("3f8f3f")
    render(img, slice_, vgrad(rind, 96, 218), outline=darken(rind, 0.45), ow=5)
    pith = below(grow(circle(128, 116, 102), -13), 96)
    render(img, pith, hexc("d8eec0"))
    flesh = below(grow(circle(128, 116, 102), -22), 96)
    render(img, flesh, vgrad(hexc("ea5566"), 96, 196, 0.12, 0.08))
    render(img, capsule(32, 96, 224, 96, 4), darken(hexc("ea5566"), 0.25), alpha=0.6)
    # seeds: kept just above CharacterArt.DetectEyes' dark threshold (r=95 > 85)
    # so the leftmost/rightmost dark blobs are the EYES, not the outer seeds.
    for (sx, sy, ang) in ((74, 132, 10), (182, 132, -10), (96, 168, 5), (160, 168, -5)):
        render(img, rot_ellipse(sx, sy, 3.4, 5.4, ang), (95, 58, 66), alpha=0.95)
    face(img, 128, 142)

def ch_onigiri(img):
    tri = poly([(128, 36), (226, 200), (30, 200)], round=42)
    body(img, tri, hexc("f6f3ea"), out_f=0.32)
    sheen(img, tri.bbox, 0.14)
    nori = rbox(128, 204, 50, 42, 10)
    render(img, nori, vgrad(hexc("2c3a2c"), 162, 246, 0.18, 0.0),
           outline=darken(hexc("2c3a2c"), 0.4), ow=3)
    face(img, 128, 138)

def ch_ghostpepper(img):
    red = hexc("c8262f")
    bod = sunion(30, circle(104, 102, 46), circle(116, 146, 40), circle(132, 182, 29),
                 circle(150, 208, 18), circle(166, 226, 9))
    body(img, bod, red, ow=5, up=0.20)
    glow(img, 92, 84, 26, 18, WHITE, 0.22)
    stem = hexc("3f7d33")
    render(img, capsule(98, 62, 86, 36, 9), stem, outline=darken(stem, 0.4), ow=3)
    render(img, rot_ellipse(104, 60, 26, 12, 10), vgrad(stem, 48, 74), outline=darken(stem, 0.4), ow=3)
    face(img, 106, 110, s=0.85, brows=("angry", EYE))

def ch_cosmic(img):
    bod = circle(128, 148, 90)
    base = hexc("191028")
    render(img, bod, vgrad(base, 58, 238, 0.10, 0.0), outline=hexc("584a86"), ow=4)
    # nebula swirls
    glow(img, 96, 110, 52, 38, hexc("5a3aa0"), 0.45)
    glow(img, 166, 184, 48, 36, hexc("2a4a9a"), 0.45)
    glow(img, 150, 96, 30, 22, hexc("8a3a8a"), 0.35)
    # stars
    for (sx, sy, sr, a) in ((84, 76, 1.8, 0.95), (170, 96, 1.4, 0.9), (66, 150, 1.5, 0.85),
                            (188, 142, 1.8, 0.95), (108, 208, 1.4, 0.8), (150, 224, 1.2, 0.8)):
        render(img, circle(sx, sy, sr), WHITE, alpha=a)
    # accretion ring (kept below the face)
    ring = cut(rot_ellipse(128, 182, 110, 27, -14), rot_ellipse(128, 182, 97, 18, -14))
    front = below(ring, 176)
    render(img, ring, hexc("8a6af5"), alpha=0.35)
    render(img, front, hexc("9a7cff"), alpha=0.9)
    face(img, 128, 120, sclera=True, blush=False)

# ---- critters -------------------------------------------------------------------
def ch_bunny(img):
    fur = hexc("f4f1ec")
    for (ax, bx) in ((96, 101), (160, 155)):
        e = capsule(ax, 44, bx, 116, 23)
        body(img, e, fur, out_f=0.32)
        render(img, capsule(ax + (2 if ax < 128 else -2), 58, bx, 108, 11), hexc("f3b8c4"))
    bod = ellipse(128, 166, 82, 76)
    body(img, bod, fur, out_f=0.32)
    sheen(img, bod.bbox, 0.16)
    face(img, 128, 148)

def ch_pig(img):
    pk = hexc("f2a9b8")
    for sx in (-1, 1):
        render(img, poly([(128 + sx * 38, 96), (128 + sx * 88, 70), (128 + sx * 80, 122)], round=10),
               vgrad(pk, 60, 130), outline=darken(pk, 0.4), ow=4)
        render(img, poly([(128 + sx * 52, 98), (128 + sx * 76, 84), (128 + sx * 72, 112)], round=6), hexc("e07a92"))
    bod = ellipse(128, 158, 88, 80)
    body(img, bod, pk, ow=5, out_f=0.4)
    sheen(img, bod.bbox, 0.12)
    face(img, 128, 130, mouth=None if False else "line", blush=True)
    snout = ellipse(128, 168, 32, 22)
    render(img, snout, vgrad(hexc("e88a9e"), 146, 190), outline=darken(pk, 0.4), ow=3)
    for sx in (-1, 1):
        render(img, ellipse(128 + sx * 11, 168, 4.5, 7), hexc("b85a72"))

def ch_mouse(img):
    gr = hexc("bcb8c6")
    for sx in (-1, 1):
        e = circle(128 + sx * 56, 84, 34)
        body(img, e, gr, out_f=0.4)
        render(img, circle(128 + sx * 56, 84, 20), hexc("f3b8c4"))
    bod = ellipse(128, 162, 82, 76)
    body(img, bod, gr, ow=5, out_f=0.4)
    sheen(img, bod.bbox, 0.14)
    face(img, 128, 140)
    for sx in (-1, 1):  # whiskers
        for dy in (-4, 6):
            render(img, capsule(128 + sx * 58, 158 + dy, 128 + sx * 84, 154 + dy * 1.6, 1.6),
                   darken(gr, 0.3), alpha=0.7)

def ch_hamster(img):
    tan = hexc("e9c28e")
    for sx in (-1, 1):
        e = circle(128 + sx * 58, 76, 23)
        body(img, e, tan, out_f=0.4)
        render(img, circle(128 + sx * 58, 78, 12), hexc("f3b8c4"))
    bod = ellipse(128, 158, 92, 84)
    body(img, bod, tan, ow=5, out_f=0.42)
    belly = ellipse(128, 198, 52, 38)
    render(img, belly, hexc("f7ead2"), alpha=0.95)
    sheen(img, bod.bbox, 0.12)
    # chubby cheeks
    glow(img, 72, 154, 24, 18, lighten(tan, 0.25), 0.35)
    glow(img, 184, 154, 24, 18, lighten(tan, 0.25), 0.35)
    face(img, 128, 132)

def ch_ghost(img):
    wt = hexc("f5f5fa")
    dome = union(circle(128, 116, 80), rbox(128, 168, 80, 56, 24))
    scal = dome
    for bx in (88, 128, 168):
        scal = cut(scal, circle(bx, 232, 21))
    body(img, scal, wt, out_f=0.30)
    sheen(img, (48, 36, 208, 224), 0.18)
    face(img, 128, 122, mouth="ooo")
    # wispy arms
    for sx in (-1, 1):
        render(img, capsule(128 + sx * 76, 150, 128 + sx * 96, 132, 13), wt,
               outline=darken(wt, 0.30), ow=3.5)

def ch_frogwizard(img):
    # sim id "wizard" (Hocus Croakus, the FROG wizard). Output named frogwizard so
    # it can't collide with the Jovial human wizard.prefab in Resources/Chars.
    grn = hexc("6fae4a")
    # frog body
    bod = ellipse(128, 178, 84, 64)
    # eye bumps behind the hat brim
    for sx in (-1, 1):
        body(img, circle(128 + sx * 42, 120, 27), grn, out_f=0.45)
    body(img, bod, grn, ow=5)
    sheen(img, bod.bbox, 0.10)
    # frog eyes on the bumps
    for sx in (-1, 1):
        render(img, circle(128 + sx * 42, 118, 15), WHITE)
        render(img, ellipse(128 + sx * 42, 119, 8, 10.5), EYE)
        render(img, circle(128 + sx * 42 + 3, 115, 3.2), WHITE)
    # wide froggy smile
    render(img, intersect(cut(circle(128, 148, 32), circle(128, 142, 30)), below(circle(128, 148, 40), 152)),
           darken(grn, 0.45))
    glow(img, 70, 168, 15, 10, BLUSH, 0.40)
    glow(img, 186, 168, 15, 10, BLUSH, 0.40)
    # wizard hat
    pur = hexc("6a4aa8")
    render(img, poly([(128, 8), (180, 88), (76, 88)], round=9), vgrad(pur, 8, 96),
           outline=darken(pur, 0.45), ow=4)
    render(img, rot_ellipse(128, 92, 70, 15, 0), vgrad(darken(pur, 0.08), 77, 107),
           outline=darken(pur, 0.45), ow=4)
    band = hexc("f2c14e")
    render(img, rbox(128, 78, 34, 7, 5), band)
    # star on the cone
    pts = []
    for i in range(10):
        a = math.radians(i * 36 - 90)
        r = 11 if i % 2 == 0 else 4.6
        pts.append((128 + math.cos(a) * r, 46 + math.sin(a) * r))
    render(img, poly(pts, round=1.5), band)

CHARS = {
    "avo": ch_avo, "egg": ch_egg, "egg2": ch_egg2, "goldegg": ch_goldegg,
    "berry": ch_berry, "brocc": ch_brocc, "donut": ch_donut, "pickle": ch_pickle,
    "tomato": ch_tomato, "pine": ch_pine, "shroom": ch_shroom, "sushi": ch_sushi,
    "nana": ch_nana, "plum": ch_plum, "orange": ch_orange, "blueberry": ch_blueberry,
    "carrot": ch_carrot, "dragonfruit": ch_dragonfruit, "melon": ch_melon,
    "onigiri": ch_onigiri, "ghostpepper": ch_ghostpepper, "cosmic": ch_cosmic,
    "bunny": ch_bunny, "pig": ch_pig, "mouse": ch_mouse, "hamster": ch_hamster,
    "ghost": ch_ghost, "frogwizard": ch_frogwizard,
}

DEFAULT_OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                           "..", "..", "unity", "EliminatedGame", "Assets",
                           "Eliminated", "Art", "Chars")

def main():
    out = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT
    only = sys.argv[2].split(",") if len(sys.argv) > 2 else None
    os.makedirs(out, exist_ok=True)
    for cid, fn in CHARS.items():
        if only and cid not in only: continue
        img = Img(S, S)
        fn(img)
        path = os.path.join(out, f"{cid}.png")
        img.save_png(path)
        print(f"  wrote {path}")
    print(f"done: {len(only) if only else len(CHARS)} sprite(s)")

if __name__ == "__main__":
    main()
