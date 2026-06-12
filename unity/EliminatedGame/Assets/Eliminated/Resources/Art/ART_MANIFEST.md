# Generated arena art (our own procedural output, no third-party license)

Produced by `tools/ArtGen/gen_floors.py`. 32-bit TGA, **512x512**, seamless/tileable,
themed to the web build's dark/neon "Squid Game" palette. Loaded by `ArenaView`
via `Resources.Load<Texture2D>("Art/floor_<theme>")` and tiled across the arena.

- `floor_courtyard.tga` — dark teal stone tiles, glowing teal grout
- `floor_neon.tga`      — near-black Tron grid: cyan + pink glowing lines
- `floor_candy.tga`     — hot-pink / soft-pink diagonal stripes + sprinkles
- `floor_toxic.tga`     — dark base with glowing radioactive-green puddles
- `floor_beach.tga`     — warm sand with subtle dune ripples (the bright arena)
- `floor_haunt.tga`     — dark purple stone with glowing organic cracks

Regenerate: `python3 tools/ArtGen/gen_floors.py <out_dir>` (stdlib only; also emits
PNG previews). NOTE: the legacy `tools/ArtGen/Program.cs` produces the old flat
checkerboard floors — do **not** run it for floors or it will overwrite these.
