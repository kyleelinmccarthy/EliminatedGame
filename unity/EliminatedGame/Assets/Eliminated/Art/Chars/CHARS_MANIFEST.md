# Generated character sprites (our own procedural output, no third-party license)

Produced by `tools/ArtGen/gen_chars.py`. 32-bit PNG, **256x256**, straight alpha,
kawaii blob-with-a-face style matching the imported packs (LayerLab slime, Jovial
cuties, MiMU animals). One sprite per roster character that has no Asset Store art.

`GeneratedCharPrefabBuilder` (Editor) imports each PNG as a readable Single sprite
(256 ppu) and builds a `Resources/Chars/<name>.prefab` — root + one "Body"
SpriteRenderer, same shape as the slime prefab — automatically on project load
(or via **Tools ▸ Eliminated ▸ Build Generated Char Prefabs**). `CharacterArt.cs`
maps sim character ids onto these prefab names.

Characters (28): avo, berry, blueberry, brocc, bunny, carrot, cosmic, donut,
dragonfruit, egg, egg2, frogwizard (sim id `wizard`), ghost, ghostpepper, goldegg,
hamster, melon, mouse, nana, onigiri, orange, pickle, pig, pine, plum, shroom,
sushi, tomato.

Regenerate: `python3 tools/ArtGen/gen_chars.py [out_dir] [comma,separated,names]`
(stdlib only; default out_dir is this folder).

Constraints baked into the art (keep when editing the generator):
- Eyes are the darkest blobs (r,g,b < 85) in the upper part of the sprite so
  `CharacterArt.DetectEyes` finds them for eyewear fitting; nothing else that
  dark may sit further left/right than the eyes (e.g. melon seeds are r=95).
- Face sits at roughly 0.6–0.7 of sprite height to suit PlayerView's generic
  accessory anchors and CharacterPreview's default face box.
- `frogwizard` is named that way to avoid clobbering the Jovial `wizard.prefab`
  used by the `sorcerer` id.
