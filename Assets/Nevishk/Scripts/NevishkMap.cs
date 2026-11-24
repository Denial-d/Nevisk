using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nevishk
{
    /// <summary>
    /// Builds the 3D overview of Невишк from Resources (lore + layout + base map)
    /// and wires clicks on the map to the lore panel.
    /// Put this on an empty GameObject (menu: Nevishk ▸ Set Up Scene does it for you).
    /// </summary>
    public class NevishkMap : MonoBehaviour
    {
        [Header("Materials (URP Lit). Set by Nevishk ▸ Set Up Scene")]
        public Material litTemplate;
        public Material groundMaterial;

        [Header("World")]
        [Tooltip("World units across the base map image (1280 px).")]
        public float mapWidth = 1280f;
        public float pinHeight = 22f;
        public float districtLabelHeight = 70f;

        [Header("Look")]
        [Tooltip("Vignette, desaturated grade and film grain for the weird-fiction mood.")]
        public bool atmosphere = true;

        LoreDatabase _db;
        LorePanelUI _ui;
        MapCameraController _cam;
        Camera _camera;
        float _mapDepth;
        readonly Dictionary<string, MapPin> _pins = new Dictionary<string, MapPin>();
        readonly Dictionary<Color, Material> _mats = new Dictionary<Color, Material>();
        MapPin _hovered, _selected;
        Transform _root;
        NevishkTerrain _terrain;
        NevishkRailroad _rail;

        /// A 3D building made from an icon on the base map.
        class ModelSpot { public float u, v, rPx; public Vector3 pos; public float top, radius; public string label; }
        readonly List<ModelSpot> _models = new List<ModelSpot>();
        readonly Dictionary<string, ModelSpot> _modelByLandmark = new Dictionary<string, ModelSpot>();

        // ------------------------------------------------------------------ lifecycle

        void Start()
        {
            if (TMP_Settings.instance == null)
            {
                Debug.LogError("[Nevishk] TextMeshPro resources are missing. Stop Play, then use " +
                               "Window ▸ TextMeshPro ▸ Import TMP Essential Resources and press Play again.");
                enabled = false; return;
            }

            _db = LoreDatabase.Load();
            if (_db == null) { enabled = false; return; }

            _camera = Camera.main;
            if (_camera == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camGo.tag = "MainCamera";
                _camera = camGo.GetComponent<Camera>();
            }
            _cam = _camera.GetComponent<MapCameraController>();
            if (_cam == null) _cam = _camera.gameObject.AddComponent<MapCameraController>();

            int[] px = _db.city.mapPixelSize;
            _mapDepth = mapWidth * px[1] / px[0];

            _root = new GameObject("City").transform;
            _root.SetParent(transform, false);

            SetUpAtmosphere();
            BuildGround();
            LoadPaths();
            BuildModels();
            BuildRoads();
            BuildBlocksAndTrees();
            BuildPaths();
            BuildSpecialLandmarks();
            BuildPins();

            bool reduced = NevishkBrowser.PrefersReducedMotion;
            _cam.smoothing = reduced ? 60f : NevishkTheme.CameraSmoothing;   // near-instant camera under reduced motion
            if (atmosphere) SetUpPostProcessing(reduced);

            _ui = gameObject.AddComponent<LorePanelUI>();
            _ui.ReducedMotion = reduced;
            _ui.Build(_db);
            _ui.DistrictClicked += id => SelectDistrict(id, true);
            _ui.LandmarkClicked += id => SelectLandmark(id, true);
            _ui.ResetClicked += ResetView;
            _ui.PanelClosed += () => { SetSelected(null); WriteHash("", !_applyingHash); };
            _ui.EntryShown += (id, isBack) => { if (id != null) WriteHash(id, !isBack && !_applyingHash); };

            _cam.panBounds = new Rect(-mapWidth / 2, -_mapDepth / 2, mapWidth, _mapDepth);
            _cam.maxDistance = mapWidth * 1.4f;          // far enough out to see the country around the city
            _camera.farClipPlane = mapWidth * 7f;
            _camera.nearClipPlane = 1f;
            ResetView();

            SetUpStoryMode();

            // Deep link: https://…/index.html#lm_046 opens that entry directly.
            NevishkBrowser.ListenForBackButton(gameObject.name);
            OnBrowserHashChanged(NevishkBrowser.GetHash());
        }

        // ------------------------------------------------------------------ story mode

        StoryMode _story;

        void SetUpStoryMode()
        {
            var rect = _terrain != null ? _terrain.WorldRect : new Rect(-mapWidth / 2, -_mapDepth / 2, mapWidth, _mapDepth);
            _story = gameObject.AddComponent<StoryMode>();
            _story.cam = _camera; _story.camCtl = _cam; _story.ui = _ui;
            _story.ground = GroundAtWorld;
            _story.anchorOf = id =>
            {
                if (!_pins.TryGetValue(id, out var pin)) return null;
                // Strings tie onto the name tag, like a pin through a card.
                return pin.label != null ? pin.label.transform.position + Vector3.down * 3f : pin.transform.position;
            };
            _story.nameOf = id => _db.District(id)?.name ?? _db.Landmark(id)?.name;
            _story.clampToMap = w => new Vector3(Mathf.Clamp(w.x, rect.xMin + 5f, rect.xMax - 5f), 0f, Mathf.Clamp(w.z, rect.yMin + 5f, rect.yMax - 5f));
            _story.makeLabel = (parent, text) =>
            {
                var l = MakeLabel(parent, text, 40f, NevishkTheme.Text, false);
                var plate = MakePlate(l, 3f, 1.6f);
                plate.color = NevishkTheme.LabelPlate;
                return l;
            };
            _story.material = c => MatFor(c);
            _story.Init();
        }

        /// Surface height (water or ground) anywhere: the city terrain inside its extent, the countryside outside.
        float GroundAtWorld(Vector3 w)
        {
            if (_terrain == null) return 0f;
            var rect = _terrain.WorldRect;
            if (rect.Contains(new Vector2(w.x, w.z))) return Surface(w.x / mapWidth + 0.5f, 0.5f - w.z / _mapDepth);
            return _landscape != null ? _landscape.HeightAt(w.x, w.z) : 0f;
        }

        // ------------------------------------------------------------------ deep links

        bool _applyingHash;

        /// <summary>Called on start and from the browser when the visitor presses Back/Forward.</summary>
        public void OnBrowserHashChanged(string hash)
        {
            _applyingHash = true;
            try
            {
                if (!string.IsNullOrEmpty(hash) && hash.StartsWith("story="))
                {
                    // A shared story scene: open it, then tidy the address so it doesn't reopen on reload.
                    _story?.OpenShared(hash.Substring(6));
                    NevishkBrowser.SetHash("", false);
                }
                else if (string.IsNullOrEmpty(hash)) { if (_ui.PanelOpen) _ui.Close(); }
                else if (_db.District(hash) != null) SelectDistrict(hash, false);
                else if (_db.Landmark(hash) != null) SelectLandmark(hash, false);
                else if (_db.Document(hash) != null) _ui.ShowDocument(hash, false);
            }
            finally { _applyingHash = false; }
        }

        void WriteHash(string id, bool push)
        {
            if (_applyingHash) return;
            NevishkBrowser.SetHash(id, push);
        }

        void Update()
        {
            if (_camera == null) return;
            UpdateHover();
            // Story mode first: it may use this click (placing, dragging, tying a string) or the Esc key.
            bool storyUsed = _story != null && _story.Tick(_hovered);

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!storyUsed && MapInput.PrimaryUp && !_cam.LastPressWasDrag && !overUI && _hovered != null)
            {
                if (_hovered.kind == PinKind.District) SelectDistrict(_hovered.id, false);
                else SelectLandmark(_hovered.id, false);
            }
            if (!storyUsed && MapInput.EscapeDown)
            {
                if (_ui.LightboxOpen) _ui.CloseLightbox();
                else if (_ui.PanelOpen) _ui.Close();
            }
        }

        // ------------------------------------------------------------------ selection

        void SelectDistrict(string id, bool fromPanel)
        {
            var d = _db.District(id);
            if (d == null || !_pins.TryGetValue(id, out var pin)) return;
            SetSelected(pin);
            _ui.ShowDistrict(id, fromPanel && _ui.PanelOpen);
            FlyToWithPanel(OnGround(d.uv), 420f);
        }

        void SelectLandmark(string id, bool fromPanel)
        {
            var l = _db.Landmark(id);
            if (l == null || !_pins.TryGetValue(id, out var pin)) return;
            SetSelected(pin);
            _ui.ShowLandmark(id, fromPanel && _ui.PanelOpen);
            FlyToWithPanel(OnGround(l.uv), 190f);
        }

        void ResetView()
        {
            if (_ui != null) _ui.Close();
            _cam.ResetView(new Vector3(0f, 10f, -_mapDepth * 0.04f), mapWidth * 0.8f);
        }

        void SetSelected(MapPin pin)
        {
            if (_selected != null) _selected.SetSelected(false);
            _selected = pin;
            if (_selected != null) _selected.SetSelected(true);
        }

        // Keep the target visible in the part of the screen the lore panel doesn't cover.
        void FlyToWithPanel(Vector3 target, float dist)
        {
            float panelFrac = Screen.width > Screen.height ? Mathf.Clamp01(620f * ScaleFactor() / Screen.width) : 0f;
            float halfW = dist * Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * _camera.aspect;
            Vector3 right = Quaternion.Euler(0, _cam.yaw, 0) * Vector3.right;
            Vector3 focus = target + right * (panelFrac * halfW);
            if (Screen.height > Screen.width)
            {
                // Portrait: panel covers the bottom ~60%, so push the target up the screen.
                Vector3 fwd = Quaternion.Euler(0, _cam.yaw, 0) * Vector3.forward;
                focus = target - fwd * dist * 0.35f;
            }
            _cam.FlyTo(focus, dist, 50f);
        }

        static float ScaleFactor()
        {
            // Matches LorePanelUI's CanvasScaler (reference resolution, match 0.5).
            float logW = Mathf.Log(Screen.width / LorePanelUI.ReferenceWidth, 2), logH = Mathf.Log(Screen.height / LorePanelUI.ReferenceHeight, 2);
            return Mathf.Pow(2, Mathf.Lerp(logW, logH, 0.5f));
        }

        void UpdateHover()
        {
            MapPin hit = null;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overUI && !_cam.IsDragging)
            {
                Ray ray = _camera.ScreenPointToRay(MapInput.PointerPosition);
                if (Physics.Raycast(ray, out var info, _camera.farClipPlane))
                    hit = info.collider.GetComponentInParent<MapPin>();
            }
            if (hit != _hovered)
            {
                if (_hovered != null) _hovered.SetHovered(false);
                _hovered = hit;
                if (_hovered != null) _hovered.SetHovered(true);
            }
            if (_hovered != null && MapInput.TouchCount == 0)
            {
                string name = _hovered.kind == PinKind.District ? _db.District(_hovered.id)?.name : _db.Landmark(_hovered.id)?.name;
                _ui.SetTooltip(name, MapInput.PointerPosition);
            }
            else _ui.SetTooltip(null, Vector2.zero);
        }

        // ------------------------------------------------------------------ coordinates

        public Vector3 UvToWorld(float[] uv, float y = 0f) =>
            new Vector3((uv[0] - 0.5f) * mapWidth, y, (0.5f - uv[1]) * _mapDepth);

        float PxToWorld(float px) => px * mapWidth / _db.city.mapPixelSize[0];

        /// Ground height at a map UV (0 if the terrain is missing).
        float Ground(float u, float v) => _terrain != null ? _terrain.HeightAtUV(u, v) : 0f;
        /// Water surface where there is water, otherwise ground.
        float Surface(float u, float v) => _terrain != null ? _terrain.SurfaceAtUV(u, v) : 0f;
        bool IsWater(float u, float v) => _terrain != null && !float.IsNaN(_terrain.WaterAtUV(u, v));

        Vector3 OnGround(float[] uv, float above = 0f) => UvToWorld(uv, Surface(uv[0], uv[1]) + above);

        // ------------------------------------------------------------------ building the world

        void SetUpAtmosphere()
        {
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = NevishkTheme.SceneBackground;
            _camera.fieldOfView = 40f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = NevishkTheme.Fog;
            // Overcast murk: starts beyond the far side of the city at full zoom-out, so districts stay clear,
            // and swallows the country well before the horizon.
            RenderSettings.fogStartDistance = mapWidth * 1.25f;
            RenderSettings.fogEndDistance = mapWidth * 3.2f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.33f, 0.36f, 0.38f);
            RenderSettings.ambientEquatorColor = new Color(0.25f, 0.26f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.09f, 0.085f);

            // Use the scene's directional light if it has one (the URP template does), otherwise make one.
            // (Runs before any other lights are created, so the only light present is the scene's own.)
            Light sun = RenderSettings.sun != null ? RenderSettings.sun : FindAnyObjectByType<Light>();
            if (sun != null && sun.type != LightType.Directional) sun = null;
            if (sun == null) { sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; }
            sun.color = NevishkTheme.SunColor;
            sun.intensity = NevishkTheme.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;   // overcast: soft, weak shadows
            sun.transform.rotation = Quaternion.Euler(NevishkTheme.SunAngles);
        }

        void SetUpPostProcessing(bool reducedMotion)
        {
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            data.requiresDepthTexture = true;   // the fog zone softens where it meets the ground

            var go = new GameObject("Atmosphere");
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile = profile;

            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var grade = profile.Add<ColorAdjustments>(true);
            grade.saturation.Override(-30f); grade.contrast.Override(14f); grade.postExposure.Override(-0.15f);
            grade.colorFilter.Override(new Color(0.93f, 0.96f, 0.96f));       // cold, damp grey
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);           // green-grey shadows, pale highlights
            smh.shadows.Override(new Vector4(0.96f, 1.0f, 0.98f, -0.06f));
            smh.highlights.Override(new Vector4(1.0f, 1.0f, 1.02f, 0f));
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.40f); vig.smoothness.Override(0.55f);
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1.1f); bloom.intensity.Override(0.45f); // Балаур's fire
            if (!reducedMotion)
            {
                var grain = profile.Add<FilmGrain>(true);
                grain.type.Override(FilmGrainLookup.Thin2); grain.intensity.Override(0.2f); grain.response.Override(0.8f);
            }
        }

        void BuildGround()
        {
            _terrain = new NevishkTerrain(mapWidth, _mapDepth);
            if (_terrain.Load())
            {
                PlanRailroad();          // shapes the ground along the track, so it must come before the meshes
                BuildTerrain();
                if (_rail != null) _rail.Build(_root, MatFor, PrimitiveMesh(PrimitiveType.Cube));
                BuildCountryside();
                // The zone: fog walls just outside the drawn city and murk over the country beyond.
                var box = new Rect(-mapWidth / 2f, -_mapDepth / 2f, mapWidth, _mapDepth);
                var cityRect = _terrain.WorldRect;
                float Ground(float x, float z) => cityRect.Contains(new Vector2(x, z))
                    ? _terrain.HeightAtWorld(new Vector3(x, 0f, z))
                    : _landscape.HeightAt(x, z);
                NevishkFog.Build(_root, box, new Vector2(_landscape.HalfX, _landscape.HalfZ), Ground, NevishkBrowser.PrefersReducedMotion);
                return;
            }
            _terrain = null;   // fall back to the flat map

            var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ground.name = "Base Map";
            ground.transform.SetParent(_root, false);
            ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ground.transform.localScale = new Vector3(mapWidth, _mapDepth, 1f);
            var tex = Resources.Load<Texture2D>(_db.city.mapImage);
            Material mat = groundMaterial != null ? new Material(groundMaterial) : NewMat(Color.white);
            if (tex != null) { mat.SetTexture("_BaseMap", tex); mat.mainTexture = tex; }
            mat.SetColor("_BaseColor", NevishkTheme.GroundTint);
            mat.SetFloat("_Smoothness", 0.05f);
            ground.GetComponent<Renderer>().sharedMaterial = mat;
            Destroy(ground.GetComponent<Collider>());

            // Surrounding terrain so the map doesn't float in a void.
            var apron = GameObject.CreatePrimitive(PrimitiveType.Quad);
            apron.name = "Surroundings";
            apron.transform.SetParent(_root, false);
            apron.transform.localPosition = new Vector3(0, -0.5f, 0);
            apron.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            apron.transform.localScale = new Vector3(mapWidth * 6f, mapWidth * 6f, 1f);
            var am = NewMat(NevishkTheme.Surroundings);
            am.SetFloat("_Smoothness", 0f);
            apron.GetComponent<Renderer>().sharedMaterial = am;
            Destroy(apron.GetComponent<Collider>());
        }

        void PlanRailroad()
        {
            var asset = Resources.Load<TextAsset>("nevishk_rail");
            if (asset == null) return;
            var pts = (MiniJson.Parse(asset.text) as Dictionary<string, object>)["points"] as List<object>;
            var uvs = new List<Vector2>(pts.Count);
            foreach (var o in pts) { var p = (List<object>)o; uvs.Add(new Vector2(F(p[0]), F(p[1]))); }
            _rail = NevishkRailroad.Plan(_terrain, uvs, mapWidth, _mapDepth);
            _rail.FlattenTerrain(_terrain);
        }

        void BuildCountryside()
        {
            var starts = new List<NevishkLandscape.RiverStart>();
            var asset = Resources.Load<TextAsset>("nevishk_landscape");
            if (asset != null)
                foreach (var o in (MiniJson.Parse(asset.text) as Dictionary<string, object>)["rivers"] as List<object>)
                {
                    var r = (Dictionary<string, object>)o;
                    starts.Add(new NevishkLandscape.RiverStart
                    {
                        pos = new Vector3(F(r["x"]), 0f, F(r["z"])),
                        dir = new Vector3(F(r["dx"]), 0f, F(r["dz"])).normalized,
                        width = F(r["width"])
                    });
                }
            _landscape = new NevishkLandscape(_terrain);
            _landscape.Build(_root, MatFor, groundMaterial, starts);
        }

        NevishkLandscape _landscape;

        bool NearRail(Vector3 world, float margin) => _rail != null && _rail.DistanceXZ(world) < margin;

        void BuildTerrain()
        {
            var tex = Resources.Load<Texture2D>("nevishk_albedo");
            if (tex != null) { tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 8; }
            Material ground = groundMaterial != null ? new Material(groundMaterial) : NewMat(Color.white);
            if (tex != null) { ground.SetTexture("_BaseMap", tex); ground.mainTexture = tex; }
            ground.SetColor("_BaseColor", NevishkTheme.TerrainTint);
            ground.SetFloat("_Smoothness", 0.04f);

            var parent = new GameObject("Terrain").transform;
            parent.SetParent(_root, false);
            foreach (var mesh in _terrain.BuildGroundTiles())
                AddMesh(parent, mesh.name, mesh, ground);

            // No side walls: the countryside sinks into the dark at the edges instead.

            var water = NewMat(NevishkTheme.Water);
            water.SetFloat("_Smoothness", 0.9f);
            AddMesh(parent, "Мырла", _terrain.BuildWater(), water);
            // Water neither casts nor needs shadows (the dark stepped shapes under the old water came from this).
            var waterRenderer = parent.Find("Мырла")?.GetComponent<MeshRenderer>();
            if (waterRenderer != null) { waterRenderer.shadowCastingMode = ShadowCastingMode.Off; waterRenderer.receiveShadows = true; }

            // A strip down the detailed terrain's edges, so no gap can show where it meets the countryside.
            AddMesh(parent, "Edge", _terrain.BuildSkirt(), NewMat(new Color(0.40f, 0.39f, 0.30f)));

            _cam.groundHeight = p => _terrain.HeightAtWorld(p);
        }

        void AddMesh(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        struct BridgeInfo { public Vector3 a, b; public float yaw, width; }
        readonly List<BridgeInfo> _bridges = new List<BridgeInfo>();

        void BuildModels()
        {
            // Let spread-out structures (parks, streets, embankments) put each piece on the real ground.
            if (_terrain != null)
            {
                NevishkBuildings.GroundAt = p => _terrain.HeightAtWorld(p);
                NevishkBuildings.IsWet = p => !float.IsNaN(_terrain.WaterAtWorld(p));
            }
            var root = new GameObject("Buildings").transform;
            root.SetParent(_root, false);
            int seed = 11;

            // Bridges go first, so every building after them can keep clear of the decks.
            var drawn = new List<Dictionary<string, object>>();
            var asset = Resources.Load<TextAsset>("nevishk_models");
            if (asset != null)
                foreach (var o in (MiniJson.Parse(asset.text) as Dictionary<string, object>)["models"] as List<object>)
                    drawn.Add((Dictionary<string, object>)o);
            foreach (bool bridges in new[] { true, false })
            {
                // 1. Buildings drawn on the base map.
                foreach (var m in drawn)
                {
                    if (((string)m["type"] == "bridge") != bridges) continue;
                    var uv = (List<object>)m["uv"]; var size = (List<object>)m["size"];
                    PlaceAndBuild(root, (string)m["type"], F(uv[0]), F(uv[1]), PxToWorld(F(size[0])), PxToWorld(F(size[1])),
                                  F(m["yaw"]), true, (string)m["label"], (string)m["landmark"], seed++);
                }
                // 2. A structure for every other place (checkpoints, bridges, the fountain, power plants…).
                foreach (var l in _db.landmarks)
                {
                    if (_modelByLandmark.ContainsKey(l.id)) continue;
                    var spec0 = NevishkBuildings.ForLandmark(l.name);
                    if (spec0 == null || (spec0.Value.type == "bridge") != bridges) continue;
                    BuildForLandmark(root, l, spec0.Value, ref seed);
                }
            }
        }

        void BuildForLandmark(Transform root, LandmarkInfo l, (string type, float w, float d) spec, ref int seed)
        {
            {
                var (type, wPx, dPx) = spec;
                int hash = 0; foreach (char ch in l.id + l.name) hash = hash * 31 + ch;
                hash &= 0x7fffffff;
                float yaw = (hash % 4) * 90f + (hash / 4 % 21 - 10);

                // A checkpoint next to a bridge stands on the bridge, its barrier across the roadway.
                if (type == "checkpoint" && NearestBridge(UvToWorld(l.uv), 90f, out var br, out float t))
                {
                    Vector3 p = Vector3.Lerp(br.a, br.b, Mathf.Clamp(t, 0.2f, 0.8f));
                    var cb = NevishkBuildings.Build("checkpoint_bridge", root, p, br.width, 4f, br.yaw + 90f, MatFor, seed++);
                    Register(l.id, l.uv[0], l.uv[1], p, cb, 6f, l.name);
                    return;
                }
                PlaceAndBuild(root, type, l.uv[0], l.uv[1], PxToWorld(wPx), PxToWorld(dPx), yaw, false, l.name, l.id, seed++);
            }
        }

        void PlaceAndBuild(Transform root, string type, float u, float v, float w, float d, float yaw, bool fixedYaw,
                           string label, string landmarkId, int seed)
        {
            Vector3 pos; float pitch = 0f;
            if (type == "bridge")
            {
                Vector3 c = UvToWorld(new[] { u, v });
                if (!fixedYaw && FindCrossing(u, v, out var centre, out var axisYaw, out var length, out _))
                { c = centre; yaw = axisYaw; w = length; }
                // The deck runs straight from bank to bank, sloping if the banks differ,
                // and each end goes a few metres into the ground so it never stops in the air.
                Vector3 axis = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                float L = w + 10f;
                Vector3 a = c - axis * L / 2, b = c + axis * L / 2;
                a.y = DeckEnd(a); b.y = DeckEnd(b);
                // Keep the whole deck clear of the ground (a rim or bank can rise mid-span) …
                float lift = 0f;
                for (int k = 1; k < 30; k++)
                {
                    Vector3 q = Vector3.Lerp(a, b, k / 30f);
                    if (_terrain != null) lift = Mathf.Max(lift, _terrain.HeightAtWorld(q) + 0.6f - q.y);
                }
                a.y += lift; b.y += lift;
                // … and give each end a stone abutment down into the ground, so it never ends in the air.
                foreach (var end in new[] { a, b })
                {
                    float g = _terrain != null ? _terrain.HeightAtWorld(end) : end.y - 2f;
                    float top = end.y - 1.4f, bottom = Mathf.Min(g, top) - 3f;
                    Prop("Abutment", PrimitiveMesh(PrimitiveType.Cube), new Vector3(end.x, (top + bottom) / 2, end.z),
                         new Vector3(d + 2f, top - bottom, d + 2f), new Color(0.52f, 0.50f, 0.46f), 0.05f)
                        .transform.rotation = Quaternion.Euler(0, yaw, 0);
                }
                pos = (a + b) / 2;
                pitch = Mathf.Atan2(b.y - a.y, L) * Mathf.Rad2Deg;
                w = L;
                _bridges.Add(new BridgeInfo { a = a, b = b, yaw = yaw, width = d });
            }
            else if (NevishkBuildings.IsGroundFeature(type))
            {
                pos = OnGround(new[] { u, v }, -0.1f);
            }
            else if (type == "depot" && _rail != null && _rail.Closest(UvToWorld(new[] { u, v }), out var railPt, out var railDir) < 30f)
            {
                // A depot on the line: centred on the track, lined up with it, open at both ends.
                type = "depot_through";
                yaw = -Mathf.Atan2(railDir.z, railDir.x) * Mathf.Rad2Deg;
                w = Mathf.Max(w, 30f); d = 13f;
                pos = new Vector3(railPt.x, railPt.y - 0.2f, railPt.z);
                u = railPt.x / mapWidth + 0.5f; v = 0.5f - railPt.z / _mapDepth;
            }
            else
            {
                MoveToDry(ref u, ref v, w, d);
                float du = w * 0.5f / mapWidth, dv = d * 0.5f / _mapDepth;
                float c0 = Ground(u - du, v - dv), c1 = Ground(u + du, v - dv), c2 = Ground(u - du, v + dv), c3 = Ground(u + du, v + dv), cc = Ground(u, v);
                // With a foundation: stand on the highest point (the foundation fills below). Ruins: settle into the slope.
                float g = NevishkBuildings.NeedsFoundation(type)
                    ? Mathf.Max(Mathf.Max(c0, c1), Mathf.Max(Mathf.Max(c2, c3), cc)) + 0.05f
                    : Mathf.Min(Mathf.Min(c0, c1), Mathf.Min(Mathf.Min(c2, c3), cc)) - 0.4f;
                pos = UvToWorld(new[] { u, v }, g);
            }
            var built = NevishkBuildings.Build(type, root, pos, w, d, yaw, MatFor, seed);
            if (pitch != 0f) built.root.transform.rotation = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(0, 0, pitch);
            Register(landmarkId, u, v, pos, built, Mathf.Max(w, d) * 0.5f * _db.city.mapPixelSize[0] / mapWidth, label);
        }

        void Register(string landmarkId, float u, float v, Vector3 pos, NevishkBuildings.Result built, float rPx, string label)
        {
            var spot = new ModelSpot { u = u, v = v, rPx = rPx, pos = pos, top = built.height, radius = built.radius, label = label };
            _models.Add(spot);
            if (!string.IsNullOrEmpty(landmarkId)) _modelByLandmark[landmarkId] = spot;
        }

        /// Height for a bridge end: the ground there, or above the water if the end is still over it.
        float DeckEnd(Vector3 p)
        {
            if (_terrain == null) return 4f;
            float wl = _terrain.WaterAtWorld(p), g = _terrain.HeightAtWorld(p);
            return float.IsNaN(wl) ? g + 0.3f : Mathf.Max(g, wl + 3f);
        }

        bool NearestBridge(Vector3 w, float maxDist, out BridgeInfo best, out float bestT)
        {
            best = default; bestT = 0f; float bestD = maxDist; bool found = false;
            foreach (var br in _bridges)
            {
                Vector2 A = new Vector2(br.a.x, br.a.z), AB = new Vector2(br.b.x - br.a.x, br.b.z - br.a.z);
                float t = Mathf.Clamp01(Vector2.Dot(new Vector2(w.x, w.z) - A, AB) / AB.sqrMagnitude);
                float dd = Vector2.Distance(new Vector2(w.x, w.z), A + AB * t);
                if (dd < bestD) { bestD = dd; best = br; bestT = t; found = true; }
            }
            return found;
        }

        /// A footprint is buildable if none of it (plus a small margin) is water, a bridge or the railway.
        bool FootprintDry(float u, float v, float w, float d)
        {
            float du = (w * 0.5f + 3f) / mapWidth, dv = (d * 0.5f + 3f) / _mapDepth;
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                if (IsWater(u + i * du, v + j * dv)) return false;
            Vector3 c = UvToWorld(new[] { u, v });
            float r = Mathf.Max(w, d) * 0.5f;
            foreach (var br in _bridges)
                if (SegmentDistance(c, br.a, br.b) < r + br.width * 0.5f + 3f) return false;
            if (NearRail(c, r + NevishkRailroad.BedHalfWidth + 2f)) return false;
            return true;
        }

        bool NearBridge(Vector3 w, float margin)
        {
            foreach (var br in _bridges) if (SegmentDistance(w, br.a, br.b) < margin + br.width * 0.5f) return true;
            return false;
        }

        static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector2 A = new Vector2(a.x, a.z), AB = new Vector2(b.x - a.x, b.z - a.z), P = new Vector2(p.x, p.z);
            float t = Mathf.Clamp01(Vector2.Dot(P - A, AB) / Mathf.Max(AB.sqrMagnitude, 1e-6f));
            return Vector2.Distance(P, A + AB * t);
        }

        /// If the footprint touches water, move it to the nearest dry spot (up to 90 m away).
        void MoveToDry(ref float u, ref float v, float w, float d)
        {
            if (_terrain == null || FootprintDry(u, v, w, d)) return;
            for (float r = 6f; r <= 90f; r += 6f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    float uu = u + Mathf.Cos(a) * r / mapWidth, vv = v - Mathf.Sin(a) * r / _mapDepth;
                    if (FootprintDry(uu, vv, w, d)) { u = uu; v = vv; return; }
                }
        }

        // ------------------------------------------------------------------ stone paths (Аллея Бужорилор, Аллея «Память»)

        readonly List<(string kind, List<Vector3> pts)> _paths = new List<(string, List<Vector3>)>();

        void LoadPaths()
        {
            var asset = Resources.Load<TextAsset>("nevishk_landscape");
            if (asset == null) return;
            var root = MiniJson.Parse(asset.text) as Dictionary<string, object>;
            if (!root.ContainsKey("paths")) return;
            foreach (var o in (List<object>)root["paths"])
            {
                var pd = (Dictionary<string, object>)o;
                var pts = new List<Vector3>();
                foreach (var q in (List<object>)pd["points"]) { var xy = (List<object>)q; pts.Add(new Vector3(F(xy[0]), 0, F(xy[1]))); }
                _paths.Add(((string)pd["kind"], pts));
            }
        }

        bool NearPath(Vector3 w, float dist)
        {
            float d2 = dist * dist;
            foreach (var (_, pts) in _paths)
                foreach (var p in pts)
                    if ((p.x - w.x) * (p.x - w.x) + (p.z - w.z) * (p.z - w.z) < d2) return true;
            return false;
        }

        void BuildPaths()
        {
            if (_paths.Count == 0 || _terrain == null) return;
            var root = new GameObject("Paths").transform;
            root.SetParent(_root, false);
            Mesh cube = NevishkBuildings.CubeMesh, cyl = NevishkBuildings.CylinderMesh;
            var stone = new List<Vector3>(); var stoneTris = new List<int>();
            var posts = new List<CombineInstance>(); var heads = new List<CombineInstance>();
            var benches = new List<CombineInstance>(); var arches = new List<CombineInstance>();
            foreach (var (kind, pts) in _paths)
            {
                bool memory = kind == "memory";
                float half = memory ? 1.5f : 1.7f, along = 0f;
                int lampIndex = 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 prev = pts[Mathf.Max(i - 1, 0)], next = pts[Mathf.Min(i + 1, pts.Count - 1)];
                    Vector3 t = next - prev; t.y = 0; t.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, t);
                    Vector3 c = pts[i]; c.y = _terrain.HeightAtWorld(c) + 0.15f;
                    stone.Add(c - right * half); stone.Add(c + right * half);
                    if (i > 0)
                    {
                        int k = stone.Count - 4;   // left/right of previous, then of this point
                        stoneTris.Add(k); stoneTris.Add(k + 2); stoneTris.Add(k + 3);
                        stoneTris.Add(k); stoneTris.Add(k + 3); stoneTris.Add(k + 1);
                        along += Vector3.Distance(new Vector3(pts[i - 1].x, 0, pts[i - 1].z), new Vector3(c.x, 0, c.z));
                    }
                    // Lamps every ~12 m, alternating sides; benches every ~30 m; arches every ~24 m on «Память».
                    if (along >= lampIndex * 12f)
                    {
                        float side = lampIndex % 2 == 0 ? 1f : -1f;
                        Vector3 lp = c + right * side * (half + 0.9f);
                        lp.y = _terrain.HeightAtWorld(lp);
                        posts.Add(new CombineInstance { mesh = cyl, transform = Matrix4x4.TRS(lp + Vector3.up * 2.2f, Quaternion.identity, new Vector3(0.22f, 2.2f, 0.22f)) });
                        heads.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(lp + Vector3.up * 4.6f, Quaternion.identity, new Vector3(0.6f, 0.45f, 0.6f)) });
                        if (lampIndex % 5 == 2)
                        {
                            Vector3 bp = c - right * side * (half + 1.1f); bp.y = _terrain.HeightAtWorld(bp);
                            benches.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(bp + Vector3.up * 0.35f, Quaternion.LookRotation(t), new Vector3(0.6f, 0.5f, 2.2f)) });
                        }
                        if (memory && lampIndex % 2 == 1)
                        {
                            var rot = Quaternion.LookRotation(t);
                            Vector3 l = c - right * (half + 0.4f), r = c + right * (half + 0.4f);
                            l.y = _terrain.HeightAtWorld(l); r.y = _terrain.HeightAtWorld(r);
                            arches.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(l + Vector3.up * 2.75f, rot, new Vector3(0.35f, 5.5f, 0.35f)) });
                            arches.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(r + Vector3.up * 2.75f, rot, new Vector3(0.35f, 5.5f, 0.35f)) });
                            arches.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS((l + r) / 2 + Vector3.up * 5.4f, rot, new Vector3((half + 0.4f) * 2 + 0.4f, 0.3f, 0.35f)) });
                        }
                        lampIndex++;
                    }
                }
            }
            var mesh = new Mesh { name = "Stone paths", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(stone); mesh.SetTriangles(stoneTris, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AddMesh(root, "Stone paths", mesh, MatFor(new Color(0.62f, 0.60f, 0.55f), 0.08f));
            AddCombined("Path lamp posts", posts, new Color(0.18f, 0.17f, 0.16f));
            AddCombined("Path lamps", heads, new Color(1.0f, 0.84f, 0.55f));
            AddCombined("Benches", benches, new Color(0.33f, 0.25f, 0.18f));
            AddCombined("Arches", arches, new Color(0.45f, 0.25f, 0.15f));
        }

        /// Finds how a bridge at this spot should run: tries directions through the point and picks the one
        /// spanning the lowest ground (river or ravine) between the highest banks.
        bool FindCrossing(float u, float v, out Vector3 centre, out float yaw, out float length, out float deckY)
        {
            centre = UvToWorld(new[] { u, v }); yaw = 0; length = 0; deckY = 0;
            const float R = 75f, step = 3f;
            int nS = Mathf.RoundToInt(2 * R / step) + 1;
            var prof = new float[nS];
            float bestGap = 2.5f; bool found = false;
            for (int deg = 0; deg < 180; deg += 10)
            {
                float a = deg * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int s = 0; s < nS; s++)
                {
                    var p = centre + dir * (-R + s * step);
                    float wl = _terrain.WaterAtWorld(p);
                    prof[s] = float.IsNaN(wl) ? _terrain.HeightAtWorld(p) : wl - 6f;   // water counts as deep
                }
                int mid = nS / 2;
                float low = float.MaxValue;
                for (int s = mid - 4; s <= mid + 4; s++) low = Mathf.Min(low, prof[s]);
                float leftTop = float.MinValue, rightTop = float.MinValue;
                for (int s = 0; s < mid - 3; s++) leftTop = Mathf.Max(leftTop, prof[s]);
                for (int s = mid + 3; s < nS; s++) rightTop = Mathf.Max(rightTop, prof[s]);
                float bank = Mathf.Min(leftTop, rightTop), gap = bank - low;
                if (gap <= bestGap) continue;
                if (low > -3f && gap < 10f) continue;      // must cross water (or a deep gap like the ravine), not a dip
                // Ends: walk out from the middle until the ground is back up near the bank height.
                int l = mid, r = mid;
                while (l > 0 && prof[l] < bank - 1f) l--;
                while (r < nS - 1 && prof[r] < bank - 1f) r++;
                float t0 = -R + l * step, t1 = -R + r * step;
                float len = Mathf.Clamp(t1 - t0 + 8f, 16f, 150f);
                Vector3 c = UvToWorld(new[] { u, v }) + dir * ((t0 + t1) * 0.5f);
                // Prefer the shortest crossing (square to the river), and keep 25 m from other bridges.
                if (found && len >= length) continue;
                Vector3 ea = c - dir * len / 2, eb = c + dir * len / 2;
                bool clash = false;
                foreach (var br in _bridges)
                    for (int q = 0; q <= 10 && !clash; q++)
                        if (SegmentDistance(Vector3.Lerp(ea, eb, q / 10f), br.a, br.b) < 25f) clash = true;
                if (clash) continue;
                found = true; length = len; centre = c;
                deckY = Mathf.Max(prof[l], prof[r]) + 0.8f;
                yaw = -deg;   // root's local x runs along the bridge
            }
            return found;
        }

        /// Is a map UV within (model radius + margin px) of a drawn building?
        bool NearModel(float u, float v, float marginPx)
        {
            int[] px = _db.city.mapPixelSize;
            foreach (var s in _models)
            {
                float dx = (u - s.u) * px[0], dy = (v - s.v) * px[1];
                float r = s.rPx + marginPx;
                if (dx * dx + dy * dy < r * r) return true;
            }
            return false;
        }

        NevishkRoads _roads;

        /// Key roads between the districts and out to the country roads (after the bridges exist, before the fill).
        void BuildRoads()
        {
            if (_terrain == null) return;
            var districts = new List<Vector3>();
            foreach (var d in _db.districts) if (d.uv != null && d.uv.Length == 2) districts.Add(UvToWorld(d.uv));
            var exits = new List<Vector3>();
            if (_landscape != null)
            {
                var rect = _terrain.WorldRect;
                foreach (var e in _landscape.CityExits)
                    exits.Add(new Vector3(Mathf.Clamp(e.x, rect.xMin + 3f, rect.xMax - 3f), 0f, Mathf.Clamp(e.z, rect.yMin + 3f, rect.yMax - 3f)));
            }
            var bridges = new List<(Vector3, Vector3, float)>();
            foreach (var br in _bridges) bridges.Add((br.a, br.b, br.width));
            bool Blocked(Vector3 w)
            {
                float u = w.x / mapWidth + 0.5f, v = 0.5f - w.z / _mapDepth;
                return NearModel(u, v, 3f) || NearPath(w, 3f) || InPrimeCity(w, 1f);
            }
            _roads = new NevishkRoads(_terrain);
            _roads.Plan(districts, exits, bridges, Blocked, w => NearRail(w, NevishkRailroad.BedHalfWidth + 4f));
            var asphalt = new Material(MatFor(new Color(0.20f, 0.20f, 0.21f))); asphalt.SetFloat("_Smoothness", 0.35f);   // damp
            _roads.Build(_root, asphalt, MatFor(new Color(0.78f, 0.76f, 0.66f)));
        }

        bool NearRoad(Vector3 w, float margin) => _roads != null && _roads.Near(w, margin);

        void BuildBlocksAndTrees()
        {
            var asset = Resources.Load<TextAsset>("nevishk_layout");
            if (asset == null) { Debug.LogWarning("[Nevishk] nevishk_layout.json missing"); return; }
            var layout = MiniJson.Parse(asset.text) as Dictionary<string, object>;
            var blocks = layout["blocks"] as Dictionary<string, object>;

            var fill = new NevishkFill();
            int seed = 0;
            foreach (var kv in blocks)
            {
                foreach (var o in (List<object>)kv.Value)
                {
                    var r = (List<object>)o;
                    float u = F(r[0]), v = F(r[1]), w = PxToWorld(F(r[2])), d = PxToWorld(F(r[3])), h = F(r[4]), yaw = F(r[5]);
                    if (NearModel(u, v, Mathf.Max(F(r[2]), F(r[3])) * 0.5f + 3f)) continue;   // make room for drawn buildings
                    if (InPrimeCity(UvToWorld(new[] { u, v }), Mathf.Max(w, d) * 0.5f)) continue;
                    if (NearRail(UvToWorld(new[] { u, v }), Mathf.Max(w, d) * 0.6f + NevishkRailroad.BedHalfWidth + 3f)) continue;
                    if (NearRoad(UvToWorld(new[] { u, v }), Mathf.Max(w, d) * 0.6f + 1.5f)) continue;
                    // Stand on the lowest corner so nothing floats on a slope; top stays level.
                    float du = w * 0.6f / mapWidth, dv = d * 0.6f / _mapDepth;
                    float g0 = Mathf.Min(Mathf.Min(Ground(u - du, v - dv), Ground(u + du, v - dv)),
                                         Mathf.Min(Ground(u - du, v + dv), Ground(u + du, v + dv))) - 0.5f;
                    // Never on the water or the river bank, never on a path.
                    if (IsWater(u, v) || IsWater(u - du, v - dv) || IsWater(u + du, v - dv) || IsWater(u - du, v + dv) || IsWater(u + du, v + dv)
                        || g0 < 0.4f || NearPath(UvToWorld(new[] { u, v }), Mathf.Max(w, d) * 0.6f + 2f)
                        || NearBridge(UvToWorld(new[] { u, v }), Mathf.Max(w, d) * 0.6f + 6f)) continue;
                    float g1 = Mathf.Max(Ground(u, v), g0);
                    float top = g1 + h;
                    seed++;
                    var kind = NevishkFill.Classify(kv.Key, w, d, h, seed * 7919 % 101);
                    fill.Add(kv.Key, kind, UvToWorld(new[] { u, v }), g0, w, d, top - g0, yaw, seed * 2654435 % 9973);
                }
            }
            fill.Finish(_root, c => MatFor(c));

            // Trees: low-poly cones, merged into one mesh.
            Mesh cone = ConeMesh(7);
            var trees = new List<CombineInstance>();
            foreach (var o in (List<object>)layout["trees"])
            {
                var r = (List<object>)o;
                float s = F(r[2]), tu = F(r[0]), tv = F(r[1]);
                if (IsWater(tu, tv) || NearModel(tu, tv, 2f) || NearRail(UvToWorld(new[] { tu, tv }), NevishkRailroad.BedHalfWidth + 4f)
                    || NearRoad(UvToWorld(new[] { tu, tv }), 2.5f)
                    || NearPath(UvToWorld(new[] { tu, tv }), 3.5f)) continue;
                Vector3 p = UvToWorld(new[] { tu, tv }, Ground(tu, tv) - 0.5f);
                trees.Add(new CombineInstance
                {
                    mesh = cone,
                    transform = Matrix4x4.TRS(p, Quaternion.Euler(0, s * 97f, 0), new Vector3(4f * s, 11f * s, 4f * s))
                });
            }
            AddCombined("Trees", trees, new Color(0.22f, 0.40f, 0.24f));
        }

        void BuildSpecialLandmarks()
        {
            Mesh cube = PrimitiveMesh(PrimitiveType.Cube), cyl = PrimitiveMesh(PrimitiveType.Cylinder);
            foreach (var l in _db.landmarks)
            {
                Vector3 p = OnGround(l.uv);
                switch (l.name)
                {
                    case "Невишк Prime City":
                        BuildPrimeCity(p, cube, cyl);
                        break;
                    case "Балаур":
                        // A rock island where the Мырла forks.
                        var rock = Prop("Балаур", PrimitiveMesh(PrimitiveType.Sphere), p + new Vector3(0, 2, 0),
                                        new Vector3(18, 22, 26), new Color(0.48f, 0.44f, 0.38f), 0.1f);
                        rock.transform.rotation = Quaternion.Euler(0, 35, 0);
                        var fire = new GameObject("Огонь в пасти").AddComponent<Light>();
                        fire.transform.SetParent(rock.transform, false);
                        fire.transform.localPosition = new Vector3(0, 0, 0.55f);
                        fire.type = LightType.Point; fire.color = new Color(1f, 0.45f, 0.15f); fire.range = 30f; fire.intensity = 4f;
                        break;
                    default:
                        if (l.category == "production" && !_modelByLandmark.ContainsKey(l.id))
                        {
                            Prop(l.name + " · цех", cube, p + new Vector3(6, 5, 4), new Vector3(16, 10, 10), new Color(0.62f, 0.52f, 0.52f), 0.1f);
                            Prop(l.name + " · труба", cyl, p + new Vector3(-6, 14, -4), new Vector3(3, 14, 3), new Color(0.55f, 0.35f, 0.32f), 0.1f);
                        }
                        break;
                }
            }
        }

        // Невишк Prime City as in the board photo, seen from the Набережная to the south, left to right (west → east):
        // «Обсидиан» (black glass, tallest, a spire), «Ось» (glass cylinder, revolving restaurant on top),
        // «Ковчег» (slim residential tower, stepped crown) and the unfinished Скелет with its crane. One mall podium
        // joins their bases ("торговый центр в основании башен").
        const float PrimeHalfX = 46f, PrimeHalfZ = 16f;

        void BuildPrimeCity(Vector3 p, Mesh cube, Mesh cyl)
        {
            Mesh spire = NevishkBuildings.SpireMesh;
            float G(float dx) => _terrain != null ? _terrain.HeightAtWorld(p + new Vector3(dx, 0, 0)) : p.y;
            float baseY = Mathf.Min(G(-40f), Mathf.Min(G(0f), G(40f))) - 0.5f;
            Vector3 At(float dx, float y) => new Vector3(p.x + dx, baseY + y, p.z);

            Prop("Сити · торговый центр", cube, At(0, 4f), new Vector3(PrimeHalfX * 2f, 8f, 26f), new Color(0.20f, 0.21f, 0.23f), 0.8f);

            // «Обсидиан» — Чернильница
            var ink = new Color(0.04f, 0.04f, 0.06f);
            Prop("Обсидиан", cube, At(-33, 8f + 34f), new Vector3(14, 68, 14), ink, 0.95f);
            Prop("Обсидиан · верх", cube, At(-33, 76f + 12f), new Vector3(11, 24, 11), ink, 0.95f);
            Prop("Обсидиан · корона", cube, At(-33, 100f + 5f), new Vector3(7.5f, 10, 7.5f), ink, 0.95f);
            Prop("Обсидиан · шпиль", spire, At(-33, 110f), new Vector3(2.2f, 22f, 2.2f), new Color(0.35f, 0.35f, 0.38f), 0.8f);

            // «Ось» — glass cylinder with the revolving restaurant
            var glass = new Color(0.36f, 0.46f, 0.52f);
            Prop("Ось", cyl, At(-11, 8f + 40f), new Vector3(13, 40, 13), glass, 0.9f);            // cylinder mesh is 2 units tall
            Prop("Ось · ресторан", cyl, At(-11, 88f + 2.5f), new Vector3(16.5f, 2.5f, 16.5f), new Color(0.62f, 0.60f, 0.52f), 0.7f);
            Prop("Ось · купол", PrimitiveMesh(PrimitiveType.Sphere), At(-11, 93f), new Vector3(12f, 7f, 12f), glass, 0.9f);

            // «Ковчег» — slim, tall, stepped crown and a mast
            var pale = new Color(0.58f, 0.60f, 0.62f);
            Prop("Ковчег", cube, At(11, 8f + 38f), new Vector3(10, 76, 10), pale, 0.75f);
            Prop("Ковчег · ступень", cube, At(11, 84f + 4f), new Vector3(8, 8, 8), pale, 0.75f);
            Prop("Ковчег · ступень 2", cube, At(11, 92f + 3f), new Vector3(5.5f, 6, 5.5f), pale, 0.75f);
            Prop("Ковчег · мачта", cube, At(11, 98f + 6f), new Vector3(0.6f, 12, 0.6f), new Color(0.3f, 0.3f, 0.3f), 0f);

            // Скелет — bare concrete frame: corner columns and floor slabs, crane on top
            var concrete = new Color(0.46f, 0.45f, 0.43f);
            const float sw = 12f, top = 8f + 66f;
            foreach (var (cx, cz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1), (0, -1), (0, 1) })
                Prop("Скелет · колонна", cube, At(33 + cx * sw / 2f, (8f + top) / 2f) + new Vector3(0, 0, cz * sw / 2f),
                     new Vector3(1.2f, top - 8f, 1.2f), concrete, 0f);
            for (float y = 12f; y <= top; y += 5.5f)
                Prop("Скелет · перекрытие", cube, At(33, y), new Vector3(sw + 0.6f, 0.5f, sw + 0.6f), concrete, 0f);
            var crane = new Color(0.85f, 0.6f, 0.15f);
            Prop("Кран · башня", cube, At(33, top + 9f), new Vector3(1.5f, 18f, 1.5f), crane, 0f);
            Prop("Кран · стрела", cube, At(33 - 6f, top + 18f), new Vector3(26f, 1.2f, 1.2f), crane, 0f);
            Prop("Кран · противовес", cube, At(33 + 7.5f, top + 17.5f), new Vector3(3f, 2f, 2f), new Color(0.3f, 0.3f, 0.3f), 0f);
        }

        /// The Prime City quarter is built by hand; keep generic blocks out of its footprint.
        bool InPrimeCity(Vector3 world, float halfSize)
        {
            foreach (var l in _db.landmarks)
            {
                if (l.name != "Невишк Prime City") continue;
                Vector3 c = OnGround(l.uv);
                return Mathf.Abs(world.x - c.x) < PrimeHalfX + 4f + halfSize && Mathf.Abs(world.z - c.z) < PrimeHalfZ + 4f + halfSize;
            }
            return false;
        }

        void BuildPins()
        {
            var pinRoot = new GameObject("Pins").transform;
            pinRoot.SetParent(_root, false);
            Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere), cyl = PrimitiveMesh(PrimitiveType.Cylinder);

            foreach (var d in _db.districts)
            {
                var go = new GameObject("District · " + d.name);
                go.transform.SetParent(pinRoot, false);
                go.transform.position = OnGround(d.uv, districtLabelHeight);
                var pin = go.AddComponent<MapPin>();
                pin.kind = PinKind.District; pin.id = d.id; pin.hasLore = !string.IsNullOrEmpty(d.text);
                var col = go.AddComponent<SphereCollider>(); col.radius = 22f;
                pin.label = MakeLabel(go.transform, $"<color=#{ColorUtility.ToHtmlStringRGB(LoreDatabase.ParseColor(d.color, Color.white))}>{d.number}</color>  {d.name.ToUpperInvariant()}",
                                      72f, NevishkTheme.Text, true);
                pin.plate = MakePlate(pin.label, 6f, 3.2f);
                _pins[d.id] = pin;
            }

            foreach (var l in _db.landmarks)
            {
                var go = new GameObject("Landmark · " + l.name);
                go.transform.SetParent(pinRoot, false);
                go.transform.position = OnGround(l.uv);
                var pin = go.AddComponent<MapPin>();
                pin.kind = PinKind.Landmark; pin.id = l.id; pin.hasLore = !string.IsNullOrEmpty(l.text);

                if (_modelByLandmark.TryGetValue(l.id, out var spot))
                {
                    // The 3D building is the marker: no pole, the name floats above its roof.
                    go.transform.position = spot.pos;
                    pin.scaleWithDistance = false;
                    var hit = go.AddComponent<SphereCollider>();
                    hit.center = Vector3.up * spot.top * 0.5f;
                    hit.radius = Mathf.Max(spot.radius, spot.top * 0.5f);
                    // Name box tinted with the place's category colour, plus a matching dot, so the legend reads on buildings.
                    Color catCol = NevishkColors.ForCategory(_db, l.category);
                    string name = string.IsNullOrEmpty(spot.label) ? l.name : spot.label;
                    pin.label = MakeLabel(go.transform, $"<size=140%><color={NevishkTheme.ToHex(catCol)}>•</color></size> {name}", 40f, NevishkTheme.Text, false);
                    pin.plateColor = TintedPlate(catCol);
                    pin.label.transform.localPosition = Vector3.up * (spot.top + 8f);
                    pin.label.alpha = 0f;
                    pin.plate = MakePlate(pin.label, 3f, 1.6f);
                    _pins[l.id] = pin;
                    continue;
                }

                Color c = NevishkColors.ForCategory(_db, l.category);
                float h = pin.hasLore ? pinHeight * 1.3f : pinHeight;

                var pole = Prop("pole", cyl, go.transform.position + Vector3.up * h / 2, new Vector3(0.6f, h / 2, 0.6f),
                                pin.hasLore ? NevishkTheme.Accent : new Color(0.22f, 0.21f, 0.19f), 0f);
                pole.transform.SetParent(go.transform, true);

                var head = Prop("head", sphere, go.transform.position + Vector3.up * h, Vector3.one * (pin.hasLore ? 6.5f : 4.5f), c, 0.4f);
                head.transform.SetParent(go.transform, true);
                pin.head = head.transform;

                var col = go.AddComponent<SphereCollider>();
                col.center = Vector3.up * h; col.radius = 9f;   // generous hit area: pins are small from afar

                pin.label = MakeLabel(go.transform, l.name, 40f, NevishkTheme.Text, false);
                pin.label.transform.localPosition = Vector3.up * (h + 9f);
                pin.label.alpha = 0f;
                pin.plate = MakePlate(pin.label, 3f, 1.6f);
                pin.plateColor = TintedPlate(c);
                _pins[l.id] = pin;
            }
        }

        /// Dark label box nudged toward a category colour; dark enough that the bone text keeps its contrast.
        static Color TintedPlate(Color category)
        {
            Color baseCol = NevishkTheme.LabelPlate;
            Color t = Color.Lerp(baseCol, category * 0.55f, 0.35f);
            t.a = 0.82f;
            return t;
        }

        // ------------------------------------------------------------------ helpers

        TextMeshPro MakeLabel(Transform parent, string text, float size, Color color, bool bold)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshPro>();
            // District names use the display serif with wide tracking, like map lettering; landmarks use PT Sans.
            t.font = bold ? NevishkFonts.Display : NevishkFonts.UI;
            if (bold) t.characterSpacing = 12f;
            t.text = text; t.fontSize = size; t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.outlineWidth = 0.12f; t.outlineColor = NevishkTheme.LabelOutline;   // light outline: the plate does the work
            if (bold) t.characterSpacing = 8f;
            t.rectTransform.sizeDelta = new Vector2(bold ? 600 : 300, bold ? 160 : 60);
            t.GetComponent<MeshRenderer>().sortingOrder = 2;
            return t;
        }

        static Sprite _plateSprite;

        /// Subtle dark rounded box behind a world-space label.
        SpriteRenderer MakePlate(TextMeshPro label, float padX, float padY)
        {
            if (_plateSprite == null)
            {
                const int size = 32, radius = 10;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance outside a rounded rect → soft alpha edge.
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                    float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                tex.Apply();
                _plateSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 7f, 0,
                                             SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            }
            label.ForceMeshUpdate();
            Bounds b = label.textBounds;
            var go = new GameObject("Plate");
            go.transform.SetParent(label.transform, false);
            go.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.6f);   // just behind the text
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _plateSprite;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(b.size.x + padX * 2f, b.size.y + padY * 2f);
            sr.color = NevishkTheme.LabelPlate;
            sr.sortingOrder = 1;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            return sr;
        }

        GameObject Prop(string name, Mesh mesh, Vector3 pos, Vector3 scale, Color color, float smooth)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_root, false);
            go.transform.position = pos; go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var m = MatFor(color);
            if (smooth > 0f) { m = new Material(m); m.SetFloat("_Smoothness", smooth); }
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        void AddCombined(string name, List<CombineInstance> parts, Color color)
        {
            if (parts.Count == 0) return;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = MatFor(color);
        }

        Material MatFor(Color c)
        {
            if (_mats.TryGetValue(c, out var m)) return m;
            m = NewMat(c); _mats[c] = m; return m;
        }

        readonly Dictionary<(Color, float), Material> _matsSmooth = new Dictionary<(Color, float), Material>();

        /// Shared material for a colour and smoothness (used by the drawn buildings).
        Material MatFor(Color c, float smoothness)
        {
            if (_matsSmooth.TryGetValue((c, smoothness), out var m)) return m;
            m = NewMat(c); m.SetFloat("_Smoothness", smoothness);
            _matsSmooth[(c, smoothness)] = m; return m;
        }

        Material NewMat(Color c)
        {
            Material m = litTemplate != null ? new Material(litTemplate)
                       : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.SetColor("_BaseColor", c); m.color = c;
            m.SetFloat("_Smoothness", 0.15f);
            return m;
        }

        static readonly Dictionary<PrimitiveType, Mesh> _prim = new Dictionary<PrimitiveType, Mesh>();

        // Cached meshes and the label sprite are destroyed when Play stops; start each Play clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _prim.Clear(); _plateSprite = null; }
        static Mesh PrimitiveMesh(PrimitiveType t)
        {
            if (_prim.TryGetValue(t, out var m) && m != null) return m;
            var go = GameObject.CreatePrimitive(t);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            _prim[t] = m;
            return m;
        }

        static Mesh ConeMesh(int sides)
        {
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0, Mathf.Sin(a0) * 0.5f);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0, Mathf.Sin(a1) * 0.5f);
                int b = v.Count;
                v.Add(p0); v.Add(Vector3.up); v.Add(p1);        // flat-shaded side
                tri.Add(b); tri.Add(b + 1); tri.Add(b + 2);
            }
            var mesh = new Mesh { name = "Cone" };
            mesh.SetVertices(v); mesh.SetTriangles(tri, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static float F(object o) => Convert.ToSingle(o, System.Globalization.CultureInfo.InvariantCulture);
    }
}
