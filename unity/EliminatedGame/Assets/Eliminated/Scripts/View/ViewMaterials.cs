using UnityEngine;

namespace Eliminated.Game.View
{
    /// <summary>
    /// Provides a single shared material that renders under whichever pipeline is
    /// active (URP if a pipeline asset is assigned, otherwise the Built-in
    /// fallback). Per-object color is applied with a MaterialPropertyBlock so we
    /// never instantiate a material per blob. Placeholder for the 2.5D slice;
    /// Phase 3 replaces blobs with real models + proper URP materials.
    /// </summary>
    public static class ViewMaterials
    {
        private static Material _shared;

        public static Material Shared
        {
            get
            {
                if (_shared == null)
                {
                    var shader =
                        Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Standard") ??
                        Shader.Find("Sprites/Default");
                    _shared = new Material(shader) { name = "EliminatedBlobShared", enableInstancing = true };
                }
                return _shared;
            }
        }

        /// <summary>Set a renderer's color via a property block (no material clone).</summary>
        public static void SetColor(Renderer r, MaterialPropertyBlock mpb, Color c)
        {
            r.GetPropertyBlock(mpb);
            if (Shared.HasProperty("_BaseColor")) mpb.SetColor("_BaseColor", c);
            if (Shared.HasProperty("_Color")) mpb.SetColor("_Color", c);
            r.SetPropertyBlock(mpb);
        }
    }
}
