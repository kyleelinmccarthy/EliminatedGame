#!/usr/bin/env python3
"""
Generates the 6 themed arena floor textures for ELIMINATED as seamless,
tileable 512x512 images in the web build's dark/neon "Squid Game" palette.

Our own procedural output — no third-party assets. Dependency-free (stdlib only),
so it runs anywhere Python 3 does (the C# tools/ArtGen needs a dotnet runtime).
This supersedes the old checkerboard floors in Program.cs.

Writes uncompressed 32-bit TGA (what Unity loads from Resources/Art/floor_<theme>)
and a same-named PNG preview for eyeballing.

Usage:  python3 gen_floors.py [out_dir]
"""
import math, struct, zlib, sys, os

SIZE = 512

# ---- small math helpers ----------------------------------------------------
def clamp(v, lo, hi): return lo if v < lo else hi if v > hi else v
def clampb(v): return int(clamp(v, 0, 255))
def smooth(e0, e1, x):
    if e0 == e1: return 0.0 if x < e0 else 1.0
    t = clamp((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)
def lerp(a, b, t): return a + (b - a) * t
def lerp3(c1, c2, t): return (lerp(c1[0], c2[0], t), lerp(c1[1], c2[1], t), lerp(c1[2], c2[2], t))
def hexc(h): return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))

# ---- tileable value noise --------------------------------------------------
_LAT = {}
def _lattice(period, seed):
    key = (period, seed)
    g = _LAT.get(key)
    if g is None:
        g = [[0.0] * period for _ in range(period)]
        for iy in range(period):
            for ix in range(period):
                s = math.sin(ix * 127.1 + iy * 311.7 + seed * 74.7) * 43758.5453
                g[iy][ix] = s - math.floor(s)
        _LAT[key] = g
    return g
def noise(period, seed, x, y):
    """Bilinear, smoothstep-interpolated, wrapping value noise in [0,1].
    `period` lattice cells span the full 512 px, so it tiles seamlessly."""
    lat = _lattice(period, seed)
    fx = x * period / SIZE
    fy = y * period / SIZE
    x0 = int(math.floor(fx)); y0 = int(math.floor(fy))
    tx = fx - x0; ty = fy - y0
    sx = tx * tx * (3 - 2 * tx); sy = ty * ty * (3 - 2 * ty)
    x0 %= period; y0 %= period
    x1 = (x0 + 1) % period; y1 = (y0 + 1) % period
    a = lerp(lat[y0][x0], lat[y0][x1], sx)
    b = lerp(lat[y1][x0], lat[y1][x1], sx)
    return lerp(a, b, sy)

# distance to nearest gridline whose spacing divides SIZE (so it wraps)
def grid_dist(v, spacing):
    m = v % spacing
    return min(m, spacing - m)

# ---- per-theme pixel shaders (return r,g,b 0..255) -------------------------
def sh_courtyard(x, y, s):
    A = (20, 53, 41); B = (23, 61, 48); teal = (25, 211, 189)
    cell = 128
    alt = ((x // cell) + (y // cell)) % 2 == 0
    c = A if alt else B
    spk = (noise(64, s, x, y) - 0.5) * 10
    c = (c[0] + spk, c[1] + spk, c[2] + spk)
    gd = min(grid_dist(x, cell), grid_dist(y, cell))
    glow = smooth(10, 0, gd) * 0.28
    c = lerp3(c, teal, glow)
    if gd < 1.5: c = lerp3(c, teal, 0.5)
    return c

def sh_neon(x, y, s):
    # Deep blue-violet base with a faint checker (web #1b1f3b / #252a52), soft GLOWING cyan/pink
    # lines (a thin crisp core + a wide dim bloom), gentle base mottling, and a brighter node
    # where lines cross. Was a harsh, flat, pure-saturation magenta/cyan grid (graph paper).
    sp = 64
    alt = ((x // sp) + (y // sp)) % 2 == 0
    b1 = (22, 26, 52); b2 = (30, 35, 66)
    base = b1 if alt else b2
    n = (noise(40, s, x, y) - 0.5) * 14
    r, g, b = base[0] + n, base[1] + n, base[2] + n * 1.2
    cyan = (60, 200, 210); pink = (210, 80, 150)   # slightly muted so they don't scream
    vd = grid_dist(x, sp); hd = grid_dist(y, sp)
    gv = smooth(2.5, 0, vd) * 0.50 + smooth(20, 0, vd) * 0.14
    gh = smooth(2.5, 0, hd) * 0.50 + smooth(20, 0, hd) * 0.14
    r += cyan[0] * gv + pink[0] * gh
    g += cyan[1] * gv + pink[1] * gh
    b += cyan[2] * gv + pink[2] * gh
    inter = gv * gh
    r += 50 * inter; g += 60 * inter; b += 72 * inter
    return (r, g, b)

def sh_candy(x, y, s):
    hot = (255, 46, 136); soft = (255, 209, 228)
    band = (x + y) % 128
    edge = smooth(0, 4, band) - smooth(64, 68, band) + smooth(124, 128, band)
    c = lerp3(soft, hot, clamp(edge, 0, 1))
    d = noise(48, s, x, y)
    if d > 0.90:
        t = smooth(0.90, 0.97, d)
        sprinkle = (255, 206, 58) if (int(x * 0.3 + y * 0.7) % 2 == 0) else (25, 211, 189)
        c = lerp3(c, sprinkle, t)
    return c

def sh_toxic(x, y, s):
    base = (10, 31, 20); green = (76, 217, 160); lime = (140, 245, 150)
    v = noise(6, s, x, y)
    c = lerp3(base, green, smooth(0.52, 0.82, v) * 0.85)
    c = lerp3(c, lime, smooth(0.80, 0.95, v) * 0.6)
    gd = min(grid_dist(x, 128), grid_dist(y, 128))
    if gd < 1.5: c = (c[0] * 0.5, c[1] * 0.5, c[2] * 0.5)
    return c

def sh_beach(x, y, s):
    sand = (216, 192, 138)
    rip = math.sin(2 * math.pi * 7 * y / SIZE + noise(8, s, x, y) * 2.2) * 0.5 + 0.5
    rip2 = math.sin(2 * math.pi * 5 * x / SIZE) * 0.5 + 0.5
    bright = (rip * 14 + rip2 * 6 - 10) + (noise(128, s, x, y) - 0.5) * 12
    return (sand[0] + bright, sand[1] + bright, sand[2] + bright * 0.7)

def sh_haunt(x, y, s):
    base = (29, 23, 48); purple = (126, 87, 194); crack = (8, 6, 16)
    fog = (noise(3, s, x, y) - 0.5) * 18
    c = (base[0] + fog, base[1] + fog, base[2] + fog * 1.3)
    v = noise(5, s, x, y)
    ridge = abs(v - 0.5)
    c = lerp3(c, crack, smooth(0.05, 0.0, ridge))           # dark crack core
    c = lerp3(c, purple, smooth(0.10, 0.045, ridge) * 0.5)  # purple glow around it
    return c

THEMES = {
    "courtyard": (sh_courtyard, 11), "neon": (sh_neon, 23), "candy": (sh_candy, 7),
    "toxic": (sh_toxic, 31), "beach": (sh_beach, 5), "haunt": (sh_haunt, 17),
}

def render(shader, seed):
    rgba = bytearray(SIZE * SIZE * 4)
    i = 0
    for y in range(SIZE):
        for x in range(SIZE):
            r, g, b = shader(x, y, seed)
            rgba[i] = clampb(r); rgba[i+1] = clampb(g); rgba[i+2] = clampb(b); rgba[i+3] = 255
            i += 4
    return rgba

def write_tga(path, rgba):
    hdr = struct.pack("<BBBHHBHHHHBB", 0, 0, 2, 0, 0, 0, 0, 0, SIZE, SIZE, 32, 0x28)
    bgra = bytearray(len(rgba))
    for p in range(0, len(rgba), 4):
        bgra[p] = rgba[p+2]; bgra[p+1] = rgba[p+1]; bgra[p+2] = rgba[p]; bgra[p+3] = rgba[p+3]
    with open(path, "wb") as f:
        f.write(hdr); f.write(bgra)

def write_png(path, rgba):
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff)
    raw = bytearray()
    stride = SIZE * 4
    for y in range(SIZE):
        raw.append(0)
        raw += rgba[y*stride:(y+1)*stride]
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n")
        f.write(chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0)))
        f.write(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        f.write(chunk(b"IEND", b""))

def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "/tmp/elimfloors"
    os.makedirs(out, exist_ok=True)
    for name, (shader, seed) in THEMES.items():
        rgba = render(shader, seed)
        write_tga(os.path.join(out, f"floor_{name}.tga"), rgba)
        write_png(os.path.join(out, f"floor_{name}.png"), rgba)
        print(f"  wrote floor_{name}.tga + .png ({SIZE}x{SIZE})")
    print(f"Done → {out}")

if __name__ == "__main__":
    main()
