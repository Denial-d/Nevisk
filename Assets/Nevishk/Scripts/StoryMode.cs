using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace Nevishk
{
    // ---------------------------------------------------------------- saved data

    [Serializable]
    public class StoryScene
    {
        public int v = 1;
        public List<StoryItem> items = new List<StoryItem>();
        public List<StoryLink> links = new List<StoryLink>();
    }

    [Serializable]
    public class StoryItem
    {
        public string id, kind, name, note, color;   // kind: figure | object | flag; color: #RRGGBB
        public float x, z;
    }

    [Serializable]
    public class StoryLink
    {
        public string a, b, color;   // ends: story item ids or district / landmark ids
    }

    /// <summary>
    /// Story mode: people put their own figures, story objects and flags on the map, move them about and tie
    /// things together with detective-board strings (to each other or to the lore's districts and landmarks).
    /// The scene autosaves in the browser and can be exported as a file or a link (#story=…) that opens it.
    /// </summary>
    public class StoryMode : MonoBehaviour
    {
        const string PrefsKey = "nevishk_story", BackupKey = "nevishk_story_backup";

        // Set by NevishkMap.
        public Camera cam;
        public MapCameraController camCtl;
        public LorePanelUI ui;
        public Func<Vector3, float> ground;                 // surface height at a world point
        public Func<string, Vector3?> anchorOf;             // where a string attaches to a district / landmark
        public Func<string, string> nameOf;                 // display name of a district / landmark
        public Func<Vector3, Vector3> clampToMap;
        public Func<Transform, string, TextMeshPro> makeLabel;
        public Func<Color, Material> material;

        StoryScene _scene = new StoryScene();
        readonly Dictionary<string, Token> _tokens = new Dictionary<string, Token>();
        readonly List<(StoryLink link, LineRenderer line)> _lines = new List<(StoryLink, LineRenderer)>();
        Transform _root;
        Material _lineMat;

        string _tool = "select";
        Color _colour = LorePanelUI.StoryColours[0];
        Token _selected, _dragging, _hoverToken;
        string _threadFrom;
        Vector3 _dragOffset;
        Vector2 _pressPos;
        bool _pressedOnToken, _dirty;
        float _saveAt;
        int _nextId = 1;

        class Token
        {
            public StoryItem item;
            public GameObject go;
            public Transform body, spin;
            public TextMeshPro label;
            public SpriteRenderer plate;
            public Renderer[] tinted;
            public float topLocal;          // anchor height before distance scaling
            public float scale = 1f;
        }

        // ---------------------------------------------------------------- set-up

        public void Init()
        {
            _root = new GameObject("Story").transform;
            _root.SetParent(transform, false);
            var unlit = Shader.Find("Nevishk/Unlit");
            _lineMat = unlit != null ? new Material(unlit) : material(Color.white);

            ui.StoryToggled += on => SetActive(on);
            ui.StoryToolChosen += SetTool;
            ui.StoryColourChosen += c =>
            {
                _colour = c; ui.SetStoryColour(c);
                if (_selected != null) { _selected.item.color = Hex(c); Recolour(_selected); MarkDirty(); }
            };
            ui.StorySave += SaveFile;
            ui.StoryOpen += OpenFile;
            ui.StoryShare += ShareLink;
            ui.StoryClear += () => { Backup(); Load(new StoryScene()); MarkDirty(); ui.Notice("Сюжет очищен.", "Вернуть", RestoreBackup); };
            ui.CardNameChanged += v => { if (_selected == null) return; _selected.item.name = v; SetLabel(_selected); MarkDirty(); };
            ui.CardNoteChanged += v => { if (_selected == null) return; _selected.item.note = v; MarkDirty(); };
            ui.CardDelete += () => { if (_selected != null) Delete(_selected); };
            ui.CardDone += () => Select(null);
            ui.SetStoryColour(_colour);
            ui.SetStoryTool(_tool);

            // Press on a token = drag it, not the camera.
            camCtl.claimPress = p => ui.StoryActive && _tool != "delete" && TokenAt(p) != null;

            var saved = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(saved)) Load(FromJson(saved));
        }

        public void SetActive(bool on)
        {
            ui.SetStoryActive(on);
            if (!on) { Select(null); _threadFrom = null; }
            SetTool("select");
        }

        void SetTool(string tool)
        {
            _tool = tool; _threadFrom = null;
            ui.SetStoryTool(tool);
            ui.SetStoryHint(tool switch
            {
                "figure" => "Нажмите на карту, чтобы поставить персонажа.",
                "object" => "Нажмите на карту, чтобы поставить сюжетный объект.",
                "flag" => "Нажмите на карту, чтобы поставить флажок.",
                "thread" => "Нить: выберите первое — персонажа, объект, флажок или место на карте.",
                "delete" => "Нажмите на фигуру или нить, чтобы удалить.",
                _ => "Перетаскивайте фигуры. Нажмите на фигуру, чтобы подписать её. Цвет — для новых фигур и нитей.",
            });
        }

        // ---------------------------------------------------------------- per frame (called by NevishkMap)

        /// Handles story input. Returns true when it used this frame's click, so the map doesn't also act on it.
        public bool Tick(MapPin hoveredPin)
        {
            UpdateTokens();
            UpdateLines();
            if (_dirty && Time.unscaledTime >= _saveAt) Save();

            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            Vector2 p = MapInput.PointerPosition;
            _hoverToken = overUI || camCtl.IsDragging ? null : TokenAt(p);

            // Tooltip for story items, in or out of story mode.
            if (_hoverToken != null && _dragging == null && MapInput.TouchCount == 0)
            {
                var it = _hoverToken.item;
                string name = string.IsNullOrEmpty(it.name) ? KindName(it.kind) : it.name;
                ui.SetTooltip(string.IsNullOrEmpty(it.note) ? name : $"{name}\n<size=85%><color=#A8A08F>{Trim(it.note, 120)}</color></size>", p);
            }

            if (!ui.StoryActive) return false;

            // Keyboard: Esc cancels a half-made string or closes the card; Delete removes the selection.
            if (MapInput.EscapeDown && (_threadFrom != null || _selected != null)) { _threadFrom = null; Select(null); SetTool(_tool); return true; }
            if (_selected != null && !ui.CardFocused && !MapInput.TypingInUI && DeletePressed()) { Delete(_selected); return true; }

            if (MapInput.PrimaryDown && !overUI)
            {
                _pressedOnToken = false;
                var t = TokenAt(p);
                if (t != null && _tool != "delete" && _tool != "thread")
                {
                    _pressedOnToken = true;
                    _dragging = t; _pressPos = p;
                    if (GroundPoint(p, out var gp)) _dragOffset = new Vector3(t.item.x - gp.x, 0, t.item.z - gp.z);
                }
            }
            if (_dragging != null && MapInput.PrimaryHeld)
            {
                if (GroundPoint(p, out var gp))
                {
                    var w = clampToMap(gp + _dragOffset);
                    _dragging.item.x = Round(w.x); _dragging.item.z = Round(w.z);
                    MarkDirty();
                }
                return true;
            }
            if (MapInput.PrimaryUp)
            {
                if (_dragging != null)
                {
                    var d = _dragging; _dragging = null;
                    if ((p - _pressPos).sqrMagnitude < 36f) Select(d);   // a click, not a drag
                    return true;
                }
                if (overUI || camCtl.LastPressWasDrag) return false;
                return Click(p, hoveredPin);
            }
            return _pressedOnToken && MapInput.PrimaryHeld;
        }

        bool Click(Vector2 p, MapPin pin)
        {
            var token = TokenAt(p);
            switch (_tool)
            {
                case "figure": case "object": case "flag":
                    if (token != null) { Select(token); SetTool("select"); return true; }
                    if (!GroundPoint(p, out var gp)) return true;
                    var w = clampToMap(gp);
                    var item = new StoryItem
                    {
                        id = "s" + _nextId++, kind = _tool, name = "", note = "", color = Hex(_colour), x = Round(w.x), z = Round(w.z)
                    };
                    _scene.items.Add(item);
                    var t = Spawn(item);
                    MarkDirty();
                    SetTool("select");
                    Select(t, true);
                    return true;

                case "thread":
                {
                    string id = token != null ? token.item.id : pin != null ? pin.id : null;
                    if (id == null) { _threadFrom = null; SetTool("thread"); return true; }
                    if (_threadFrom == null)
                    {
                        _threadFrom = id;
                        ui.SetStoryHint($"Нить от «{Describe(id)}»: теперь выберите второе. Esc — отмена.");
                        return true;
                    }
                    if (id != _threadFrom)
                    {
                        var link = new StoryLink { a = _threadFrom, b = id, color = Hex(_colour) };
                        _scene.links.Add(link);
                        AddLine(link);
                        MarkDirty();
                    }
                    _threadFrom = null;
                    SetTool("thread");
                    return true;
                }

                case "delete":
                    if (token != null) { Delete(token); return true; }
                    int li = LineAt(p);
                    if (li >= 0) { RemoveLine(li); MarkDirty(); }
                    return true;

                default:   // select
                    if (token != null) { Select(token); return true; }
                    if (_selected != null) { Select(null); return true; }
                    return false;   // let the map open the lore for a landmark
            }
        }

        static bool DeletePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var k = UnityEngine.InputSystem.Keyboard.current;
            return k != null && k.deleteKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Delete);
#endif
        }

        // ---------------------------------------------------------------- tokens

        Token Spawn(StoryItem item)
        {
            var t = new Token { item = item };
            t.go = new GameObject("Story · " + item.kind + " " + item.id);
            t.go.transform.SetParent(_root, false);
            t.body = new GameObject("Body").transform;
            t.body.SetParent(t.go.transform, false);
            var parts = new List<Renderer>();
            Material plain(Color c) => material(c);
            Color col = Col(item.color);
            switch (item.kind)
            {
                case "figure":
                    // A board-game pawn: base, body, collar, head.
                    parts.Add(Part(t.body, PrimitiveType.Cylinder, new Vector3(0, 0.5f, 0), new Vector3(6f, 0.5f, 6f), col));
                    parts.Add(Part(t.body, PrimitiveType.Cylinder, new Vector3(0, 4.2f, 0), new Vector3(3.2f, 3.4f, 3.2f), col));
                    parts.Add(Part(t.body, PrimitiveType.Cylinder, new Vector3(0, 7.7f, 0), new Vector3(4.4f, 0.35f, 4.4f), col));
                    parts.Add(Part(t.body, PrimitiveType.Sphere, new Vector3(0, 10.0f, 0), new Vector3(4.6f, 4.6f, 4.6f), col));
                    t.topLocal = 12.3f;
                    break;
                case "object":
                    // A floating, slowly turning gem over a short post.
                    Part(t.body, PrimitiveType.Cylinder, new Vector3(0, 2.5f, 0), new Vector3(0.5f, 2.5f, 0.5f), new Color(0.2f, 0.2f, 0.2f));
                    t.spin = new GameObject("Gem").transform; t.spin.SetParent(t.body, false); t.spin.localPosition = new Vector3(0, 9f, 0);
                    var gem = new GameObject("Gem mesh", typeof(MeshFilter), typeof(MeshRenderer));
                    gem.transform.SetParent(t.spin, false); gem.transform.localScale = new Vector3(4f, 5.5f, 4f);
                    gem.GetComponent<MeshFilter>().sharedMesh = Octahedron;
                    var gr = gem.GetComponent<MeshRenderer>(); gr.sharedMaterial = Glossy(col); parts.Add(gr);
                    t.topLocal = 14.5f;
                    break;
                default:   // flag
                    Part(t.body, PrimitiveType.Cylinder, new Vector3(0, 7f, 0), new Vector3(0.45f, 7f, 0.45f), new Color(0.25f, 0.24f, 0.22f));
                    t.spin = new GameObject("Cloth").transform; t.spin.SetParent(t.body, false); t.spin.localPosition = new Vector3(0, 11.5f, 0);
                    var cloth = new GameObject("Cloth mesh", typeof(MeshFilter), typeof(MeshRenderer));
                    cloth.transform.SetParent(t.spin, false); cloth.transform.localPosition = new Vector3(3.2f, 0, 0);
                    cloth.transform.localScale = new Vector3(6.4f, 4f, 0.25f);
                    cloth.GetComponent<MeshFilter>().sharedMesh = NevishkBuildings.CubeMesh;
                    var cr = cloth.GetComponent<MeshRenderer>(); cr.sharedMaterial = plain(col); parts.Add(cr);
                    t.topLocal = 14f;
                    break;
            }
            t.tinted = parts.ToArray();
            foreach (var r in t.go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;

            var hit = t.go.AddComponent<CapsuleCollider>();
            hit.center = new Vector3(0, t.topLocal * 0.5f, 0); hit.height = t.topLocal + 2f; hit.radius = 4.5f;
            t.go.AddComponent<StoryTokenTag>().id = item.id;

            t.label = makeLabel(t.go.transform, "");
            t.plate = t.label.GetComponentInChildren<SpriteRenderer>();
            SetLabel(t);
            _tokens[item.id] = t;
            Place(t);
            return t;
        }

        Renderer Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
        {
            var go = new GameObject(type.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = type == PrimitiveType.Sphere ? NevishkBuildings.SphereMesh
                                                    : type == PrimitiveType.Cylinder ? NevishkBuildings.CylinderMesh : NevishkBuildings.CubeMesh;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = Glossy(c);
            return r;
        }

        Material Glossy(Color c) { var m = new Material(material(c)); m.SetFloat("_Smoothness", 0.55f); return m; }

        void Recolour(Token t)
        {
            var c = Col(t.item.color);
            foreach (var r in t.tinted) r.sharedMaterial = Glossy(c);
            SetLabel(t);
        }

        void SetLabel(Token t)
        {
            string name = string.IsNullOrEmpty(t.item.name) ? "" : t.item.name;
            t.label.text = $"<size=140%><color={Hex(Col(t.item.color))}>•</color></size> {(name.Length > 0 ? name : KindName(t.item.kind))}";
            t.label.ForceMeshUpdate();
            if (t.plate != null)
            {
                var b = t.label.textBounds;
                t.plate.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.6f);
                t.plate.size = new Vector2(b.size.x + 6f, b.size.y + 3.2f);
            }
        }

        void Place(Token t)
        {
            var w = new Vector3(t.item.x, 0, t.item.z);
            w.y = ground(w);
            t.go.transform.position = w;
        }

        void UpdateTokens()
        {
            if (cam == null) return;
            float time = Time.unscaledTime;
            foreach (var t in _tokens.Values)
            {
                Place(t);
                // Figures are small next to the city; grow them when the camera is far away.
                float d = Vector3.Distance(cam.transform.position, t.go.transform.position);
                float s = Mathf.Clamp(d / 320f, 1f, 3.2f);
                if (t == _selected) s *= 1f + 0.08f * Mathf.Sin(time * 5f);
                if (t == _dragging) s *= 1.12f;
                t.scale = s;
                t.body.localScale = Vector3.one * s;
                if (t.spin != null)
                {
                    if (t.item.kind == "object") t.spin.localRotation = Quaternion.Euler(0, time * 40f, 0);
                    else t.spin.localRotation = Quaternion.Euler(0, 12f * Mathf.Sin(time * 1.7f + t.item.x), 0);
                }
                t.label.transform.localPosition = Vector3.up * (t.topLocal * s + 5f);
                t.label.transform.rotation = Quaternion.LookRotation(t.label.transform.position - cam.transform.position, cam.transform.up);
                t.label.transform.localScale = Vector3.one * Mathf.Clamp(d / 600f, 0.15f, 2.5f);
                var col = t.go.GetComponent<CapsuleCollider>();
                col.center = new Vector3(0, t.topLocal * s * 0.5f, 0); col.height = t.topLocal * s + 2f; col.radius = 4.5f * s;
            }
        }

        void Select(Token t, bool focusName = false)
        {
            _selected = t;
            if (t == null) { ui.HideCard(); return; }
            ui.ShowCard(KindName(t.item.kind), t.item.name, t.item.note, focusName);
        }

        void Delete(Token t)
        {
            string id = t.item.id;
            _scene.items.Remove(t.item);
            for (int i = _lines.Count - 1; i >= 0; i--)
                if (_lines[i].link.a == id || _lines[i].link.b == id) RemoveLine(i);
            Destroy(t.go);
            _tokens.Remove(id);
            if (_selected == t) Select(null);
            MarkDirty();
        }

        Token TokenAt(Vector2 screen)
        {
            if (cam == null) return null;
            var ray = cam.ScreenPointToRay(screen);
            var hits = Physics.RaycastAll(ray, cam.farClipPlane);
            Token best = null; float bd = float.MaxValue;
            foreach (var h in hits)
            {
                var tag = h.collider.GetComponent<StoryTokenTag>();
                if (tag != null && h.distance < bd && _tokens.TryGetValue(tag.id, out var t)) { best = t; bd = h.distance; }
            }
            return best;
        }

        /// Where the pointer ray meets the ground (marched against the height function, then refined).
        bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            var ray = cam.ScreenPointToRay(screen);
            float step = 6f, t = 0f, max = cam.farClipPlane;
            Vector3 prev = ray.origin;
            while (t < max)
            {
                t += step;
                var q = ray.GetPoint(t);
                if (q.y <= ground(q))
                {
                    float lo = t - step, hi = t;
                    for (int i = 0; i < 12; i++)
                    {
                        float mid = (lo + hi) / 2f; var m = ray.GetPoint(mid);
                        if (m.y <= ground(m)) hi = mid; else lo = mid;
                    }
                    point = ray.GetPoint(hi);
                    return true;
                }
                step = Mathf.Min(step * 1.04f, 40f);
            }
            point = default; return false;
        }

        // ---------------------------------------------------------------- strings

        void AddLine(StoryLink link)
        {
            var go = new GameObject("String", typeof(LineRenderer));
            go.transform.SetParent(_root, false);
            var lr = go.GetComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = 24; lr.numCapVertices = 3;
            lr.alignment = LineAlignment.View; lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.sharedMaterial = _lineMat;
            var c = Col(link.color);
            lr.startColor = lr.endColor = c;
            _lines.Add((link, lr));
        }

        void RemoveLine(int i)
        {
            _scene.links.Remove(_lines[i].link);
            Destroy(_lines[i].line.gameObject);
            _lines.RemoveAt(i);
        }

        Vector3? Anchor(string id)
        {
            if (_tokens.TryGetValue(id, out var t)) return t.go.transform.position + Vector3.up * (t.topLocal * t.scale - 1f);
            return anchorOf(id);
        }

        void UpdateLines()
        {
            for (int i = 0; i < _lines.Count; i++)
            {
                var (link, lr) = _lines[i];
                var a = Anchor(link.a); var b = Anchor(link.b);
                if (a == null || b == null) { lr.enabled = false; continue; }
                lr.enabled = true;
                Vector3 A = a.Value, B = b.Value;
                float len = Vector3.Distance(A, B), sag = len * 0.07f;
                for (int k = 0; k < lr.positionCount; k++)
                {
                    float s = k / (float)(lr.positionCount - 1);
                    lr.SetPosition(k, Vector3.Lerp(A, B, s) + Vector3.down * (4f * sag * s * (1f - s)));
                }
                // Keep the string visible at any zoom: about 2 px wide.
                float d = Vector3.Distance(cam.transform.position, (A + B) * 0.5f);
                lr.widthMultiplier = Mathf.Max(0.5f, d * 0.0035f);
            }
        }

        int LineAt(Vector2 screen)
        {
            int best = -1; float bd = 14f;   // px
            for (int i = 0; i < _lines.Count; i++)
            {
                var lr = _lines[i].line;
                if (!lr.enabled) continue;
                for (int k = 0; k + 1 < lr.positionCount; k++)
                {
                    Vector3 a = cam.WorldToScreenPoint(lr.GetPosition(k)), b = cam.WorldToScreenPoint(lr.GetPosition(k + 1));
                    if (a.z < 0 || b.z < 0) continue;
                    float dd = DistToSegment(screen, a, b);
                    if (dd < bd) { bd = dd; best = i; }
                }
            }
            return best;
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ---------------------------------------------------------------- scenes: load, save, files, links

        void Load(StoryScene scene)
        {
            Select(null);
            foreach (var t in _tokens.Values) Destroy(t.go);
            _tokens.Clear();
            foreach (var (_, lr) in _lines) Destroy(lr.gameObject);
            _lines.Clear();

            _scene = scene ?? new StoryScene();
            _scene.items ??= new List<StoryItem>(); _scene.links ??= new List<StoryLink>();
            _nextId = 1;
            foreach (var it in _scene.items)
            {
                if (string.IsNullOrEmpty(it.id) || _tokens.ContainsKey(it.id)) it.id = "s" + (1000 + _nextId);
                if (it.id.StartsWith("s") && int.TryParse(it.id.Substring(1), out int n)) _nextId = Mathf.Max(_nextId, n + 1);
                if (it.kind != "figure" && it.kind != "object" && it.kind != "flag") it.kind = "figure";
                var w = clampToMap(new Vector3(it.x, 0, it.z)); it.x = w.x; it.z = w.z;
                Spawn(it);
            }
            // Drop strings whose ends no longer exist.
            _scene.links.RemoveAll(l => l == null || Anchor(l.a) == null || Anchor(l.b) == null);
            foreach (var l in _scene.links) AddLine(l);
        }

        void MarkDirty() { _dirty = true; _saveAt = Time.unscaledTime + 0.6f; }

        void Save()
        {
            _dirty = false;
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(_scene));
            PlayerPrefs.Save();
        }

        void Backup()
        {
            if (_scene.items.Count == 0 && _scene.links.Count == 0) return;
            PlayerPrefs.SetString(BackupKey, JsonUtility.ToJson(_scene));
            PlayerPrefs.Save();
        }

        void RestoreBackup()
        {
            var json = PlayerPrefs.GetString(BackupKey, "");
            if (string.IsNullOrEmpty(json)) return;
            Load(FromJson(json));
            MarkDirty();
            ui.Notice("Прежний сюжет возвращён.");
        }

        static StoryScene FromJson(string json)
        {
            try { return JsonUtility.FromJson<StoryScene>(json); }
            catch (Exception e) { Debug.LogWarning("[Nevishk] Could not read story: " + e.Message); return null; }
        }

        void SaveFile()
        {
            Save();
            string json = JsonUtility.ToJson(_scene, true);
            string file = "nevishk-syuzhet-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json";
#if UNITY_WEBGL && !UNITY_EDITOR
            NevishkBrowser.Download(file, json);
#else
            string path = System.IO.Path.Combine(Application.persistentDataPath, file);
            System.IO.File.WriteAllText(path, json);
            Debug.Log("[Nevishk] Story saved to " + path);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(path);
#endif
#endif
            ui.Notice("Сюжет сохранён в файл.");
        }

        void OpenFile()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            NevishkBrowser.OpenTextFile(gameObject.name, nameof(OnStoryFileLoaded));
#elif UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel("Открыть сюжет", Application.persistentDataPath, "json");
            if (!string.IsNullOrEmpty(path)) OnStoryFileLoaded(System.IO.File.ReadAllText(path));
#else
            ui.Notice("Открытие файлов работает в веб-версии.");
#endif
        }

        /// Called from the browser with the chosen file's text.
        public void OnStoryFileLoaded(string json)
        {
            var scene = FromJson(json);
            if (scene == null || scene.items == null) { ui.Notice("Это не файл сюжета."); return; }
            Backup();
            Load(scene);
            MarkDirty();
            ui.Notice($"Сюжет открыт: {scene.items.Count} фигур, {scene.links.Count} нитей.", "Вернуть прежний", RestoreBackup, 10f);
        }

        void ShareLink()
        {
            string payload = Base64Url(Encoding.UTF8.GetBytes(JsonUtility.ToJson(_scene)));
            string hash = "story=" + payload;
#if UNITY_WEBGL && !UNITY_EDITOR
            NevishkBrowser.CopyLinkWithHash(hash);
            ui.Notice("Ссылка на сюжет скопирована. Кто её откроет, увидит эту сцену.");
#else
            GUIUtility.systemCopyBuffer = "#" + hash;
            Debug.Log("[Nevishk] Story link hash copied (" + hash.Length + " chars).");
            ui.Notice("Ссылка скопирована (в редакторе — только часть после #).");
#endif
        }

        /// Opens a scene shared as #story=… . The visitor's own scene is kept as a backup.
        public void OpenShared(string payload)
        {
            StoryScene scene = null;
            try { scene = FromJson(Encoding.UTF8.GetString(FromBase64Url(payload))); } catch { }
            if (scene == null || scene.items == null) { ui.Notice("Ссылка на сюжет повреждена."); return; }
            Backup();
            Load(scene);
            MarkDirty();
            ui.Notice("Открыт сюжет по ссылке.", "Вернуть мой", RestoreBackup, 12f);
        }

        static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        static byte[] FromBase64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }

        // ---------------------------------------------------------------- small helpers

        string Describe(string id) =>
            _tokens.TryGetValue(id, out var t) ? (string.IsNullOrEmpty(t.item.name) ? KindName(t.item.kind) : t.item.name) : nameOf(id) ?? id;

        static string KindName(string kind) => kind == "object" ? "Сюжетный объект" : kind == "flag" ? "Флажок" : "Персонаж";
        static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";
        static float Round(float f) => Mathf.Round(f * 10f) / 10f;
        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        static Color Col(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : LorePanelUI.StoryColours[0];

        static Mesh _octa;
        static Mesh Octahedron
        {
            get
            {
                if (_octa != null) return _octa;
                var v = new[] { Vector3.up * 0.5f, Vector3.down * 0.5f, Vector3.right * 0.5f, Vector3.left * 0.5f, Vector3.forward * 0.5f, Vector3.back * 0.5f };
                int[] faces = { 0, 4, 2, 0, 2, 5, 0, 5, 3, 0, 3, 4, 1, 2, 4, 1, 5, 2, 1, 3, 5, 1, 4, 3 };
                var verts = new Vector3[faces.Length]; var tris = new int[faces.Length];
                for (int i = 0; i < faces.Length; i++) { verts[i] = v[faces[i]]; tris[i] = i; }
                _octa = new Mesh { name = "Gem", vertices = verts, triangles = tris };
                _octa.RecalculateNormals(); _octa.RecalculateBounds();
                return _octa;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _octa = null;
    }

    /// Marks a story token's collider.
    public class StoryTokenTag : MonoBehaviour { public string id; }
}
