#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Eliminated.Game.EditorTools
{
    /// <summary>
    /// Builds single-sprite character prefabs from our OWN generated kawaii
    /// sprites (tools/ArtGen/gen_chars.py → Assets/Eliminated/Art/Chars/*.png):
    /// the food/fruit/veg roster, the leftover critters and the frog wizard.
    /// No third-party assets — the PNGs ship in the repo, so unlike the Asset
    /// Store packs these prefabs always resolve.
    ///
    /// Each prefab is a root + one "Body" SpriteRenderer — exactly the shape of
    /// the LayerLab slime prefab, so PlayerView/CharacterPreview treat them the
    /// same way (generic accessory anchors, default face box). Textures are
    /// imported readable so CharacterArt.DetectEyes can fit eyewear.
    ///
    /// Output → Resources/Chars/&lt;name&gt;.prefab, consumed by CharacterArt/PlayerView.
    /// Run: <b>Tools ▸ Eliminated ▸ Build Generated Char Prefabs</b> (also runs
    /// automatically once on project load when new sprites appear).
    /// </summary>
    public static class GeneratedCharPrefabBuilder
    {
        private const string SrcDir = "Assets/Eliminated/Art/Chars";
        private const string OutDir = "Assets/Eliminated/Resources/Chars";

        // Build the prefabs automatically when a generated sprite exists without
        // its prefab (first import, or a new character was added to gen_chars.py).
        [InitializeOnLoadMethod]
        private static void AutoBuildOnce()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (!Directory.Exists(SrcDir)) return;
                foreach (var png in Directory.GetFiles(SrcDir, "*.png"))
                {
                    var name = Path.GetFileNameWithoutExtension(png);
                    if (AssetDatabase.LoadAssetAtPath<GameObject>($"{OutDir}/{name}.prefab") == null)
                    {
                        BuildAll();
                        return;
                    }
                }
            };
        }

        [MenuItem("Tools/Eliminated/Build Generated Char Prefabs")]
        public static void BuildAll()
        {
            if (!Directory.Exists(SrcDir))
            {
                Debug.LogError($"[GeneratedCharPrefabBuilder] {SrcDir} not found. Run tools/ArtGen/gen_chars.py first.");
                return;
            }
            Directory.CreateDirectory(OutDir);

            int made = 0;
            foreach (var png in Directory.GetFiles(SrcDir, "*.png"))
            {
                var path = png.Replace('\\', '/');
                var name = Path.GetFileNameWithoutExtension(path);

                EnsureImportSettings(path);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                {
                    Debug.LogWarning($"[GeneratedCharPrefabBuilder] No sprite at {path}; skipped.");
                    continue;
                }

                var root = new GameObject(name);
                var bodyGo = new GameObject("Body");
                bodyGo.transform.SetParent(root.transform, false);
                bodyGo.AddComponent<SpriteRenderer>().sprite = sprite;

                PrefabUtility.SaveAsPrefabAsset(root, $"{OutDir}/{name}.prefab");
                Object.DestroyImmediate(root);
                made++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[GeneratedCharPrefabBuilder] Built {made} prefab(s) into {OutDir}. Mappings live in CharacterArt.cs.");
        }

        private static void EnsureImportSettings(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            bool dirty =
                imp.textureType != TextureImporterType.Sprite ||
                imp.spriteImportMode != SpriteImportMode.Single ||
                !imp.isReadable || imp.mipmapEnabled ||
                imp.textureCompression != TextureImporterCompression.Uncompressed;
            if (!dirty) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = 256f;
            imp.isReadable = true;            // CharacterArt.DetectEyes needs GetPixels32
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
    }
}
#endif
