using System.Collections.Generic;
using UnityEngine;
using Eliminated.Sim.Core;
using Eliminated.Sim.Games;
using Eliminated.Sim.Model;
using Eliminated.Sim.Room;

namespace Eliminated.Game.View
{
    /// <summary>
    /// Live, looping mini-renders of each minigame for the How-to-Play cards. Each
    /// on-screen game gets a "station": a headless sim that plays (and re-loops) the
    /// game, its own <see cref="ArenaView"/> rig — real floor, rim walls, player sprites,
    /// props — isolated on a private layer, and a texture it refreshes a few times a
    /// second. The card draws that texture, so the preview is the game actually being
    /// played. Stations are built lazily for the cards on screen and torn down when the
    /// page closes (so the rigs never bleed into the live game, which shares the origin).
    /// </summary>
    public static class GamePreview
    {
        private const int LayerBase = 8;    // first private layer; one per station
        private const int MaxStations = 16; // ≤ the number of registered games
        private const int TexW = 360, TexH = 188; // ~1.92:1 — matches the card thumbnail box
        private const int SettleTicks = 12; // a new station opens already mid-action

        private static readonly string[] Cast = { "fox", "koala", "cat", "panther", "cow", "owl", "sheep", "demon" };

        private static GamePreviewService _svc;

        /// <summary>Ask for a game's live preview this frame (call for every on-screen card).</summary>
        public static void Request(GameId id)
        {
            if (_svc == null)
            {
                var go = new GameObject("GamePreviewService");
                Object.DontDestroyOnLoad(go);
                _svc = go.AddComponent<GamePreviewService>();
                _svc.Configure(Cast, LayerBase, MaxStations, TexW, TexH, SettleTicks);
            }
            _svc.Request(id);
        }

        /// <summary>The live texture for a game, or null until its station has rendered once.</summary>
        public static Texture GetLive(GameId id) => _svc != null ? _svc.GetTexture(id) : null;
    }

    public sealed class GamePreviewService : MonoBehaviour
    {
        // How often each station re-renders (gif-like; the sim still ticks every frame),
        // and a cap on GPU read-backs per frame so a full screen of cards can't stall.
        private const float RenderInterval = 1f / 20f;
        private const int ReadbacksPerFrame = 4;

        private sealed class Station
        {
            public GameId Id;
            public int Layer;
            public GameObject Rig;
            public ArenaView Arena;
            public Camera Cam;
            public RenderTexture Rt;
            public Texture2D Tex;
            public IMinigame Game;
            public float Acc;        // sim-tick accumulator
            public float RenderAcc;  // render-cadence accumulator
            public int Seq;          // loop counter (varies the seed per replay)
            public bool Rendered;
        }

        private string[] _cast;
        private int _layerBase, _max, _w, _h, _settle;

        private readonly Dictionary<GameId, Station> _stations = new Dictionary<GameId, Station>();
        private readonly HashSet<GameId> _requested = new HashSet<GameId>();
        private readonly HashSet<GameId> _seenThisFrame = new HashSet<GameId>();
        private int _lastRequestFrame = -999;
        private int _layerCursor;

        public void Configure(string[] cast, int layerBase, int max, int w, int h, int settle)
        {
            _cast = cast; _layerBase = layerBase; _max = max; _w = w; _h = h; _settle = settle;
        }

        public void Request(GameId id)
        {
            _seenThisFrame.Add(id);
            _lastRequestFrame = Time.frameCount;
        }

        public Texture GetTexture(GameId id)
            => _stations.TryGetValue(id, out var s) && s.Rendered ? s.Tex : null;

        private void LateUpdate()
        {
            // Tear everything down once the page stops asking for previews, so the rigs
            // (which sit at the same world origin as the live arena) can't bleed in-game.
            if (Time.frameCount - _lastRequestFrame > 2)
            {
                if (_stations.Count > 0) TeardownAll();
                _seenThisFrame.Clear();
                return;
            }

            _requested.Clear();
            foreach (var id in _seenThisFrame) _requested.Add(id);
            _seenThisFrame.Clear();

            float dt = Time.deltaTime;
            bool builtOne = false;
            int budget = ReadbacksPerFrame;

            foreach (var id in _requested)
            {
                if (!_stations.TryGetValue(id, out var st))
                {
                    if (builtOne || _stations.Count >= _max) continue; // ≤ one new station per frame
                    st = Build(id);
                    if (st == null) continue;
                    _stations[id] = st;
                    builtOne = true;
                }

                TickSim(st, dt);

                st.RenderAcc += dt;
                if (st.RenderAcc >= RenderInterval && budget > 0)
                {
                    st.RenderAcc = 0f;
                    RenderStation(st, Mathf.Max(dt, RenderInterval));
                    budget--;
                }
            }
        }

        private void TickSim(Station st, float dt)
        {
            st.Acc += Mathf.Min(dt, 0.25f);
            int guard = 0;
            while (st.Acc >= Constants.Dt && guard++ < 8)
            {
                st.Acc -= Constants.Dt;
                st.Game.Tick(Constants.Dt);
                if (st.Game.IsDone) { st.Seq++; NewGame(st); break; } // loop the game
            }
        }

        private void RenderStation(Station st, float smoothingDt)
        {
            var snap = st.Game.BuildSnapshot();
            st.Arena.RenderSnapshot(snap, smoothingDt, false);
            SetLayerRecursive(st.Rig, st.Layer); // catch any newly-pooled props
            st.Cam.Render();

            var prevActive = RenderTexture.active;
            RenderTexture.active = st.Rt;
            st.Tex.ReadPixels(new Rect(0, 0, _w, _h), 0, 0);
            st.Tex.Apply(false);
            RenderTexture.active = prevActive;
            st.Rendered = true;
        }

        private Station Build(GameId id)
        {
            int layer = _layerBase + (_layerCursor++ % Mathf.Max(1, _max));
            var rig = new GameObject("PreviewStation_" + id);
            var arena = rig.AddComponent<ArenaView>();
            // Theme each preview by the game's position in the (enum-ordered) card list, so
            // neighbouring How-to-Play cards never share a floor background.
            int themeOrdinal = 0, k = 0;
            foreach (GameId g in System.Enum.GetValues(typeof(GameId)))
            {
                if (!GameCatalog.IsRegistered(g)) continue;
                if (g == id) { themeOrdinal = k; break; }
                k++;
            }
            arena.Init(null, themeOrdinal);
            arena.enabled = false;   // we drive rendering; no auto LateUpdate
            // Confine this rig's directional Sun to its own layer — otherwise every
            // station's light would stack on every other and wash all previews out.
            foreach (var lt in rig.GetComponentsInChildren<Light>(true)) lt.cullingMask = 1 << layer;
            var cam = arena.Camera;
            cam.enabled = false;     // manual render only — never draws to the screen
            cam.cullingMask = 1 << layer;
            cam.aspect = (float)_w / _h;
            arena.Reframe(cam.aspect);

            var rt = new RenderTexture(_w, _h, 24, RenderTextureFormat.ARGB32) { name = "PreviewRT_" + id };
            rt.Create();
            cam.targetTexture = rt;

            var st = new Station
            {
                Id = id, Layer = layer, Rig = rig, Arena = arena, Cam = cam, Rt = rt,
                Tex = new Texture2D(_w, _h, TextureFormat.RGB24, false) { name = "GamePreview_" + id },
            };
            NewGame(st);
            for (int t = 0; t < _settle && !st.Game.IsDone; t++) st.Game.Tick(Constants.Dt); // open mid-action
            return st;
        }

        /// <summary>(Re)start this station's game with a fresh field of bots.</summary>
        private void NewGame(Station st)
        {
            var meta = GameCatalog.Of(st.Id);
            int n = 8;
            if (n < meta.MinPlayers) n = meta.MinPlayers;
            if (meta.RequiresEven && n % 2 != 0) n++;

            var actors = new List<Actor>(n);
            for (int i = 0; i < n; i++)
                actors.Add(new Actor
                {
                    Id = "p" + i, Name = "P" + (i + 1),
                    CharacterId = _cast[i % _cast.Length], Number = i + 1, IsBot = true,
                });

            var ctx = new GameContext
            {
                Actors = actors,
                Rng = new Rng(7919 + (int)st.Id * 101 + st.Seq),
                RoundIndex = 0, TotalRounds = -1, Intensity = 0.35f,
            };
            st.Game = GameCatalog.Create(st.Id, ctx);
            st.Game.Start();
            st.Acc = 0f;
        }

        private void TeardownAll()
        {
            foreach (var st in _stations.Values)
            {
                if (st.Cam != null) st.Cam.targetTexture = null;
                if (st.Rt != null) { st.Rt.Release(); Destroy(st.Rt); }
                if (st.Tex != null) Destroy(st.Tex);
                if (st.Rig != null) Destroy(st.Rig);
            }
            _stations.Clear();
            _requested.Clear();
            _layerCursor = 0;
        }

        private void OnDestroy() => TeardownAll();

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }
    }
}
