using System.Collections.Generic;
using UnityEngine;
using Eliminated.Sim.Model;
using Eliminated.Sim.Economy;
using Eliminated.Game.Accessibility;
using Eliminated.Game.SimBridge;

namespace Eliminated.Game.View
{
    /// <summary>
    /// One actor's visual. If the character has real art (a sprite-prefab in
    /// <see cref="CharacterArt"/>) we instantiate it as an upright, camera-facing
    /// sprite stack; otherwise we render the procedural player (a body + a facing
    /// "nose"). Either way position is smoothed each frame so the 20 Hz sim reads
    /// as fluid motion. Real per-character models arrive pack by pack via
    /// <see cref="CharacterArt"/>; until then a character uses the player.
    /// </summary>
    public sealed class PlayerView
    {
        public readonly GameObject Root;

        // --- procedural player (fallback when a character has no art prefab) ---
        private readonly GameObject _playerGo; // parent of body + nose, toggled off when art is used
        private readonly Transform _body;
        private readonly Transform _nose;
        private readonly Renderer _bodyRenderer;
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        // --- real sprite-prefab art (when available) ---
        private GameObject _art;
        private SpriteRenderer[] _artRenderers;
        private SpriteRenderer _faceR, _headR; // the art's face/head parts, for anchoring worn accessories
        private float _artScale;  // unit-height → target-height multiplier
        private float _artFootY;  // local y of the art's lowest point at scale 1 (to plant feet on the ground)
        private string _boundId;  // character id the art was built for
        private float _faceSign = 1f;

        private Vector3 _target;
        private Vector3 _prevWorld;
        private bool _hasPrev;
        private float _age;    // seconds visible — drives the spawn-pop
        private float _phase;  // per-player offset so idle motion de-syncs

        // Art height in world units = WorldRadius(actor.Radius) * this. One knob to
        // resize all sprite characters relative to the arena; tweak in Unity if needed.
        private const float ArtHeightFactor = 4.4f;

        // Worn accessories: head-anchored, camera-facing billboards (procedural art).
        private readonly Dictionary<string, GameObject> _accObjs = new Dictionary<string, GameObject>();
        private string _accKey = "\0";

        // Eliminated players are sealed in place into a "present coffin" — a black
        // gift box with a hot-pink ribbon + bow — instead of vanishing, matching
        // the web's Squid Game drawCoffin. Built lazily the first time this actor dies.
        private GameObject _coffin;
        private Transform _coffinDrop; // child that runs the drop-in / squash bounce
        private bool _coffinActive;
        private float _coffinAge;

        // Glass-bridge death: a plummet through the shattered pane instead of a coffin.
        private bool _falling;
        private float _fallAge;

        public PlayerView()
        {
            Root = new GameObject("Player");

            _playerGo = new GameObject("PlayerBody");
            _playerGo.transform.SetParent(Root.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(_playerGo.transform, false);
            _body = body.transform;
            _bodyRenderer = body.GetComponent<Renderer>();
            _bodyRenderer.sharedMaterial = ViewMaterials.Shared;
            var player = LoadPlayerMesh();
            if (player != null) body.GetComponent<MeshFilter>().sharedMesh = player;

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(nose.GetComponent<Collider>());
            nose.transform.SetParent(_playerGo.transform, false);
            _nose = nose.transform;
            var noseR = nose.GetComponent<Renderer>();
            noseR.sharedMaterial = ViewMaterials.Shared;
            ViewMaterials.SetColor(noseR, new MaterialPropertyBlock(), new Color(0.1f, 0.1f, 0.12f));

            _phase = (Root.GetInstanceID() & 1023) * 0.0131f;
        }

        public void Bind(Actor a, Vector2? overrideLogical = null, string presentCharId = null)
        {
            _target = overrideLogical.HasValue
                ? LogicalSpace.ToWorld(overrideLogical.Value.x, overrideLogical.Value.y)
                : LogicalSpace.ToWorld(a.Pos);
            Root.transform.position = _target;
            Root.transform.rotation = Quaternion.identity;
            _prevWorld = _target;
            _hasPrev = false;
            _age = 0f;
            EnsureArt(a, presentCharId ?? a.CharacterId);
        }

        /// <summary>Attach the presented character's art prefab (once), or keep the
        /// player. <paramref name="charId"/> is normally the actor's own character,
        /// but a disguised actor presents a BORROWED identity to everyone else; the
        /// id can change mid-life (disguise on/off), so we rebind when it does.</summary>
        private void EnsureArt(Actor a, string charId)
        {
            if (_boundId == charId) return;
            _boundId = charId;

            if (_art != null) { Object.Destroy(_art); _art = null; _artRenderers = null; }
            _faceR = _headR = null;

            var prefab = CharacterArt.Load(charId);
            if (prefab == null) { _playerGo.SetActive(true); return; }

            _art = Object.Instantiate(prefab, Root.transform);
            _art.transform.localPosition = Vector3.zero;
            _art.transform.localRotation = Quaternion.identity;
            _art.transform.localScale = Vector3.one;
            _artRenderers = _art.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var r in _artRenderers)
            {
                var rn = r.gameObject.name.ToLowerInvariant();
                if (_headR == null && rn.Contains("head")) _headR = r;
                if (_faceR == null && rn.Contains("face")) _faceR = r;
            }
            if (_faceR == null) _faceR = _headR;

            // Size to a target height and plant the feet on the ground. Bounds are
            // world-space; Root sits on the floor (y=0) at bind, so they're ground-relative.
            if (_artRenderers != null && _artRenderers.Length > 0)
            {
                var b = _artRenderers[0].bounds;
                for (int i = 1; i < _artRenderers.Length; i++) b.Encapsulate(_artRenderers[i].bounds);
                float h = Mathf.Max(0.01f, b.size.y);
                float target = LogicalSpace.WorldRadius(a.Radius) * ArtHeightFactor;
                _artScale = target / h;
                _artFootY = b.min.y - Root.transform.position.y;
            }
            else { _artScale = 1f; _artFootY = 0f; }

            _playerGo.SetActive(false); // hide the procedural player; the art replaces it
        }

        public void Render(Actor a, float dt, Vector2? overrideLogical = null, float? overrideFacing = null, bool fallDeath = false, bool airborne = false, string presentCharId = null)
        {
            if (a == null)
            {
                if (Root.activeSelf) { Root.SetActive(false); _age = 0f; _hasPrev = false; }
                return;
            }
            // An explicit view-driven plummet (glass-bridge shatter, tug-of-war pit) takes
            // precedence over the alive check: tug hauls the LOSING TEAM — still flagged alive
            // until the round resolves — over the edge, so the drop must fire while alive too.
            if (fallDeath) { FallAway(dt); return; }
            if (!a.Alive)
            {
                ShowCoffin(a, dt, overrideLogical); // sealed into a present coffin in place
                return;
            }
            if (_coffinActive) RestoreFromCoffin();      // alive again on a fresh round
            if (_falling) RestoreFromFall();
            if (!Root.activeSelf) { Root.SetActive(true); _age = 0f; _hasPrev = false; }
            _age += dt;

            // Bespoke games (tug, jump, glass, rps…) stage actors by game state, not by
            // their raw sim position — ArenaView supplies a logical override so the player
            // lines up with the scene's props (the rope, the bridge, the duel).
            _target = overrideLogical.HasValue
                ? LogicalSpace.ToWorld(overrideLogical.Value.x, overrideLogical.Value.y)
                : LogicalSpace.ToWorld(a.Pos);
            if (airborne) _target += Vector3.up * 1.4f; // jump rope: a clear hop off the deck
            Vector3 cur = Vector3.Lerp(Root.transform.position, _target, 1f - Mathf.Exp(-18f * dt));
            Root.transform.position = cur;

            Vector3 vel = (_hasPrev && dt > 1e-5f) ? (cur - _prevWorld) / dt : Vector3.zero;
            vel.y = 0f;
            _prevWorld = cur;
            _hasPrev = true;
            float speed = vel.magnitude;

            // 🥸 Disguise: present a borrowed identity to everyone but yourself. The
            // id can flip mid-life, so rebind the art if it changed.
            bool disguised = presentCharId != null && presentCharId != a.CharacterId;
            string effId = disguised ? presentCharId : a.CharacterId;
            EnsureArt(a, effId);

            if (_art != null) RenderArt(a, vel, speed, overrideFacing);
            else RenderPlayer(a, vel, speed, overrideFacing, effId);

            float visualH = LogicalSpace.WorldRadius(a.Radius) * (_art != null ? ArtHeightFactor : 2f);
            SyncAccessories(a, visualH, disguised); // hide your own cosmetics behind the mask
        }

        // ── Present coffin (elimination) ─────────────────────────────────
        // Drops a hot-pink-ribboned gift box in over the spot where the player died,
        // hiding its living visuals. Mirrors the web's drawCoffin, including the
        // little fall-from-above and squash-bounce on landing.
        private void ShowCoffin(Actor a, float dt, Vector2? overrideLogical = null)
        {
            if (!Root.activeSelf) Root.SetActive(true);
            if (_coffin == null) BuildCoffin();
            if (!_coffinActive)
            {
                _coffinActive = true;
                _coffinAge = 0f;
                _playerGo.SetActive(false);
                if (_art != null) _art.SetActive(false);
                foreach (var kv in _accObjs) if (kv.Value != null) kv.Value.SetActive(false);
                _coffin.SetActive(true);
                // Settle at the death spot (Pos is frozen once dead) — no slide-in.
                Root.transform.position = overrideLogical.HasValue
                    ? LogicalSpace.ToWorld(overrideLogical.Value.x, overrideLogical.Value.y)
                    : LogicalSpace.ToWorld(a.Pos);
                Root.transform.rotation = Quaternion.identity;
                // A coffin is a coffin — always standard size, never the powerup scale
                // the player happened to die at (a Shrunk player shouldn't get a doll coffin).
                float r = LogicalSpace.WorldRadius(26f); // 26 = Constants.PlayerRadius
                _coffin.transform.localScale = Vector3.one * (r / 0.52f); // 0.52 = WorldRadius(26)
            }
            _coffinAge += dt;
            float dropY = 0f, sx = 1f, sy = 1f;
            if (_coffinAge < 0.30f) { float p = _coffinAge / 0.30f; dropY = (1f - p) * (1f - p) * 3.1f; }
            else if (_coffinAge < 0.50f) { float b = Mathf.Sin((_coffinAge - 0.30f) / 0.20f * Mathf.PI); sx = 1f + b * 0.18f; sy = 1f - b * 0.18f; }
            _coffinDrop.localPosition = new Vector3(0f, dropY, 0f);
            _coffinDrop.localScale = new Vector3(sx, sy, sx);
        }

        // Glass bridge: the player drops through the shattered glass — accelerate downward,
        // tumble, shrink — then vanish off the map (no coffin, no body left behind).
        private void FallAway(float dt)
        {
            if (!Root.activeSelf) Root.SetActive(true);
            if (!_falling)
            {
                _falling = true; _fallAge = 0f;
                if (_coffin != null) _coffin.SetActive(false);
                _coffinActive = false;
                _playerGo.SetActive(_art == null);
                if (_art != null) _art.SetActive(true);
                foreach (var kv in _accObjs) if (kv.Value != null) kv.Value.SetActive(false);
            }
            _fallAge += dt;
            float p = _fallAge / 0.7f;
            if (p >= 1f) { Root.SetActive(false); return; }
            var pos = Root.transform.position;
            pos.y = -(p * p) * 7f;                                          // accelerate downward through the glass
            Root.transform.position = pos;
            Root.transform.Rotate(Vector3.forward, dt * 420f, Space.Self);  // tumble as they fall
            Root.transform.localScale = Vector3.one * (1f - 0.55f * p);
        }

        private void RestoreFromFall()
        {
            _falling = false;
            Root.transform.localScale = Vector3.one;
            Root.transform.rotation = Quaternion.identity;
            var p = Root.transform.position; p.y = 0f; Root.transform.position = p;
            _hasPrev = false;
        }

        private void RestoreFromCoffin()
        {
            _coffinActive = false;
            if (_coffin != null) _coffin.SetActive(false);
            _playerGo.SetActive(_art == null);
            if (_art != null) _art.SetActive(true);
            foreach (var kv in _accObjs) if (kv.Value != null) kv.Value.SetActive(true);
            _age = 0f; _hasPrev = false;
        }

        private void BuildCoffin()
        {
            _coffin = new GameObject("PresentCoffin");
            _coffin.transform.SetParent(Root.transform, false);
            _coffinDrop = new GameObject("Drop").transform;
            _coffinDrop.SetParent(_coffin.transform, false);

            var box = new Color(0.14f, 0.14f, 0.17f);   // dark gift box
            var lid = new Color(0.20f, 0.20f, 0.25f);
            var ribbon = new Color(1f, 0.18f, 0.53f);    // hot pink #ff2e88
            var bow = new Color(1f, 0.35f, 0.65f);

            // World-unit sizes for a radius-26 player; the whole coffin is scaled to
            // the actor's radius in ShowCoffin. Camera is tilted, so the lid cross +
            // bow on top read the box as a wrapped present from the top-down view.
            CoffinPrim(PrimitiveType.Cube,   new Vector3(0f, 0.26f, 0f),    new Vector3(1.02f, 0.52f, 0.72f), box);    // casket body
            CoffinPrim(PrimitiveType.Cube,   new Vector3(0f, 0.56f, 0f),    new Vector3(1.12f, 0.14f, 0.82f), lid);    // lid
            CoffinPrim(PrimitiveType.Cube,   new Vector3(0f, 0.64f, 0f),    new Vector3(1.14f, 0.06f, 0.18f), ribbon); // ribbon across (on lid)
            CoffinPrim(PrimitiveType.Cube,   new Vector3(0f, 0.64f, 0f),    new Vector3(0.18f, 0.06f, 0.84f), ribbon); // ribbon along (on lid)
            CoffinPrim(PrimitiveType.Cube,   new Vector3(0f, 0.26f, 0.37f), new Vector3(0.18f, 0.54f, 0.04f), ribbon); // ribbon down the front
            CoffinPrim(PrimitiveType.Sphere, new Vector3(0f, 0.70f, 0f),    new Vector3(0.26f, 0.18f, 0.26f), bow);    // bow
        }

        private void CoffinPrim(PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_coffinDrop, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var rend = go.GetComponent<Renderer>();
            rend.sharedMaterial = ViewMaterials.Shared;
            ViewMaterials.SetColor(rend, new MaterialPropertyBlock(), color);
        }

        private void SyncAccessories(Actor a, float H, bool hide = false)
        {
            // While disguised, your own worn cosmetics would give the mask away — drop them.
            string key = (hide || a.Accessories == null || a.Accessories.Count == 0) ? "" : string.Join(",", a.Accessories);
            if (key != _accKey)
            {
                _accKey = key;
                foreach (var kv in _accObjs) if (kv.Value != null) Object.Destroy(kv.Value);
                _accObjs.Clear();
                if (a.Accessories != null)
                    foreach (var id in a.Accessories)
                    {
                        var sprite = AccessoryArt.Get(id);
                        if (sprite == null || _accObjs.ContainsKey(id)) continue;
                        var go = new GameObject("Acc_" + id);
                        go.transform.SetParent(Root.transform, false);
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.sprite = sprite;
                        sr.sortingOrder = 60; // in front of the character's own sprites
                        _accObjs[id] = go;
                    }
            }
            if (_accObjs.Count == 0) return;

            Vector3 basePos = Root.transform.position;
            foreach (var kv in _accObjs)
            {
                if (kv.Value == null) continue;
                var t = kv.Value.transform;
                if (PlaceAccessory(a, Cosmetics.SlotOf(kv.Key), out var pos, out var rotZ, out var scl))
                {
                    t.position = pos;
                    t.rotation = Quaternion.Euler(0f, 0f, rotZ); // tilt to match the eyes (glasses) / upright otherwise
                    t.localScale = Vector3.one * scl;
                }
                else // procedural player / unmapped art → generic head-anchored billboard
                {
                    var anchor = AccessoryAnchor(Cosmetics.SlotOf(kv.Key), H);
                    t.position = basePos + anchor.off;
                    t.rotation = Quaternion.identity;
                    t.localScale = Vector3.one * anchor.scale;
                }
            }
        }

        private static (Vector3 off, float scale) AccessoryAnchor(string slot, float H)
        {
            switch (slot)
            {
                case "head": return (new Vector3(0f, H * 0.95f, -0.05f), H * 0.85f);
                case "eyes": return (new Vector3(0f, H * 0.66f, -0.06f), H * 0.72f);
                case "neck": return (new Vector3(0f, H * 0.40f, -0.06f), H * 0.60f);
                case "ear":  return (new Vector3(H * 0.30f, H * 0.78f, -0.06f), H * 0.46f);
                default:     return (new Vector3(0f, H * 0.90f, -0.05f), H * 0.60f);
            }
        }

        // Anchor a worn accessory to the character's ACTUAL art (Face/Head renderer world bounds)
        // instead of a generic height — mirroring the verified menu preview. Glasses fit a lens
        // onto each real eye (tilted); hats/collars/flower sit on the head box. Returns false for
        // the procedural player (no art parts), so the caller uses the generic billboard fallback.
        private bool PlaceAccessory(Actor a, string slot, out Vector3 pos, out float rotZ, out float scale)
        {
            pos = Vector3.zero; rotZ = 0f; scale = 1f;
            if (_art == null) return false;
            bool flip = _faceSign < 0f;

            if (slot == "eyes" && _faceR != null && CharacterArt.TryEyes(a.CharacterId, out var eL, out var eR, out _))
            {
                var b = _faceR.bounds; // world AABB of the face features
                Vector2 e1 = flip ? new Vector2(1f - eL.x, eL.y) : eL;
                Vector2 e2 = flip ? new Vector2(1f - eR.x, eR.y) : eR;
                Vector3 p1 = new Vector3(b.min.x + e1.x * b.size.x, b.min.y + e1.y * b.size.y, b.center.z);
                Vector3 p2 = new Vector3(b.min.x + e2.x * b.size.x, b.min.y + e2.y * b.size.y, b.center.z);
                Vector3 L = p1.x <= p2.x ? p1 : p2, R = p1.x <= p2.x ? p2 : p1;
                float dist = Vector2.Distance(new Vector2(L.x, L.y), new Vector2(R.x, R.y));
                if (dist < 1e-4f) return false;
                pos = (L + R) * 0.5f; pos.z = b.center.z - 0.05f;
                rotZ = Mathf.Atan2(R.y - L.y, R.x - L.x) * Mathf.Rad2Deg;
                scale = dist / CharacterArt.EyewearLensFrac; // accessory sprite is ~1 world unit
                return true;
            }

            var hr = _headR != null ? _headR : _faceR;
            if (hr == null) return false;
            var hb = hr.bounds;
            float cx = hb.center.x, w = hb.size.x, hgt = hb.size.y, bot = hb.min.y, top = hb.max.y, z = hb.center.z - 0.05f;
            switch (slot)
            {
                case "head": pos = new Vector3(cx, top + w * 0.12f, z);   scale = w * 1.00f; break; // hat above the crown
                case "eyes": pos = new Vector3(cx, bot + hgt * 0.55f, z); scale = w * 0.62f; break; // glasses centred (no eye data)
                case "neck": pos = new Vector3(cx, bot + hgt * 0.02f, z); scale = w * 0.80f; break; // collar at the neck
                case "ear":  pos = new Vector3(hb.min.x + w * (flip ? 0.32f : 0.68f), bot + hgt * 0.69f, z); scale = w * 0.52f; break; // flower beside the ear
                default:     pos = new Vector3(cx, top, z);               scale = w * 0.80f; break;
            }
            return true;
        }

        // Real sprite art: an upright, camera-facing stack. The camera has no yaw
        // (ArenaView only pitches it down), so keeping Root unrotated leaves the
        // sprites facing the camera; we mirror on X to face the travel direction.
        private void RenderArt(Actor a, Vector3 vel, float speed, float? overrideFacing = null)
        {
            Root.transform.rotation = Quaternion.identity;
            if (overrideFacing.HasValue) _faceSign = Mathf.Cos(overrideFacing.Value) >= 0f ? 1f : -1f;
            else if (Mathf.Abs(vel.x) > 0.05f) _faceSign = vel.x >= 0f ? 1f : -1f;

            float pop = SpawnPop(_age);
            float bob = 1f + 0.03f * Mathf.Sin((Time.time + _phase) * 3.2f);
            // _artScale is fixed at bind time (for Scale==1); fold in the LIVE powerup
            // scale so Shrink/Embiggen actually resize the sprite, not just the hitbox.
            float k = _artScale * pop * (a.Scale > 0f ? a.Scale : 1f);
            _art.transform.localScale = new Vector3(k * _faceSign, k * bob, k);
            _art.transform.localPosition = new Vector3(0f, -_artFootY * k, 0f);

            // Status tint only — don't repaint the art with the player body color.
            Color tint = Color.white;
            if (a.Team >= 0) tint = Color.Lerp(Color.white, Palette.Team(a.Team), 0.30f);
            if (a.Frozen) tint = Color.Lerp(tint, new Color(0.6f, 0.85f, 1f), 0.55f);
            if (a.Burning) tint = Color.Lerp(tint, Palette.Danger, 0.45f);
            if (a.Shield) tint = Color.Lerp(tint, Palette.Safe, 0.30f);
            tint.a = a.Ghost ? 0.55f : 1f;
            if (_artRenderers != null)
                foreach (var r in _artRenderers)
                {
                    if (r == null) continue;
                    // leave the prefab's own drop-shadow sprite dark/untinted
                    if (r.gameObject.name.ToLowerInvariant().Contains("shadow")) continue;
                    r.color = tint;
                }
        }

        private void RenderPlayer(Actor a, Vector3 vel, float speed, float? overrideFacing = null, string presentCharId = null)
        {
            Color col = a.Team >= 0 ? Palette.Team(a.Team) : Palette.Body(presentCharId ?? a.CharacterId);
            if (a.Frozen) col = Color.Lerp(col, new Color(0.6f, 0.85f, 1f), 0.6f);
            if (a.Burning) col = Color.Lerp(col, Palette.Danger, 0.5f);
            if (a.Ghost) col = Color.Lerp(col, Color.white, 0.5f);
            if (a.Shield) col = Color.Lerp(col, Palette.Safe, 0.35f);
            ViewMaterials.SetColor(_bodyRenderer, _mpb, col);

            float d = LogicalSpace.WorldRadius(a.Radius) * 2f;
            float pop = SpawnPop(_age);
            float breathe = 1f + 0.04f * Mathf.Sin((Time.time + _phase) * 3.2f);
            float baseScale = d * pop * breathe;

            float k = Mathf.Clamp01(speed / 6f);
            if (speed > 0.05f)
            {
                _body.rotation = Quaternion.LookRotation(vel / speed, Vector3.up);
                _body.localScale = new Vector3(
                    baseScale * (1f - 0.18f * k), baseScale, baseScale * (1f + 0.30f * k));
            }
            else
            {
                _body.localRotation = Quaternion.identity;
                _body.localScale = Vector3.one * baseScale;
            }
            _body.localPosition = new Vector3(0f, d * 0.5f, 0f);

            float nz = d * 0.5f;
            _nose.localScale = Vector3.one * (d * 0.28f * pop);
            _nose.localPosition = new Vector3(0f, d * 0.5f, nz);
            Root.transform.rotation = LogicalSpace.FacingToRotation(overrideFacing ?? a.Facing);
        }

        /// <summary>Ease-out-back from 0→1 over a short duration: a snappy, slightly
        /// overshooting pop when a character first appears.</summary>
        private static float SpawnPop(float t)
        {
            const float dur = 0.28f;
            if (t >= dur) return 1f;
            float x = t / dur;
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float e = 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
            return Mathf.Max(0.02f, e);
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }

        private static Mesh _playerMesh;
        private static bool _playerTried;
        private static Mesh LoadPlayerMesh()
        {
            if (_playerTried) return _playerMesh;
            _playerTried = true;
            var go = Resources.Load<GameObject>("Models/player");
            if (go != null)
            {
                var mf = go.GetComponentInChildren<MeshFilter>();
                if (mf != null) _playerMesh = mf.sharedMesh;
            }
            return _playerMesh;
        }
    }
}
