using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace Nevishk
{
    // ---------------------------------------------------------------- saved data

    /// A story: the cast (items) and a sequence of steps. Each step says where every item stands (items absent
    /// from a step are off the board in it), which strings are tied, and a caption.
    [Serializable]
    public class StoryScene
    {
        public int v = 2;
        public List<StoryItem> items = new List<StoryItem>();
        public List<StoryStep> steps = new List<StoryStep>();
        public List<StoryLink> links;   // v1 scenes only (a single step); read and converted on load
    }

    [Serializable]
    public class StoryItem
    {
        public string id, kind, shape, name, note, color, portrait;   // kind: figure | object | flag; color #RRGGBB; portrait: base64 JPEG
        public float x, z;                                           // v1 position; v2 keeps positions per step
    }

    [Serializable]
    public class StoryStep
    {
        public string text = "";
        public List<StoryPos> pos = new List<StoryPos>();
        public List<StoryLink> links = new List<StoryLink>();
    }

    [Serializable]
    public class StoryPos { public string id; public float x, z; }

    [Serializable]
    public class StoryLink { public string a, b, color; }   // ends: story item ids or district / landmark ids

    /// <summary>
    /// Story mode: people put their own figures (several shapes, with portraits), story objects and flags on the
    /// map, tie things together with detective-board strings, and tell it step by step: each step is a
    /// arrangement with a caption, and the story can be played back with figures gliding between steps.
    /// Autosaves in the browser; exports as a file or a link (#story=…).
    /// </summary>
    public class StoryMode : MonoBehaviour
    {
        const string PrefsKey = "nevishk_story", BackupKey = "nevishk_story_backup";

        public static readonly (string id, string label)[] Shapes =
            { ("pawn", "Пешка"), ("meeple", "Человек"), ("queen", "Ферзь"), ("cloak", "Плащ"), ("rook", "Ладья") };

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
        int _step;
        StoryStep Step => _scene.steps[_step];

        readonly Dictionary<string, Token> _tokens = new Dictionary<string, Token>();
        readonly List<(StoryLink link, LineRenderer line)> _lines = new List<(StoryLink, LineRenderer)>();
        bool _linesDirty;
        Transform _root;
        Material _lineMat;

        string _tool = "select";
        Color _colour = LorePanelUI.StoryColours[0];
        Token _selected, _dragging, _hoverToken;
        string _threadFrom;
        Vector3 _dragOffset;
        Vector2 _pressPos;
        bool _pressedOnToken, _dirty, _playing;
        float _saveAt;
        int _nextId = 1;
        Coroutine _play;

        class Token
        {
            public StoryItem item;
            public GameObject go;
            public Transform body, spin;
            public TextMeshPro label;
            public SpriteRenderer plate, portrait;
            public Renderer[] tinted;
            public float topLocal;          // anchor height before distance scaling
            public float scale = 1f, appear;
            public Vector2 shown;           // where it's drawn (glides toward the step's position)
            public bool placed;
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
            ui.StoryClear += () => { StopPlay(); Backup(); Load(new StoryScene()); MarkDirty(); ui.Notice("Сюжет очищен.", "Вернуть", RestoreBackup); };

            ui.CardNameChanged += v => { if (_selected == null) return; _selected.item.name = v; SetLabel(_selected); MarkDirty(); };
            ui.CardNoteChanged += v => { if (_selected == null) return; _selected.item.note = v; MarkDirty(); };
            ui.CardDelete += () => { if (_selected != null) Delete(_selected); };
            ui.CardDone += () => Select(null);
            ui.CardShapeChosen += shape =>
            {
                if (_selected == null || _selected.item.kind != "figure") return;
                _selected.item.shape = shape;
                var item = _selected.item; var shown = _selected.shown;
                Despawn(_selected); var t = Spawn(item); t.shown = shown; t.placed = true; t.appear = 1f;
                Select(t);
                MarkDirty();
            };
            ui.CardPortraitPick += PickPortrait;
            ui.CardPortraitClear += () =>
            {
                if (_selected == null) return;
                _selected.item.portrait = null; ForgetPortrait(_selected.item.id); ApplyPortrait(_selected); RefreshCard(); MarkDirty();
            };

            ui.StepPrev += () => { StopPlay(); GoTo(_step - 1); };
            ui.StepNext += () => { StopPlay(); GoTo(_step + 1); };
            ui.StepAdd += AddStep;
            ui.StepDelete += DeleteStep;
            ui.StepPlay += () => { if (_playing) StopPlay(); else StartPlay(); };
            ui.StepCaptionChanged += v => { Step.text = v; MarkDirty(); };

            ui.SetStoryColour(_colour);
            ui.SetStoryTool(_tool);

            // Press on a token = drag it, not the camera.
            camCtl.claimPress = p => ui.StoryActive && !_playing && _tool != "delete" && TokenAt(p) != null;

            var saved = PlayerPrefs.GetString(PrefsKey, "");
            Load(string.IsNullOrEmpty(saved) ? new StoryScene() : FromJson(saved));
        }

        public void SetActive(bool on)
        {
            ui.SetStoryActive(on);
            if (!on) { Select(null); _threadFrom = null; StopPlay(); }
            SetTool("select");
            RefreshSteps();
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
                "delete" => "Нажмите на фигуру или нить, чтобы убрать её с этого шага и следующих.",
                _ => "Перетаскивайте фигуры. Нажмите на фигуру, чтобы подписать её. Шаги — внизу.",
            });
        }

        // ---------------------------------------------------------------- per frame (called by NevishkMap)

        /// Handles story input. Returns true when it used this frame's click, so the map doesn't also act on it.
        public bool Tick(MapPin hoveredPin)
        {
            UpdateTokens();
            if (_linesDirty) RebuildLines();
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
            if (_playing)
            {
                // Any click or Esc stops the playback.
                if (MapInput.EscapeDown || (MapInput.PrimaryUp && !overUI && !camCtl.LastPressWasDrag)) { StopPlay(); return true; }
                return false;
            }

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
                    var pos = PosOf(Step, t.item.id);
                    if (pos != null && GroundPoint(p, out var gp)) _dragOffset = new Vector3(pos.x - gp.x, 0, pos.z - gp.z);
                }
            }
            if (_dragging != null && MapInput.PrimaryHeld)
            {
                var pos = PosOf(Step, _dragging.item.id);
                if (pos != null && GroundPoint(p, out var gp))
                {
                    var w = clampToMap(gp + _dragOffset);
                    pos.x = Round(w.x); pos.z = Round(w.z);
                    _dragging.shown = new Vector2(pos.x, pos.z);
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
                        id = "s" + _nextId++, kind = _tool, shape = _tool == "figure" ? "pawn" : null,
                        name = "", note = "", color = Hex(_colour)
                    };
                    _scene.items.Add(item);
                    // A new piece enters at this step and stays for the following ones.
                    for (int s = _step; s < _scene.steps.Count; s++)
                        _scene.steps[s].pos.Add(new StoryPos { id = item.id, x = Round(w.x), z = Round(w.z) });
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
                        string a = _threadFrom, col = Hex(_colour);
                        for (int s = _step; s < _scene.steps.Count; s++)
                        {
                            var links = _scene.steps[s].links;
                            if (!links.Exists(l => (l.a == a && l.b == id) || (l.a == id && l.b == a)))
                                links.Add(new StoryLink { a = a, b = id, color = col });
                        }
                        _linesDirty = true;
                        MarkDirty();
                    }
                    _threadFrom = null;
                    SetTool("thread");
                    return true;
                }

                case "delete":
                    if (token != null) { Delete(token); return true; }
                    int li = LineAt(p);
                    if (li >= 0)
                    {
                        var dead = _lines[li].link;
                        for (int s = _step; s < _scene.steps.Count; s++)
                            _scene.steps[s].links.RemoveAll(l => (l.a == dead.a && l.b == dead.b) || (l.a == dead.b && l.b == dead.a));
                        _linesDirty = true;
                        MarkDirty();
                    }
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

        // ---------------------------------------------------------------- steps

        static StoryPos PosOf(StoryStep s, string id) => s.pos.Find(q => q.id == id);

        void GoTo(int step)
        {
            if (_scene.steps.Count == 0) return;
            _step = Mathf.Clamp(step, 0, _scene.steps.Count - 1);
            _threadFrom = null;
            if (_selected != null && PosOf(Step, _selected.item.id) == null) Select(null);
            _linesDirty = true;
            RefreshSteps();
        }

        void AddStep()
        {
            StopPlay();
            // The new step starts as a copy of this one: move pieces to tell what happens next.
            var copy = new StoryStep { text = "" };
            foreach (var q in Step.pos) copy.pos.Add(new StoryPos { id = q.id, x = q.x, z = q.z });
            foreach (var l in Step.links) copy.links.Add(new StoryLink { a = l.a, b = l.b, color = l.color });
            _scene.steps.Insert(_step + 1, copy);
            GoTo(_step + 1);
            MarkDirty();
            ui.Notice($"Шаг {_step + 1}: передвиньте фигуры — так рассказывается, что было дальше.");
        }

        void DeleteStep()
        {
            StopPlay();
            if (_scene.steps.Count <= 1) { ui.Notice("Это единственный шаг."); return; }
            _scene.steps.RemoveAt(_step);
            // Pieces that no longer appear in any step leave the cast.
            _scene.items.RemoveAll(it => !_scene.steps.Exists(s => PosOf(s, it.id) != null) && RemoveToken(it.id));
            GoTo(Mathf.Min(_step, _scene.steps.Count - 1));
            MarkDirty();
        }

        void StartPlay()
        {
            if (_scene.steps.Count < 2) { ui.Notice("Добавьте хотя бы два шага, чтобы проиграть сюжет."); return; }
            Select(null); SetTool("select");
            _playing = true;
            if (_play != null) StopCoroutine(_play);
            _play = StartCoroutine(Play());
        }

        IEnumerator Play()
        {
            int from = _step >= _scene.steps.Count - 1 ? 0 : _step;
            for (int s = from; s < _scene.steps.Count; s++)
            {
                GoTo(s);
                ui.ShowStoryCaption(s + 1, Step.text);
                // Long captions stay up longer.
                float hold = 2.6f + Mathf.Clamp((Step.text ?? "").Length * 0.045f, 0f, 6f);
                yield return new WaitForSecondsRealtime(hold);
            }
            StopPlay();
        }

        void StopPlay()
        {
            if (_play != null) { StopCoroutine(_play); _play = null; }
            _playing = false;
            ui.ShowStoryCaption(0, null);
            RefreshSteps();
        }

        void RefreshSteps() => ui.SetSteps(_step, _scene.steps.Count, Step.text, _playing);

        // ---------------------------------------------------------------- tokens

        Token Spawn(StoryItem item)
        {
            var t = new Token { item = item };
            t.go = new GameObject("Story · " + item.kind + " " + item.id);
            t.go.transform.SetParent(_root, false);
            t.body = new GameObject("Body").transform;
            t.body.SetParent(t.go.transform, false);
            var parts = new List<Renderer>();
            Color col = Col(item.color);
            switch (item.kind)
            {
                case "figure": t.topLocal = BuildFigure(t, item.shape, col, parts); break;
                case "object":
                    // A floating, slowly turning gem over a short post.
                    Part(t.body, PrimitiveType.Cylinder, new Vector3(0, 2.5f, 0), new Vector3(0.5f, 2.5f, 0.5f), new Color(0.2f, 0.2f, 0.2f));
                    t.spin = new GameObject("Gem").transform; t.spin.SetParent(t.body, false); t.spin.localPosition = new Vector3(0, 9f, 0);
                    parts.Add(MeshPart(t.spin, Octahedron, Vector3.zero, new Vector3(4f, 5.5f, 4f), Quaternion.identity, col));
                    t.topLocal = 14.5f;
                    break;
                default:   // flag
                    Part(t.body, PrimitiveType.Cylinder, new Vector3(0, 7f, 0), new Vector3(0.45f, 7f, 0.45f), new Color(0.25f, 0.24f, 0.22f));
                    t.spin = new GameObject("Cloth").transform; t.spin.SetParent(t.body, false); t.spin.localPosition = new Vector3(0, 11.5f, 0);
                    parts.Add(MeshPart(t.spin, NevishkBuildings.CubeMesh, new Vector3(3.2f, 0, 0), new Vector3(6.4f, 4f, 0.25f), Quaternion.identity, col));
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

            var pgo = new GameObject("Portrait", typeof(SpriteRenderer));
            pgo.transform.SetParent(t.go.transform, false);
            t.portrait = pgo.GetComponent<SpriteRenderer>();
            t.portrait.sortingOrder = 3;
            t.portrait.shadowCastingMode = ShadowCastingMode.Off;
            ApplyPortrait(t);

            _tokens[item.id] = t;
            return t;
        }

        /// The figure shapes. Each returns the height its string attaches at.
        float BuildFigure(Token t, string shape, Color col, List<Renderer> parts)
        {
            var b = t.body;
            switch (shape)
            {
                case "meeple":
                    // The board-game person: legs, body, arms out, round head.
                    parts.Add(Part(b, PrimitiveType.Cube, new Vector3(-1.3f, 2.2f, 0), new Vector3(1.9f, 4.4f, 2.2f), col));
                    parts.Add(Part(b, PrimitiveType.Cube, new Vector3(1.3f, 2.2f, 0), new Vector3(1.9f, 4.4f, 2.2f), col));
                    parts.Add(Part(b, PrimitiveType.Cube, new Vector3(0, 6.6f, 0), new Vector3(4.6f, 4.6f, 2.2f), col));
                    parts.Add(Part(b, PrimitiveType.Cube, new Vector3(0, 7.6f, 0), new Vector3(9.4f, 1.9f, 2.2f), col));
                    parts.Add(Part(b, PrimitiveType.Sphere, new Vector3(0, 10.6f, 0), new Vector3(3.6f, 3.6f, 2.6f), col));
                    return 12.5f;
                case "queen":
                    // A taller pawn with a ring of crown points.
                    parts.Add(Part(b, PrimitiveType.Cylinder, new Vector3(0, 0.5f, 0), new Vector3(6.4f, 0.5f, 6.4f), col));
                    parts.Add(Part(b, PrimitiveType.Cylinder, new Vector3(0, 5.2f, 0), new Vector3(3.0f, 4.4f, 3.0f), col));
                    parts.Add(Part(b, PrimitiveType.Cylinder, new Vector3(0, 9.7f, 0), new Vector3(4.6f, 0.35f, 4.6f), col));
                    parts.Add(Part(b, PrimitiveType.Sphere, new Vector3(0, 11.6f, 0), new Vector3(4.2f, 3.6f, 4.2f), col));
                    for (int k = 0; k < 6; k++)
                    {
                        float a = k * Mathf.PI / 3f;
                        parts.Add(MeshPart(b, NevishkBuildings.SpireMesh, new Vector3(Mathf.Cos(a) * 1.7f, 12.8f, Mathf.Sin(a) * 1.7f),
                                           new Vector3(1.1f, 2.4f, 1.1f), Quaternion.identity, col));
                    }
                    return 15.5f;
                case "cloak":
                    // A hooded figure: a long cloak (cone) and a hood.
                    parts.Add(MeshPart(b, NevishkBuildings.SpireMesh, Vector3.zero, new Vector3(6.4f, 10.5f, 6.4f), Quaternion.identity, col));
                    parts.Add(Part(b, PrimitiveType.Sphere, new Vector3(0, 9.6f, 0), new Vector3(3.8f, 4.2f, 3.8f), col));
                    return 12f;
                case "rook":
                    // A squat tower with battlements.
                    parts.Add(Part(b, PrimitiveType.Cylinder, new Vector3(0, 0.5f, 0), new Vector3(6.4f, 0.5f, 6.4f), col));
                    parts.Add(Part(b, PrimitiveType.Cylinder, new Vector3(0, 4.8f, 0), new Vector3(4.6f, 4.0f, 4.6f), col));
                    for (int k = 0; k < 4; k++)
                    {
                        float a = k * Mathf.PI / 2f + Mathf.PI / 4f;
                        parts.Add(Part(b, PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 1.7f, 9.6f, Mathf.Sin(a) * 1.7f), new Vector3(1.5f, 1.6f, 1.5f), col));
                    }
                    return 11f;
                default:   // pawn
                    parts.Add(Part(b, PrimitiveType.Cylinder, new Vector3(0, 0.5f, 0), new Vector3(6f, 0.5f, 6f), col));
                    parts.Add(Part(b, PrimitiveType.Cylinder, new Vector3(0, 4.2f, 0), new Vector3(3.2f, 3.4f, 3.2f), col));
                    parts.Add(Part(b, PrimitiveType.Cylinder, new Vector3(0, 7.7f, 0), new Vector3(4.4f, 0.35f, 4.4f), col));
                    parts.Add(Part(b, PrimitiveType.Sphere, new Vector3(0, 10.0f, 0), new Vector3(4.6f, 4.6f, 4.6f), col));
                    return 12.3f;
            }
        }

        Renderer Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c) =>
            MeshPart(parent, type == PrimitiveType.Sphere ? NevishkBuildings.SphereMesh
                           : type == PrimitiveType.Cylinder ? NevishkBuildings.CylinderMesh : NevishkBuildings.CubeMesh,
                     pos, scale, Quaternion.identity, c);

        Renderer MeshPart(Transform parent, Mesh mesh, Vector3 pos, Vector3 scale, Quaternion rot, Color c)
        {
            var go = new GameObject("Part", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localRotation = rot;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = Glossy(c);
            return r;
        }

        readonly Dictionary<Color, Material> _glossy = new Dictionary<Color, Material>();
        Material Glossy(Color c)
        {
            if (_glossy.TryGetValue(c, out var m) && m != null) return m;
            m = new Material(material(c)); m.SetFloat("_Smoothness", 0.55f);
            return _glossy[c] = m;
        }

        void Recolour(Token t)
        {
            var c = Col(t.item.color);
            foreach (var r in t.tinted) r.sharedMaterial = Glossy(c);
            SetLabel(t);
        }

        void SetLabel(Token t)
        {
            string name = string.IsNullOrEmpty(t.item.name) ? KindName(t.item.kind) : t.item.name;
            t.label.text = $"<size=140%><color={Hex(Col(t.item.color))}>•</color></size> {name}";
            t.label.ForceMeshUpdate();
            if (t.plate != null)
            {
                var b = t.label.textBounds;
                t.plate.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.6f);
                t.plate.size = new Vector2(b.size.x + 6f, b.size.y + 3.2f);
            }
        }

        void Despawn(Token t)
        {
            if (_selected == t) _selected = null;
            Destroy(t.go);
            _tokens.Remove(t.item.id);
        }

        bool RemoveToken(string id)
        {
            if (_tokens.TryGetValue(id, out var t)) Despawn(t);
            ForgetPortrait(id);
            return true;
        }

        void UpdateTokens()
        {
            if (cam == null || _scene.steps.Count == 0) return;
            float time = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            foreach (var t in _tokens.Values)
            {
                var pos = PosOf(Step, t.item.id);
                // Off the board in this step: shrink away.
                t.appear = Mathf.MoveTowards(t.appear, pos != null ? 1f : 0f, dt * 3f);
                if (t.appear <= 0f) { if (t.go.activeSelf) t.go.SetActive(false); continue; }
                if (!t.go.activeSelf) t.go.SetActive(true);
                if (pos != null)
                {
                    var target = new Vector2(pos.x, pos.z);
                    if (!t.placed) { t.shown = target; t.placed = true; }
                    else if (t != _dragging) t.shown = Vector2.Lerp(t.shown, target, 1f - Mathf.Exp(-4f * dt));   // glide between steps
                }
                var w = new Vector3(t.shown.x, 0f, t.shown.y);
                w.y = ground(w);
                t.go.transform.position = w;

                // Figures are small next to the city; grow them when the camera is far away.
                float d = Vector3.Distance(cam.transform.position, w);
                float s = Mathf.Clamp(d / 320f, 1f, 3.2f) * Mathf.SmoothStep(0f, 1f, t.appear);
                if (t == _selected) s *= 1f + 0.08f * Mathf.Sin(time * 5f);
                if (t == _dragging) s *= 1.12f;
                t.scale = s;
                t.body.localScale = Vector3.one * s;
                if (t.spin != null)
                {
                    if (t.item.kind == "object") t.spin.localRotation = Quaternion.Euler(0, time * 40f, 0);
                    else t.spin.localRotation = Quaternion.Euler(0, 12f * Mathf.Sin(time * 1.7f + t.shown.x), 0);
                }
                Quaternion face = Quaternion.LookRotation(t.go.transform.position - cam.transform.position, cam.transform.up);
                float top = t.topLocal * s;
                bool hasPortrait = t.portrait.sprite != null;
                if (hasPortrait)
                {
                    float ps = 7.5f * Mathf.Max(s, 1f);
                    t.portrait.transform.localPosition = Vector3.up * (top + 2f + ps * 0.5f);
                    t.portrait.transform.rotation = face;
                    t.portrait.transform.localScale = Vector3.one * ps;
                    top += 2f + ps;
                }
                t.label.transform.localPosition = Vector3.up * (top + 5f);
                t.label.transform.rotation = Quaternion.LookRotation(t.label.transform.position - cam.transform.position, cam.transform.up);
                t.label.transform.localScale = Vector3.one * Mathf.Clamp(d / 600f, 0.15f, 2.5f);
                var col = t.go.GetComponent<CapsuleCollider>();
                col.center = new Vector3(0, top * 0.5f, 0); col.height = top + 2f; col.radius = 4.5f * s;
            }
        }

        void Select(Token t, bool focusName = false)
        {
            _selected = t;
            if (t == null) { ui.HideCard(); return; }
            RefreshCard(focusName);
        }

        void RefreshCard(bool focusName = false)
        {
            if (_selected == null) return;
            var it = _selected.item;
            ui.ShowCard(KindName(it.kind), it.name, it.note, focusName, it.kind == "figure", it.shape ?? "pawn", PortraitRound(it));
        }

        /// Takes the piece off the board from this step on; once it's in no step at all it leaves the cast.
        void Delete(Token t)
        {
            string id = t.item.id;
            for (int s = _step; s < _scene.steps.Count; s++)
            {
                _scene.steps[s].pos.RemoveAll(q => q.id == id);
                _scene.steps[s].links.RemoveAll(l => l.a == id || l.b == id);
            }
            if (!_scene.steps.Exists(s => PosOf(s, id) != null))
            {
                _scene.items.Remove(t.item);
                RemoveToken(id);
            }
            if (_selected == t) Select(null);
            _linesDirty = true;
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
                if (tag != null && h.distance < bd && _tokens.TryGetValue(tag.id, out var t) && t.appear > 0.5f) { best = t; bd = h.distance; }
            }
            return best;
        }

        /// Where the pointer ray meets the ground (marched against the height function, then refined).
        bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            var ray = cam.ScreenPointToRay(screen);
            float step = 6f, t = 0f, max = cam.farClipPlane;
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

        // ---------------------------------------------------------------- portraits

        readonly Dictionary<string, (Texture2D round, Sprite sprite)> _portraits = new Dictionary<string, (Texture2D, Sprite)>();
        string _portraitFor;

        void PickPortrait()
        {
            if (_selected == null) return;
            _portraitFor = _selected.item.id;
#if UNITY_WEBGL && !UNITY_EDITOR
            NevishkBrowser.OpenImageFile(gameObject.name, nameof(OnPortraitLoaded), 160);
#elif UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters("Портрет", "", new[] { "Изображения", "png,jpg,jpeg" });
            if (!string.IsNullOrEmpty(path)) OnPortraitLoaded(Convert.ToBase64String(System.IO.File.ReadAllBytes(path)));
#else
            ui.Notice("Загрузка портретов работает в веб-версии.");
#endif
        }

        /// Called from the browser with the picked image (base64; the page has already cropped it square and small).
        public void OnPortraitLoaded(string base64)
        {
            if (_portraitFor == null || !_tokens.TryGetValue(_portraitFor, out var t)) return;
            var square = SquarePortrait(base64, 128);
            if (square == null) { ui.Notice("Не удалось прочитать картинку."); return; }
            t.item.portrait = Convert.ToBase64String(square.EncodeToJPG(85));
            Destroy(square);
            ForgetPortrait(t.item.id);
            ApplyPortrait(t);
            if (_selected == t) RefreshCard();
            MarkDirty();
        }

        /// Decodes an image and centre-crops / scales it to size × size.
        static Texture2D SquarePortrait(string base64, int size)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64); } catch { return null; }
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(bytes)) { Destroy(src); return null; }
            int side = Mathf.Min(src.width, src.height);
            float ox = (src.width - side) / 2f, oy = (src.height - side) / 2f;
            var dst = new Texture2D(size, size, TextureFormat.RGB24, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = src.GetPixelBilinear((ox + (x + 0.5f) * side / size) / src.width, (oy + (y + 0.5f) * side / size) / src.height);
            dst.SetPixels(px); dst.Apply();
            Destroy(src);
            return dst;
        }

        /// Round version of the item's portrait (alpha circle, thin bone rim), cached; null without a portrait.
        Texture2D PortraitRound(StoryItem it)
        {
            if (string.IsNullOrEmpty(it.portrait)) return null;
            if (_portraits.TryGetValue(it.id, out var cached) && cached.round != null) return cached.round;
            var sq = SquarePortrait(it.portrait, 128);
            if (sq == null) return null;
            const int n = 128;
            var round = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            var src = sq.GetPixels(); var px = new Color[n * n];
            Color rim = new Color(0.93f, 0.91f, 0.85f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                Color c = d > n / 2f - 5f ? rim : src[y * n + x];
                c.a = Mathf.Clamp01(n / 2f - d);
                px[y * n + x] = c;
            }
            round.SetPixels(px); round.Apply(true);
            Destroy(sq);
            var sprite = Sprite.Create(round, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            _portraits[it.id] = (round, sprite);
            return round;
        }

        void ApplyPortrait(Token t)
        {
            PortraitRound(t.item);
            t.portrait.sprite = _portraits.TryGetValue(t.item.id, out var p) ? p.sprite : null;
            t.portrait.enabled = t.portrait.sprite != null;
        }

        void ForgetPortrait(string id)
        {
            if (_portraits.TryGetValue(id, out var p))
            {
                if (p.sprite != null) Destroy(p.sprite);
                if (p.round != null) Destroy(p.round);
                _portraits.Remove(id);
            }
        }

        // ---------------------------------------------------------------- strings

        void RebuildLines()
        {
            _linesDirty = false;
            foreach (var (_, lr) in _lines) Destroy(lr.gameObject);
            _lines.Clear();
            if (_scene.steps.Count == 0) return;
            foreach (var link in Step.links)
            {
                var go = new GameObject("String", typeof(LineRenderer));
                go.transform.SetParent(_root, false);
                var lr = go.GetComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.positionCount = 24; lr.numCapVertices = 3;
                lr.alignment = LineAlignment.View; lr.textureMode = LineTextureMode.Stretch;
                lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
                lr.sharedMaterial = _lineMat;
                lr.startColor = lr.endColor = Col(link.color);
                _lines.Add((link, lr));
            }
        }

        Vector3? Anchor(string id)
        {
            if (_tokens.TryGetValue(id, out var t))
            {
                if (!t.go.activeSelf || t.appear < 0.3f) return null;
                return t.go.transform.position + Vector3.up * (t.topLocal * t.scale - 1f);
            }
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
            foreach (var id in new List<string>(_portraits.Keys)) ForgetPortrait(id);

            _scene = scene ?? new StoryScene();
            _scene.items ??= new List<StoryItem>();
            _scene.steps ??= new List<StoryStep>();
            // v1: one arrangement, positions on the items and strings on the scene → a single step.
            if (_scene.steps.Count == 0)
            {
                var first = new StoryStep();
                foreach (var it in _scene.items) if (it != null) first.pos.Add(new StoryPos { id = it.id, x = it.x, z = it.z });
                if (_scene.links != null) first.links.AddRange(_scene.links);
                _scene.steps.Add(first);
            }
            _scene.links = null;
            _scene.v = 2;

            _nextId = 1;
            _scene.items.RemoveAll(it => it == null || string.IsNullOrEmpty(it.id));
            var seen = new HashSet<string>();
            _scene.items.RemoveAll(it => !seen.Add(it.id));
            foreach (var it in _scene.items)
            {
                if (it.id.StartsWith("s") && int.TryParse(it.id.Substring(1), out int n)) _nextId = Mathf.Max(_nextId, n + 1);
                if (it.kind != "figure" && it.kind != "object" && it.kind != "flag") it.kind = "figure";
                Spawn(it);
            }
            foreach (var s in _scene.steps)
            {
                s.pos ??= new List<StoryPos>(); s.links ??= new List<StoryLink>(); s.text ??= "";
                s.pos.RemoveAll(q => q == null || !_tokens.ContainsKey(q.id));
                foreach (var q in s.pos) { var w = clampToMap(new Vector3(q.x, 0, q.z)); q.x = w.x; q.z = w.z; }
                s.links.RemoveAll(l => l == null || string.IsNullOrEmpty(l.a) || string.IsNullOrEmpty(l.b)
                                       || (!_tokens.ContainsKey(l.a) && anchorOf(l.a) == null) || (!_tokens.ContainsKey(l.b) && anchorOf(l.b) == null));
            }
            _step = 0;
            _linesDirty = true;
            RefreshSteps();
        }

        void MarkDirty() { _dirty = true; _saveAt = Time.unscaledTime + 0.6f; RefreshSteps(); }

        void Save()
        {
            _dirty = false;
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(_scene));
            PlayerPrefs.Save();
        }

        bool IsEmpty => _scene.items.Count == 0;

        void Backup()
        {
            if (IsEmpty) return;
            PlayerPrefs.SetString(BackupKey, JsonUtility.ToJson(_scene));
            PlayerPrefs.Save();
        }

        void RestoreBackup()
        {
            var json = PlayerPrefs.GetString(BackupKey, "");
            if (string.IsNullOrEmpty(json)) return;
            StopPlay();
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
            StopPlay();
            Backup();
            Load(scene);
            MarkDirty();
            ui.Notice($"Сюжет открыт: {_scene.items.Count} фигур, {_scene.steps.Count} шаг(ов).", "Вернуть прежний", RestoreBackup, 10f);
        }

        void ShareLink()
        {
            // Portraits would make the address enormous; they travel in files only.
            var copy = FromJson(JsonUtility.ToJson(_scene));
            bool hadPortraits = false;
            foreach (var it in copy.items) if (!string.IsNullOrEmpty(it.portrait)) { it.portrait = null; hadPortraits = true; }
            string payload = Base64Url(Encoding.UTF8.GetBytes(JsonUtility.ToJson(copy)));
            string hash = "story=" + payload;
            string extra = hadPortraits ? " Портреты в ссылку не входят — для них сохраните файл." : "";
#if UNITY_WEBGL && !UNITY_EDITOR
            NevishkBrowser.CopyLinkWithHash(hash);
            ui.Notice("Ссылка на сюжет скопирована." + extra, null, null, 8f);
#else
            GUIUtility.systemCopyBuffer = "#" + hash;
            Debug.Log("[Nevishk] Story link hash copied (" + hash.Length + " chars).");
            ui.Notice("Ссылка скопирована (в редакторе — только часть после #)." + extra, null, null, 8f);
#endif
        }

        /// Opens a scene shared as #story=… . The visitor's own scene is kept as a backup.
        public void OpenShared(string payload)
        {
            StoryScene scene = null;
            try { scene = FromJson(Encoding.UTF8.GetString(FromBase64Url(payload))); } catch { }
            if (scene == null || scene.items == null) { ui.Notice("Ссылка на сюжет повреждена."); return; }
            StopPlay();
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
