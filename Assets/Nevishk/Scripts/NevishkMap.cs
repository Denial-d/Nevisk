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
            BuildBlocksAndTrees();
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
            _cam.maxDistance = mapWidth * 1.15f;
            _camera.farClipPlane = mapWidth * 4f;
            _camera.nearClipPlane = 1f;
            ResetView();

            // Deep link: https://…/index.html#lm_046 opens that entry directly.
            NevishkBrowser.ListenForBackButton(gameObject.name);
            OnBrowserHashChanged(NevishkBrowser.GetHash());
        }

        // ------------------------------------------------------------------ deep links

        bool _applyingHash;

        /// <summary>Called on start and from the browser when the visitor presses Back/Forward.</summary>
        public void OnBrowserHashChanged(string hash)
        {
            _applyingHash = true;
            try
            {
                if (string.IsNullOrEmpty(hash)) { if (_ui.PanelOpen) _ui.Close(); }
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

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (MapInput.PrimaryUp && !_cam.LastPressWasDrag && !overUI && _hovered != null)
            {
                if (_hovered.kind == PinKind.District) SelectDistrict(_hovered.id, false);
                else SelectLandmark(_hovered.id, false);
            }
            if (MapInput.EscapeDown && _ui.PanelOpen) _ui.Close();
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
            // Matches LorePanelUI's CanvasScaler (1600x900, match 0.5).
            float logW = Mathf.Log(Screen.width / 1600f, 2), logH = Mathf.Log(Screen.height / 900f, 2);
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
            RenderSettings.fogStartDistance = mapWidth * 0.75f;
            RenderSettings.fogEndDistance = mapWidth * 2.1f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.44f, 0.50f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.30f, 0.28f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.11f, 0.10f);

            // Use the scene's directional light if it has one (the URP template does), otherwise make one.
            // (Runs before any other lights are created, so the only light present is the scene's own.)
            Light sun = RenderSettings.sun != null ? RenderSettings.sun : FindAnyObjectByType<Light>();
            if (sun != null && sun.type != LightType.Directional) sun = null;
            if (sun == null) { sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; }
            sun.color = NevishkTheme.SunColor;
            sun.intensity = NevishkTheme.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(NevishkTheme.SunAngles);
        }

        void SetUpPostProcessing(bool reducedMotion)
        {
            var data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            var go = new GameObject("Atmosphere");
            go.transform.SetParent(transform, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 10;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile = profile;

            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var grade = profile.Add<ColorAdjustments>(true);
            grade.saturation.Override(-16f); grade.contrast.Override(10f);
            grade.colorFilter.Override(new Color(1f, 0.97f, 0.92f));          // faint sodium-lamp warmth
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.32f); vig.smoothness.Override(0.5f);
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
            if (_terrain.Load()) { BuildTerrain(); return; }
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

            // Dark ground far below, the same colour as the sky, so the sunken edges disappear into it.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            floor.name = "Floor";
            floor.transform.SetParent(_root, false);
            floor.transform.localPosition = new Vector3(0, -46f, 0);
            floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floor.transform.localScale = new Vector3(mapWidth * 8f, mapWidth * 8f, 1f);
            var floorMat = NewMat(NevishkTheme.SceneBackground);
            floorMat.SetFloat("_Smoothness", 0f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            Destroy(floor.GetComponent<Collider>());

            _cam.groundHeight = p => _terrain.HeightAtWorld(p);
        }

        void AddMesh(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        void BuildBlocksAndTrees()
        {
            var asset = Resources.Load<TextAsset>("nevishk_layout");
            if (asset == null) { Debug.LogWarning("[Nevishk] nevishk_layout.json missing"); return; }
            var layout = MiniJson.Parse(asset.text) as Dictionary<string, object>;
            var blocks = layout["blocks"] as Dictionary<string, object>;

            Mesh cube = PrimitiveMesh(PrimitiveType.Cube);
            foreach (var kv in blocks)
            {
                var district = _db.District(kv.Key);
                Color concrete = NevishkTheme.Concrete;
                Color tint = district != null ? LoreDatabase.ParseColor(district.color, concrete) : concrete;
                Color col = Color.Lerp(concrete, tint, NevishkTheme.DistrictTint);
                var combine = new List<CombineInstance>();
                foreach (var o in (List<object>)kv.Value)
                {
                    var r = (List<object>)o;
                    float u = F(r[0]), v = F(r[1]), w = PxToWorld(F(r[2])), d = PxToWorld(F(r[3])), h = F(r[4]), yaw = F(r[5]);
                    // Stand on the lowest corner so nothing floats on a slope; top stays level.
                    float du = w * 0.6f / mapWidth, dv = d * 0.6f / _mapDepth;
                    float g0 = Mathf.Min(Mathf.Min(Ground(u - du, v - dv), Ground(u + du, v - dv)),
                                         Mathf.Min(Ground(u - du, v + dv), Ground(u + du, v + dv))) - 0.5f;
                    float g1 = Mathf.Max(Ground(u, v), g0);
                    float top = g1 + h;
                    Vector3 p = UvToWorld(new[] { u, v }, (g0 + top) / 2f);
                    combine.Add(new CombineInstance
                    {
                        mesh = cube,
                        transform = Matrix4x4.TRS(p, Quaternion.Euler(0, yaw, 0), new Vector3(w, top - g0, d))
                    });
                }
                AddCombined("Blocks · " + kv.Key, combine, col);
            }

            // Trees: low-poly cones, merged into one mesh.
            Mesh cone = ConeMesh(7);
            var trees = new List<CombineInstance>();
            foreach (var o in (List<object>)layout["trees"])
            {
                var r = (List<object>)o;
                float s = F(r[2]), tu = F(r[0]), tv = F(r[1]);
                if (IsWater(tu, tv)) continue;
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
                        // Обсидиан (black glass), Ось (cylinder), Ковчег, Скелет (unfinished)
                        Prop("Обсидиан", cube, p + new Vector3(-9, 45, 6), new Vector3(9, 90, 9), new Color(0.05f, 0.05f, 0.07f), 0.95f);
                        Prop("Ось", cyl, p + new Vector3(7, 37, 9), new Vector3(9, 37, 9), new Color(0.55f, 0.68f, 0.78f), 0.9f);
                        Prop("Ковчег", cube, p + new Vector3(10, 30, -8), new Vector3(10, 60, 8), new Color(0.75f, 0.72f, 0.66f), 0.6f);
                        Prop("Скелет", cube, p + new Vector3(-10, 25, -10), new Vector3(8, 50, 8), new Color(0.45f, 0.44f, 0.42f), 0f);
                        Prop("Кран", cube, p + new Vector3(-10, 58, -4), new Vector3(1.2f, 1.2f, 18), new Color(0.85f, 0.6f, 0.15f), 0f);
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
                        if (l.category == "production")
                        {
                            Prop(l.name + " · цех", cube, p + new Vector3(6, 5, 4), new Vector3(16, 10, 10), new Color(0.62f, 0.52f, 0.52f), 0.1f);
                            Prop(l.name + " · труба", cyl, p + new Vector3(-6, 14, -4), new Vector3(3, 14, 3), new Color(0.55f, 0.35f, 0.32f), 0.1f);
                        }
                        break;
                }
            }
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
                _pins[l.id] = pin;
            }
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

        Material NewMat(Color c)
        {
            Material m = litTemplate != null ? new Material(litTemplate)
                       : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            m.SetColor("_BaseColor", c); m.color = c;
            m.SetFloat("_Smoothness", 0.15f);
            return m;
        }

        static readonly Dictionary<PrimitiveType, Mesh> _prim = new Dictionary<PrimitiveType, Mesh>();
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
