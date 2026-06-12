using UnityEngine;
using UnityEngine.Rendering;

namespace Eliminated.Game.View
{
    /// <summary>
    /// Provides a single shared material whose shader matches the <em>active</em>
    /// render pipeline. This matters: the URP "Lit" shader exists whenever the URP
    /// package is installed, so a naive <c>Shader.Find("Universal Render
    /// Pipeline/Lit")</c> succeeds even when no URP asset is assigned — and URP/Lit
    /// renders solid magenta under the Built-in pipeline (the "everything is pink"
    /// bug). We instead read <see cref="GraphicsSettings.currentRenderPipeline"/> and
    /// only use a URP/HDRP shader when an SRP is genuinely active, falling back to the
    /// Built-in <c>Standard</c> shader otherwise. Per-object color is applied with a
    /// MaterialPropertyBlock so we never instantiate a material per player. Placeholder
    /// for the 2.5D slice; Phase 3 replaces players with real models + proper URP
    /// materials.
    /// </summary>
    public static class ViewMaterials
    {
        private static Material _shared;
        private static Material _props;
        private static Material _propsGround;
        private static Material _propsFloor;

        public static Material Shared
        {
            get
            {
                if (_shared == null)
                {
                    _shared = new Material(PickLitShader())
                        { name = "EliminatedPlayerShared", enableInstancing = true };
                }
                return _shared;
            }
        }

        /// <summary>
        /// A second shared material in alpha-blended (Fade) mode for the flat floor
        /// overlays (Disc/Box/Ball) so their authored RGBA alpha is actually honored.
        /// The Built-in <c>Standard</c> shader defaults to OPAQUE, which silently
        /// ignores _Color alpha — that is why translucent game props (Mingle rooms,
        /// lava pools, dodgeball halves, team rings…) rendered as flat opaque pads.
        /// Kept SEPARATE from <see cref="Shared"/>: making the player/wall material
        /// transparent would disable its depth writes and wreck 3D sorting.
        /// </summary>
        public static Material Props
        {
            get
            {
                if (_props == null)
                {
                    _props = new Material(PickLitShader())
                        { name = "EliminatedPropShared", enableInstancing = true };
                    ConfigureFade(_props);
                }
                return _props;
            }
        }

        /// <summary>
        /// A GROUND variant of <see cref="Props"/> for flat decals that lie on the floor —
        /// lava fills, islands, finish zones, board tiles, room pads, team rings. Identical
        /// alpha-blended Fade setup but a LOWER render queue (2600) so it always paints
        /// AFTER the opaque floor yet BEFORE the transparent character sprites (queue 3000).
        /// That is the deterministic fix for "players render behind the map": a full-arena
        /// fill (King-of-the-Lava's opaque lava, Keepy-Uppy's dusk) at the players' own Z
        /// would otherwise win the transparency sort and hide every sprite in the top half
        /// of the field. Queue order (not Z) guarantees ground decals stay underneath.
        /// </summary>
        public static Material PropsGround
        {
            get
            {
                if (_propsGround == null)
                {
                    _propsGround = new Material(PickLitShader())
                        { name = "EliminatedPropGround", enableInstancing = true };
                    ConfigureFade(_propsGround);
                    _propsGround.renderQueue = 2600; // between opaque floor (2000) and sprite players (3000)
                }
                return _propsGround;
            }
        }

        /// <summary>
        /// The lowest decal tier (queue 2500): full-arena FLOOR FILLS that visually replace
        /// the themed floor — King-of-the-Lava's molten field, Keepy-Uppy's dusk, the
        /// dodgeball/finish-zone washes. A step below <see cref="PropsGround"/> so an opaque
        /// fill can never hide the islands / lines / pads that sit ON it (those are 2600),
        /// while both stay under the characters (3000).
        /// </summary>
        public static Material PropsFloor
        {
            get
            {
                if (_propsFloor == null)
                {
                    _propsFloor = new Material(PickLitShader())
                        { name = "EliminatedPropFloor", enableInstancing = true };
                    ConfigureFade(_propsFloor);
                    _propsFloor.renderQueue = 2500;
                }
                return _propsFloor;
            }
        }

        /// <summary>Switch a Built-in <c>Standard</c> material to alpha-blended Fade mode
        /// and a matte finish (no specular sheen). No-ops on properties a non-Standard
        /// shader lacks.</summary>
        private static void ConfigureFade(Material m)
        {
            if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 2f); // 2 = Fade
            if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000; // Transparent
        }

        /// <summary>
        /// The lit shader for whatever pipeline is actually rendering. With a
        /// Scriptable Render Pipeline assigned (URP/HDRP) we use its lit shader; with
        /// the Built-in pipeline we use <c>Standard</c>. Crucially this never returns
        /// a URP shader while the Built-in pipeline is active — that is the magenta
        /// trap, because the URP shader compiles but has no Built-in subshader.
        /// </summary>
        private static Shader PickLitShader()
        {
            var srp = GraphicsSettings.currentRenderPipeline
                   ?? GraphicsSettings.defaultRenderPipeline;
            if (srp != null)
            {
                return Shader.Find("Universal Render Pipeline/Lit")
                    ?? srp.defaultShader
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Sprites/Default");
            }
            // Built-in pipeline: URP/Lit would render magenta here.
            return Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
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
