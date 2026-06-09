using UnityEngine;
using Eliminated.Sim.Model;
using Eliminated.Game.Accessibility;
using Eliminated.Game.SimBridge;

namespace Eliminated.Game.View
{
    /// <summary>
    /// One blob's visual: a body sphere plus a small "nose" cube that shows
    /// facing/aim (important for the combat games). Placeholder geometry for the
    /// vertical slice — swapped for real 3D blob models in Phase 3. Position is
    /// smoothed each frame so 20 Hz simulation reads as fluid motion.
    /// </summary>
    public sealed class BlobView
    {
        public readonly GameObject Root;
        private readonly Transform _body;
        private readonly Transform _nose;
        private readonly Renderer _bodyRenderer;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        private Vector3 _target;

        public BlobView()
        {
            Root = new GameObject("Blob");

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(Root.transform, false);
            _body = body.transform;
            _bodyRenderer = body.GetComponent<Renderer>();
            _bodyRenderer.sharedMaterial = ViewMaterials.Shared;

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(Root.transform, false);
            _nose = nose.transform;
            var noseR = nose.GetComponent<Renderer>();
            noseR.sharedMaterial = ViewMaterials.Shared;
            var noseMpb = new MaterialPropertyBlock();
            ViewMaterials.SetColor(noseR, noseMpb, new Color(0.1f, 0.1f, 0.12f));
        }

        public void Bind(Actor a)
        {
            _target = LogicalSpace.ToWorld(a.Pos);
            Root.transform.position = _target;
        }

        public void Render(Actor a, float dt)
        {
            if (a == null || !a.Alive)
            {
                if (Root.activeSelf) Root.SetActive(false);
                return;
            }
            if (!Root.activeSelf) Root.SetActive(true);

            Color col = a.Team >= 0 ? Palette.Team(a.Team) : Palette.Body(a.CharacterId);
            if (a.Frozen) col = Color.Lerp(col, new Color(0.6f, 0.85f, 1f), 0.6f);
            if (a.Burning) col = Color.Lerp(col, Palette.Danger, 0.5f);
            if (a.Ghost) col = Color.Lerp(col, Color.white, 0.5f);
            if (a.Shield) col = Color.Lerp(col, Palette.Safe, 0.35f);
            ViewMaterials.SetColor(_bodyRenderer, _mpb, col);

            _target = LogicalSpace.ToWorld(a.Pos);
            Root.transform.position = Vector3.Lerp(Root.transform.position, _target, 1f - Mathf.Exp(-18f * dt));

            float d = LogicalSpace.WorldRadius(a.Radius) * 2f;
            _body.localScale = Vector3.one * d;
            _body.localPosition = new Vector3(0f, d * 0.5f, 0f);

            // nose sits just ahead of the body in the facing direction
            float nz = d * 0.5f;
            _nose.localScale = Vector3.one * (d * 0.28f);
            _nose.localPosition = new Vector3(0f, d * 0.5f, nz);
            Root.transform.rotation = LogicalSpace.FacingToRotation(a.Facing);
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }
    }
}
