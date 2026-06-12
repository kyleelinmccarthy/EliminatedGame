#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Eliminated.Game.EditorTools
{
    /// <summary>
    /// Builds finished single-character prefabs from the MiMU "2D Animal Character
    /// Pack". That pack is ONE shared 8-part rig (BearCatOwl.prefab:
    /// Head/Body/ArmL/ArmR/LegL/LegR/Tail/Face) plus a texture sheet per animal,
    /// every sheet sliced into the SAME sub-sprite names. So each output is a
    /// <see cref="Recipe"/>: a base sheet, optional per-part swaps from OTHER sheets
    /// (e.g. a demon head on a bear body), parts to hide (e.g. drop the tail), and a
    /// recolor tint. We find each rig part by its current sprite name and swap in the
    /// same-named sub-sprite from the chosen sheet — no hand-placing parts.
    ///
    /// Notes / limits:
    ///  • Tint is a MULTIPLY over existing art (shifts/darkens hue, can't fully
    ///    repaint). Faces are left untinted so eyes stay readable (TintFace overrides).
    ///  • Mixing/recolors are approximations — verify in the Editor and tell me which
    ///    recipe lines to tweak; this table is meant to be edited.
    ///
    /// Output → Resources/Chars/&lt;Out&gt;.prefab, consumed by CharacterArt/PlayerView.
    /// Run: <b>Tools ▸ Eliminated ▸ Build Animal Prefabs</b>.
    /// </summary>
    public static class AnimalPrefabBuilder
    {
        private const string PackRoot = "Assets/2D Animal Character Pack/Sprites/Characters";
        private const string RigPrefab = "Assets/2D Animal Character Pack/Prefabs/BearCatOwl.prefab";
        private const string OutDir = "Assets/Eliminated/Resources/Chars";

        private sealed class Recipe
        {
            public string Out;                          // prefab name in Resources/Chars
            public string Base;                         // default sheet (under PackRoot, no extension)
            public Dictionary<string, string> PartFrom; // part sprite-name -> sheet to pull that part from
            public string[] Hide;                       // part sprite-names to remove entirely
            public Color Tint = Color.white;            // multiply over non-face parts
            public bool TintFace = false;
        }

        private static readonly Recipe[] Recipes =
        {
            // ---- straight species (real art, as authored) ----
            new Recipe { Out = "blackcat", Base = "Cats/Black-cat" },
            new Recipe { Out = "cat",      Base = "Cats/Toon" },
            new Recipe { Out = "cat2",     Base = "Cats/Kanif" },
            new Recipe { Out = "bear",     Base = "Bears/Bear" },
            new Recipe { Out = "owl",      Base = "Owls/Owl" },
            new Recipe { Out = "snowowl",  Base = "Owls/Snow-owl" },
            new Recipe { Out = "cow",      Base = "Cow/Cow" },
            new Recipe { Out = "demon",    Base = "Demons/DemonRed" },
            new Recipe { Out = "devil",    Base = "Demons/DemonBrown" },

            // ---- composed / recolored (experimental; tweak after seeing them) ----
            // koala  = grey bear, tail dropped
            new Recipe { Out = "koala", Base = "Bears/Bear", Hide = new[] { "Tail" },
                         Tint = new Color(0.80f, 0.84f, 0.90f) },
            // aardvark = bear body + demon head (for the long ears/snout), tan tint
            new Recipe { Out = "aardvark", Base = "Bears/Bear",
                         PartFrom = new Dictionary<string, string> { { "Head", "Demons/DemonRed" } },
                         Tint = new Color(0.82f, 0.69f, 0.54f) },
            // fox = cat silhouette (pointy ears) recolored orange
            new Recipe { Out = "fox", Base = "Cats/Kanif", Tint = new Color(1.0f, 0.55f, 0.22f) },
            // sheep = purple demon recolored cream, tail dropped
            new Recipe { Out = "sheep", Base = "Demons/DemonMagen", Hide = new[] { "Tail" },
                         Tint = new Color(0.95f, 0.93f, 0.88f) },
        };

        private static readonly Dictionary<string, Dictionary<string, Sprite>> _sheetCache =
            new Dictionary<string, Dictionary<string, Sprite>>();

        private static Dictionary<string, Sprite> Sheet(string rel)
        {
            if (_sheetCache.TryGetValue(rel, out var d)) return d;
            d = new Dictionary<string, Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{PackRoot}/{rel}.png"))
                if (o is Sprite s) d[s.name] = s;
            _sheetCache[rel] = d;
            return d;
        }

        // Build the animal prefabs automatically the first time the project loads
        // with the pack present — so they exist without anyone finding the menu.
        [InitializeOnLoadMethod]
        private static void AutoBuildOnce()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(OutDir + "/blackcat.prefab") != null) return; // already built
                if (AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab) == null) return;                  // pack not imported
                BuildAll();
            };
        }

        [MenuItem("Tools/Eliminated/Build Animal Prefabs")]
        public static void BuildAll()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);
            if (rig == null)
            {
                Debug.LogError($"[AnimalPrefabBuilder] Rig not found at {RigPrefab}. Is the MiMU pack imported?");
                return;
            }
            Directory.CreateDirectory(OutDir);
            _sheetCache.Clear();

            int made = 0;
            foreach (var r in Recipes)
            {
                var baseSheet = Sheet(r.Base);
                if (baseSheet.Count == 0)
                {
                    Debug.LogWarning($"[AnimalPrefabBuilder] No sub-sprites in {r.Base}; skipped {r.Out}.");
                    continue;
                }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(rig);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = r.Out;

                var hide = new HashSet<string>(r.Hide ?? System.Array.Empty<string>());
                var toDestroy = new List<GameObject>();
                foreach (var sr in inst.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    string part = sr.sprite != null ? sr.sprite.name : null;
                    if (part == null) continue;
                    if (hide.Contains(part)) { toDestroy.Add(sr.gameObject); continue; }

                    var src = baseSheet;
                    if (r.PartFrom != null && r.PartFrom.TryGetValue(part, out var ov)) src = Sheet(ov);
                    if (src.TryGetValue(part, out var rep)) sr.sprite = rep;

                    bool isFace = part.StartsWith("Face");
                    if (!isFace || r.TintFace) sr.color = r.Tint;
                }
                foreach (var go in toDestroy) Object.DestroyImmediate(go);

                PrefabUtility.SaveAsPrefabAsset(inst, $"{OutDir}/{r.Out}.prefab");
                Object.DestroyImmediate(inst);
                made++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[AnimalPrefabBuilder] Built {made} prefab(s) into {OutDir}. Mappings live in CharacterArt.cs.");
        }
    }
}
#endif
