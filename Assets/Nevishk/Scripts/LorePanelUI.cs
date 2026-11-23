using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using T = Nevishk.NevishkTheme;

namespace Nevishk
{
    /// <summary>
    /// Builds the whole screen UI at runtime: header with search, category legend, district bar,
    /// hover tooltip, and the reading panel for districts, landmarks and archive documents.
    /// All colours, sizes and fonts come from NevishkTheme / NevishkFonts.
    /// </summary>
    public class LorePanelUI : MonoBehaviour
    {
        public event Action<string> DistrictClicked;
        public event Action<string> LandmarkClicked;
        public event Action ResetClicked;
        public event Action PanelClosed;
        /// <summary>Raised after an entry renders: (entry id or null, wasBackNavigation).</summary>
        public event Action<string, bool> EntryShown;

        public bool ReducedMotion { get; set; }
        public bool PanelOpen => _panel != null && _panel.gameObject.activeSelf;

        LoreDatabase _db;
        RectTransform _canvasRect, _panel, _content, _tooltipRect;
        CanvasGroup _panelGroup;
        ScrollRect _scroll;
        TextMeshProUGUI _kicker, _title, _meta, _tooltip;
        Button _backBtn;
        TMP_InputField _search;
        readonly Dictionary<string, Image> _districtUnderlines = new Dictionary<string, Image>();

        struct Entry { public Action render; public string id; }
        readonly Stack<Entry> _history = new Stack<Entry>();
        Entry _current;
        bool _portrait;
        Coroutine _anim;
        bool _closing;

        public void Build(LoreDatabase db)
        {
            _db = db;
            _portrait = Screen.height > Screen.width;
            EnsureEventSystem();

            var canvasGo = new GameObject("Nevishk UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRect = (RectTransform)canvasGo.transform;

            BuildHeader();
            if (!_portrait) BuildLegend();
            BuildDistrictBar();
            BuildPanel();
            BuildTooltip();
            _panel.gameObject.SetActive(false);
        }

        // ================================================================ public API

        public void ShowDistrict(string id, bool pushHistory = true) =>
            Show(new Entry { id = id, render = () => RenderDistrict(id) }, pushHistory);

        public void ShowLandmark(string id, bool pushHistory = true) =>
            Show(new Entry { id = id, render = () => RenderLandmark(id) }, pushHistory);

        public void ShowDocument(string id, bool pushHistory = true) =>
            Show(new Entry { id = id, render = () => RenderDocument(id) }, pushHistory);

        public void Close()
        {
            if (!PanelOpen) return;
            _history.Clear(); _current = default;
            SetActiveDistrict(null);
            Animate(false);
            PanelClosed?.Invoke();
        }

        public void SetActiveDistrict(string id)
        {
            foreach (var kv in _districtUnderlines) kv.Value.enabled = kv.Key == id;
        }

        public void SetTooltip(string text, Vector2 screenPos)
        {
            if (string.IsNullOrEmpty(text)) { _tooltipRect.gameObject.SetActive(false); return; }
            _tooltipRect.gameObject.SetActive(true);
            _tooltip.text = text;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, null, out var local);
            _tooltipRect.anchoredPosition = local + new Vector2(18f, 18f);
        }

        // ================================================================ navigation

        void Show(Entry entry, bool pushHistory)
        {
            bool wasOpen = PanelOpen;
            if (pushHistory && wasOpen && _current.render != null) _history.Push(_current);
            if (!pushHistory) _history.Clear();
            _current = entry;
            Render(false);
            if (!wasOpen || _closing) Animate(true);
        }

        void Back()
        {
            if (_history.Count == 0) return;
            _current = _history.Pop();
            Render(true);
        }

        void Render(bool isBack)
        {
            _panel.gameObject.SetActive(true);
            ClearContent();
            _kicker.color = T.Accent;
            _current.render();
            _backBtn.gameObject.SetActive(_history.Count > 0);
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 1f;
            EntryShown?.Invoke(_current.id, isBack);
        }

        void Animate(bool open)
        {
            if (_anim != null) StopCoroutine(_anim);
            _closing = !open;
            _anim = StartCoroutine(AnimatePanel(open));
        }

        // Enter 220 ms ease-out, exit 150 ms (exit faster than enter). Instant under reduced motion.
        IEnumerator AnimatePanel(bool open)
        {
            _panel.gameObject.SetActive(true);
            Vector2 home = _portrait ? new Vector2(0, 0) : new Vector2(0, T.BarHeight / 2f);
            Vector2 away = home + (_portrait ? new Vector2(0, -120) : new Vector2(80, 0));
            float dur = ReducedMotion ? 0f : open ? 0.22f : 0.15f;
            for (float t = 0; t < dur; t += Time.unscaledDeltaTime)
            {
                float k = t / dur;
                float e = open ? 1f - Mathf.Pow(1f - k, 3f) : k * k;
                _panelGroup.alpha = open ? e : 1f - e;
                _panel.anchoredPosition = open ? Vector2.Lerp(away, home, e) : Vector2.Lerp(home, away, e);
                yield return null;
            }
            _panelGroup.alpha = open ? 1f : 0f;
            _panel.anchoredPosition = home;
            if (!open) _panel.gameObject.SetActive(false);
            _anim = null; _closing = false;
        }

        // ================================================================ entry rendering

        void RenderDistrict(string id)
        {
            var d = _db.District(id);
            if (d == null) return;
            SetActiveDistrict(id);
            _kicker.text = $"РАЙОН {d.number}";
            _title.text = d.name;
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(d.summary)) meta.Add($"<i>{d.summary}</i>");
            if (!string.IsNullOrEmpty(d.owner)) meta.Add("ведёт " + d.owner);
            if (!string.IsNullOrEmpty(d.status)) meta.Add(StatusRu(d.status));
            _meta.text = string.Join("   ·   ", meta);

            if (!string.IsNullOrEmpty(d.text)) AddBody(d.text);
            else AddNote("Лор района ещё пишется.");

            AddDocRows(d.docs);

            var places = new List<LandmarkInfo>(_db.LandmarksIn(id));
            if (places.Count > 0)
            {
                // Entries with lore first, then alphabetical.
                places.Sort((a, b) =>
                {
                    int la = string.IsNullOrEmpty(a.text) ? 1 : 0, lb = string.IsNullOrEmpty(b.text) ? 1 : 0;
                    return la != lb ? la.CompareTo(lb) : string.Compare(a.name, b.name, StringComparison.CurrentCulture);
                });
                AddSection($"Места · {places.Count}");
                foreach (var l in places)
                {
                    var cat = _db.Category(l.category);
                    var lid = l.id;
                    AddRow(l.name, (cat != null ? cat.name : "") + (string.IsNullOrEmpty(l.text) ? "" : "  ·  есть лор"),
                           () => LandmarkClicked?.Invoke(lid), NevishkColors.ForCategory(_db, l.category), !string.IsNullOrEmpty(l.text));
                }
            }
        }

        void RenderLandmark(string id)
        {
            var l = _db.Landmark(id);
            if (l == null) return;
            var cat = _db.Category(l.category);
            var dist = _db.District(l.district);
            SetActiveDistrict(dist?.id);
            _kicker.text = (cat != null ? cat.name : "").ToUpperInvariant();
            _title.text = l.name;
            _meta.text = dist != null
                ? $"Район {dist.number} · <link=\"d:{dist.id}\"><color={T.ToHex(T.Accent)}><u>{dist.name}</u></color></link>"
                : "";

            if (!string.IsNullOrEmpty(l.text)) AddBody(l.text);
            else AddNote("Описания пока нет.");

            AddDocRows(l.docs);
            if (dist != null)
            {
                AddSection("Район");
                var did = dist.id;
                AddRow(dist.name, $"Район {dist.number}", () => DistrictClicked?.Invoke(did), JsonColor(dist.color), false);
            }
        }

        void RenderDocument(string id)
        {
            var doc = _db.Document(id);
            if (doc == null) return;
            _kicker.text = KindRu(doc.kind);
            _kicker.color = doc.kind == "archive" ? T.Stamp : T.Accent;
            _title.text = doc.title;
            _meta.text = string.IsNullOrEmpty(doc.author) ? "" : "Автор: " + doc.author;
            AddBody(doc.text);
            AddEndMark();
        }

        void RenderArchiveIndex()
        {
            _kicker.text = "ВСЕ МАТЕРИАЛЫ";
            _title.text = "Архив Невишка";
            _meta.text = $"{_db.documents.Length} документов";
            foreach (var doc in _db.documents)
            {
                var did = doc.id;
                AddRow(doc.title, KindRu(doc.kind) + (string.IsNullOrEmpty(doc.author) ? "" : "  ·  " + doc.author),
                       () => ShowDocument(did), doc.kind == "archive" ? T.Stamp : T.Accent, true);
            }
        }

        void RenderSearch(string q)
        {
            _kicker.text = "ПОИСК";
            _title.text = $"«{q}»";
            int hits = 0;
            var cmp = StringComparison.CurrentCultureIgnoreCase;

            var names = new List<Action>();
            foreach (var d in _db.districts)
                if (d.name.IndexOf(q, cmp) >= 0 || (d.text ?? "").IndexOf(q, cmp) >= 0)
                {
                    var id = d.id; var nm = d.name; var num = d.number; var col = JsonColor(d.color);
                    names.Add(() => AddRow(nm, $"Район {num}", () => DistrictClicked?.Invoke(id), col, true));
                }
            foreach (var l in _db.landmarks)
                if (l.name.IndexOf(q, cmp) >= 0 || (l.text ?? "").IndexOf(q, cmp) >= 0)
                {
                    var id = l.id; var nm = l.name; var sub = _db.District(l.district)?.name ?? ""; var col = NevishkColors.ForCategory(_db, l.category);
                    var lore = !string.IsNullOrEmpty(l.text);
                    names.Add(() => AddRow(nm, sub, () => LandmarkClicked?.Invoke(id), col, lore));
                }
            if (names.Count > 0) { AddSection("Места и районы"); foreach (var a in names) a(); hits += names.Count; }

            var docs = new List<DocumentInfo>();
            foreach (var doc in _db.documents)
                if (doc.title.IndexOf(q, cmp) >= 0 || doc.text.IndexOf(q, cmp) >= 0) docs.Add(doc);
            if (docs.Count > 0)
            {
                AddSection("Архив");
                foreach (var doc in docs)
                {
                    var did = doc.id;
                    AddRow(doc.title, $"{Count(doc.text, q)} совп.", () => ShowDocument(did), T.Stamp, true);
                }
                hits += docs.Count;
            }
            _meta.text = hits == 0 ? "Ничего не найдено." : $"{hits} результатов";
            if (hits == 0) AddNote("Попробуйте другое слово: название района, места или имя.");
        }

        static int Count(string text, string q)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(q, i, StringComparison.CurrentCultureIgnoreCase)) >= 0) { n++; i += q.Length; }
            return n;
        }

        void AddDocRows(string[] docs)
        {
            if (docs == null || docs.Length == 0) return;
            AddSection("Из архива");
            foreach (var docId in docs)
            {
                var doc = _db.Document(docId);
                if (doc == null) continue;
                var did = docId;
                AddRow(doc.title, KindRu(doc.kind), () => ShowDocument(did), doc.kind == "archive" ? T.Stamp : T.Accent, true);
            }
        }

        static string StatusRu(string s) => s switch
        {
            "Complete" => "готов", "In Progress" => "в работе", "Not Started" => "не начат", _ => s
        };

        static string KindRu(string k) => k switch
        {
            "archive" => "АРХИВ · ДЕЛО", "history" => "ИСТОРИЯ", "factions" => "ФРАКЦИИ", "places" => "МЕСТА",
            "notes" => "ЗАМЕТКИ", "glossary" => "ТЕРМИНЫ", _ => "ДОКУМЕНТ"
        };

        static Color JsonColor(string hex) => LoreDatabase.ParseColor(hex, Color.gray);

        // ================================================================ content builders

        void ClearContent()
        {
            for (int i = _content.childCount - 1; i >= 0; i--) Destroy(_content.GetChild(i).gameObject);
        }

        void AddBody(string text)
        {
            // A single TMP mesh tops out around ~16k glyphs, so long documents are split by paragraph.
            const int chunk = 2500;
            var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            foreach (var p in paragraphs)
            {
                if (sb.Length > 0 && sb.Length + p.Length > chunk) { AddReading(sb.ToString()); sb.Clear(); }
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(p);
            }
            if (sb.Length > 0) AddReading(sb.ToString());
        }

        void AddReading(string text)
        {
            var t = AddText(text, NevishkFonts.Reading, T.SizeBody, T.Text);
            t.lineSpacing = T.BodyLineSpacing;
            t.paragraphSpacing = T.BodyParagraphSpacing;
        }

        void AddNote(string text)
        {
            var t = AddText($"<i>{text}</i>", NevishkFonts.Reading, T.SizeBody - 2, T.TextMuted);
            t.margin = new Vector4(0, T.S1, 0, T.S1);
        }

        void AddEndMark()
        {
            var t = AddText("*   *   *", NevishkFonts.Reading, T.SizeBody, T.Faint);
            t.alignment = TextAlignmentOptions.Center;
            t.margin = new Vector4(0, T.S3, 0, 0);
        }

        void AddSection(string title)
        {
            var go = new GameObject("Section", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(_content, false);
            go.GetComponent<LayoutElement>().minHeight = T.S6;
            var rule = NewRect("Rule", go.transform);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1); rule.pivot = new Vector2(0.5f, 1);
            rule.sizeDelta = new Vector2(0, 1); rule.anchoredPosition = new Vector2(0, -T.S2);
            rule.gameObject.AddComponent<Image>().color = T.Border;
            var t = NewText("Title", go.transform, title.ToUpperInvariant(), NevishkFonts.UIBold, T.SizeKicker, T.TextMuted);
            t.characterSpacing = T.KickerTracking;
            var r = t.rectTransform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = new Vector2(0, -T.S2 - 4);
            t.alignment = TextAlignmentOptions.BottomLeft;
        }

        TextMeshProUGUI AddText(string text, TMP_FontAsset font, float size, Color color)
        {
            var t = NewText("Text", _content, text, font, size, color);
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        void AddRow(string label, string sub, Action onClick, Color swatch, bool emphasise)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_content, false);
            go.GetComponent<LayoutElement>().minHeight = T.RowHeight;
            StyleButton(go.GetComponent<Button>(), go.GetComponent<Image>());
            go.GetComponent<Button>().onClick.AddListener(() => onClick());

            var bar = NewRect("Swatch", go.transform);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(0, 1); bar.pivot = new Vector2(0, 0.5f);
            bar.sizeDelta = new Vector2(4, 0);
            var barImg = bar.gameObject.AddComponent<Image>(); barImg.color = swatch; barImg.raycastTarget = false;

            var t = NewText("Label", go.transform, label, NevishkFonts.UI, T.SizeUI + 1, emphasise ? T.Text : T.TextMuted);
            var r = t.rectTransform; r.anchorMin = new Vector2(0, 0.5f); r.anchorMax = new Vector2(1, 1);
            r.offsetMin = new Vector2(T.S2 + 4, 0); r.offsetMax = new Vector2(-T.S6, -8);
            t.alignment = TextAlignmentOptions.BottomLeft; t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;

            var s = NewText("Sub", go.transform, sub, NevishkFonts.UI, T.SizeRowSub, T.TextMuted);
            var sr = s.rectTransform; sr.anchorMin = new Vector2(0, 0); sr.anchorMax = new Vector2(1, 0.5f);
            sr.offsetMin = new Vector2(T.S2 + 4, 8); sr.offsetMax = new Vector2(-T.S6, -2);
            s.alignment = TextAlignmentOptions.TopLeft; s.textWrappingMode = TextWrappingModes.NoWrap;
            s.overflowMode = TextOverflowModes.Ellipsis;

            var chev = NewText("Chevron", go.transform, "›", NevishkFonts.UI, 28, T.Faint);
            var cr = chev.rectTransform; cr.anchorMin = new Vector2(1, 0); cr.anchorMax = Vector2.one; cr.pivot = new Vector2(1, 0.5f);
            cr.sizeDelta = new Vector2(T.S4, 0); cr.anchoredPosition = new Vector2(-T.S2, 0);
            chev.alignment = TextAlignmentOptions.Center;
        }

        // ================================================================ static layout

        void BuildHeader()
        {
            var header = NewRect("Header", _canvasRect);
            header.anchorMin = header.anchorMax = header.pivot = new Vector2(0, 1);
            header.anchoredPosition = new Vector2(T.S3, -T.S3);
            header.sizeDelta = new Vector2(460, 172);
            var bg = header.gameObject.AddComponent<Image>(); bg.color = T.Scrim;

            var title = NewText("City", header, _db.city.name.ToUpperInvariant(), NevishkFonts.Display, 46, T.Text);
            title.characterSpacing = 18;
            Pin(title.rectTransform, T.S3, -T.S2, 56);

            var tag = NewText("Tagline", header, $"<i>{_db.city.tagline}</i>", NevishkFonts.Reading, 16, T.TextMuted);
            Pin(tag.rectTransform, T.S3, -T.S2 - 56, 24);

            // Search field + two actions on one row, all 44 px tall.
            float y = -T.S2 - 56 - 24 - T.S2;
            _search = BuildSearchField(header);
            var sr = (RectTransform)_search.transform;
            sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(0, 1);
            sr.anchoredPosition = new Vector2(T.S3, y); sr.sizeDelta = new Vector2(220, T.TouchTarget);

            var reset = SmallButton(header, "Обзор", () => ResetClicked?.Invoke());
            reset.anchorMin = reset.anchorMax = reset.pivot = new Vector2(0, 1);
            reset.anchoredPosition = new Vector2(T.S3 + 220 + T.S1, y);

            var arch = SmallButton(header, "Архив", () => Show(new Entry { id = null, render = RenderArchiveIndex }, false));
            arch.anchorMin = arch.anchorMax = arch.pivot = new Vector2(0, 1);
            arch.anchoredPosition = new Vector2(T.S3 + 220 + T.S1 + reset.sizeDelta.x + T.S1, y);

            // Controls hint (touch users get the gestures; the rest is in the README).
            float hy = y - T.TouchTarget - T.S1;
            string hint = Application.isMobilePlatform
                ? "Палец — двигать · два пальца — масштаб и поворот"
                : "Тянуть — двигать · колесо — масштаб · ПКМ — поворот · WASD";
            var hintText = NewText("Hint", header, hint, NevishkFonts.UI, 13, T.TextMuted);
            Pin(hintText.rectTransform, T.S3, hy, 20, T.S3);

            header.sizeDelta = new Vector2(Mathf.Max(460, arch.anchoredPosition.x + arch.sizeDelta.x + T.S3), -hy + 20 + T.S2);
        }

        TMP_InputField BuildSearchField(Transform parent)
        {
            var go = new GameObject("Search", typeof(RectTransform), typeof(Image));
            go.SetActive(false);   // TMP_InputField must have its parts assigned before OnEnable
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = T.Raised;

            var area = NewRect("Text Area", go.transform);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(T.S2, 4); area.offsetMax = new Vector2(-T.S2, -4);
            area.gameObject.AddComponent<RectMask2D>();

            var ph = NewText("Placeholder", area, "Поиск по лору…", NevishkFonts.UI, T.SizeUI, T.TextMuted);
            Fill(ph.rectTransform); ph.alignment = TextAlignmentOptions.MidlineLeft; ph.fontStyle = FontStyles.Italic;
            var txt = NewText("Text", area, "", NevishkFonts.UI, T.SizeUI, T.Text);
            Fill(txt.rectTransform); txt.alignment = TextAlignmentOptions.MidlineLeft;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = area; input.textComponent = txt; input.placeholder = ph;
            input.fontAsset = NevishkFonts.UI; input.pointSize = T.SizeUI;
            input.caretColor = T.Accent; input.customCaretColor = true;
            input.selectionColor = new Color(T.Accent.r, T.Accent.g, T.Accent.b, 0.35f);
            input.lineType = TMP_InputField.LineType.SingleLine;
            StyleSelectable(input, go.GetComponent<Image>());

            go.SetActive(true);
            input.onValueChanged.AddListener(q =>
            {
                q = q.Trim();
                if (q.Length >= 2) Show(new Entry { id = null, render = () => RenderSearch(q) }, false);
                else if (PanelOpen && _current.id == null && _title.text.StartsWith("«")) Close();
            });
            return input;
        }

        void BuildLegend()
        {
            var legend = NewRect("Legend", _canvasRect);
            legend.anchorMin = legend.anchorMax = legend.pivot = new Vector2(0, 0);
            legend.anchoredPosition = new Vector2(T.S3, T.BarHeight + T.S2);
            legend.gameObject.AddComponent<Image>().color = T.Scrim;
            var v = legend.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(16, 16, 12, 12); v.spacing = 4; v.childControlHeight = v.childControlWidth = true;
            v.childForceExpandHeight = false;
            var fit = legend.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var head = NewText("Title", legend, "ЛЕГЕНДА", NevishkFonts.UIBold, 12, T.TextMuted);
            head.characterSpacing = T.KickerTracking;
            foreach (var c in _db.categories)
            {
                var col = NevishkColors.ForCategory(_db, c.id);
                NewText(c.name, legend, $"<size=150%><color={T.ToHex(col)}>•</color></size>  {c.name}", NevishkFonts.UI, 15, T.Text);
            }
            NewText("Lore", legend, $"<b><color={T.ToHex(T.Accent)}>|</color></b>   жёлтый шест — есть лор", NevishkFonts.UI, 15, T.TextMuted);
        }

        void BuildDistrictBar()
        {
            var bar = NewRect("District Bar", _canvasRect);
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0); bar.pivot = new Vector2(0.5f, 0);
            bar.sizeDelta = new Vector2(0, T.BarHeight); bar.anchoredPosition = Vector2.zero;
            bar.gameObject.AddComponent<Image>().color = T.Scrim;

            var scroll = bar.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = NewRect("Viewport", bar);
            Fill(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var row = NewRect("Row", viewport);
            row.anchorMin = new Vector2(0, 0); row.anchorMax = new Vector2(0, 1); row.pivot = new Vector2(0, 0.5f);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset((int)T.S3, (int)T.S3, 10, 10); h.spacing = T.S1;
            h.childForceExpandWidth = false; h.childControlWidth = true; h.childControlHeight = true;
            row.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = row;

            var list = new List<DistrictInfo>(_db.districts);
            list.Sort((a, b) => a.number.CompareTo(b.number));
            foreach (var d in list)
            {
                var go = new GameObject(d.name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                go.transform.SetParent(row, false);
                StyleButton(go.GetComponent<Button>(), go.GetComponent<Image>());
                var t = NewText("Label", go.transform,
                    $"<b><color={T.ToHex(JsonColor(d.color))}>{d.number}</color></b>   {d.name}",
                    NevishkFonts.UI, T.SizeUI, T.Text);
                Fill(t.rectTransform, T.S2, 0); t.alignment = TextAlignmentOptions.Center;
                go.GetComponent<LayoutElement>().preferredWidth = t.GetPreferredValues().x + T.S4 + 8;
                go.GetComponent<LayoutElement>().minHeight = T.TouchTarget;

                var line = NewRect("Active", go.transform);
                line.anchorMin = new Vector2(0, 0); line.anchorMax = new Vector2(1, 0); line.pivot = new Vector2(0.5f, 0);
                line.sizeDelta = new Vector2(0, 3);
                var li = line.gameObject.AddComponent<Image>(); li.color = T.Accent; li.raycastTarget = false; li.enabled = false;
                _districtUnderlines[d.id] = li;

                var id = d.id;
                go.GetComponent<Button>().onClick.AddListener(() => DistrictClicked?.Invoke(id));
            }
        }

        void BuildPanel()
        {
            _panel = NewRect("Lore Panel", _canvasRect);
            if (_portrait)
            {
                _panel.anchorMin = new Vector2(0, 0); _panel.anchorMax = new Vector2(1, 0.64f);
                _panel.offsetMin = new Vector2(0, T.BarHeight); _panel.offsetMax = Vector2.zero;
            }
            else
            {
                _panel.anchorMin = new Vector2(1, 0); _panel.anchorMax = new Vector2(1, 1); _panel.pivot = new Vector2(1, 0.5f);
                _panel.sizeDelta = new Vector2(T.PanelWidth, -T.BarHeight); _panel.anchoredPosition = new Vector2(0, T.BarHeight / 2f);
            }
            _panel.gameObject.AddComponent<Image>().color = T.Surface;
            _panelGroup = _panel.gameObject.AddComponent<CanvasGroup>();

            // Left edge rule, like a folder spine.
            var spine = NewRect("Spine", _panel);
            spine.anchorMin = new Vector2(0, 0); spine.anchorMax = new Vector2(0, 1); spine.pivot = new Vector2(0, 0.5f);
            spine.sizeDelta = new Vector2(1, 0);
            spine.gameObject.AddComponent<Image>().color = T.Border;

            float g = _portrait ? T.S3 : T.PanelGutter;

            var close = SmallButton(_panel, "×", Close);
            close.anchorMin = close.anchorMax = close.pivot = new Vector2(1, 1);
            close.anchoredPosition = new Vector2(-T.S2, -T.S2); close.sizeDelta = new Vector2(T.TouchTarget, T.TouchTarget);
            close.GetComponentInChildren<TextMeshProUGUI>().fontSize = 28;

            var back = SmallButton(_panel, "‹  Назад", Back);
            back.anchorMin = back.anchorMax = back.pivot = new Vector2(0, 1);
            back.anchoredPosition = new Vector2(g - 12, -T.S2);
            _backBtn = back.GetComponent<Button>();

            float top = -T.S2 - T.TouchTarget - T.S2;
            _kicker = NewText("Kicker", _panel, "", NevishkFonts.UIBold, T.SizeKicker, T.Accent);
            Pin(_kicker.rectTransform, g, top, 20, g); _kicker.characterSpacing = T.KickerTracking;

            _title = NewText("Title", _panel, "", NevishkFonts.Display, T.SizeTitle, T.Text);
            Pin(_title.rectTransform, g, top - 24, 104, g);
            _title.enableAutoSizing = true; _title.fontSizeMin = T.SizeTitleMin; _title.fontSizeMax = T.SizeTitle;
            _title.alignment = TextAlignmentOptions.TopLeft; _title.lineSpacing = -8;
            _title.textWrappingMode = TextWrappingModes.Normal;

            _meta = NewText("Meta", _panel, "", NevishkFonts.UI, T.SizeMeta, T.TextMuted);
            Pin(_meta.rectTransform, g, top - 24 - 104 - 4, 24, g);
            _meta.raycastTarget = true;
            _meta.gameObject.AddComponent<TmpLinkHandler>().onLink = link =>
            {
                if (link.StartsWith("d:")) DistrictClicked?.Invoke(link.Substring(2));
            };

            float headerH = -(top - 24 - 104 - 4 - 24) + T.S2;
            var rule = NewRect("Header Rule", _panel);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1); rule.pivot = new Vector2(0.5f, 1);
            rule.offsetMin = new Vector2(g, -headerH - 1); rule.offsetMax = new Vector2(-g, -headerH);
            rule.gameObject.AddComponent<Image>().color = T.Border;

            // Scroll body
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(_panel, false);
            var sr = (RectTransform)scrollGo.transform;
            sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one;
            sr.offsetMin = new Vector2(g, 0); sr.offsetMax = new Vector2(-g + 20, -headerH - 1);
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false; _scroll.scrollSensitivity = 40f; _scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = NewRect("Viewport", sr);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(-20, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f); // catches scroll input

            _content = NewRect("Content", viewport);
            _content.anchorMin = new Vector2(0, 1); _content.anchorMax = new Vector2(1, 1); _content.pivot = new Vector2(0.5f, 1);
            _content.sizeDelta = Vector2.zero;
            var v = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = T.S1; v.padding = new RectOffset(0, 0, (int)T.S3, (int)T.S6 + 16);
            v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.viewport = viewport; _scroll.content = _content;

            // Thin scrollbar in the gutter
            var sbGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            sbGo.transform.SetParent(sr, false);
            var sbr = (RectTransform)sbGo.transform;
            sbr.anchorMin = new Vector2(1, 0); sbr.anchorMax = Vector2.one; sbr.pivot = new Vector2(1, 0.5f);
            sbr.sizeDelta = new Vector2(6, -T.S2 * 2); sbr.anchoredPosition = Vector2.zero;
            sbGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.04f);
            var handle = NewRect("Handle", sbr); Fill(handle);
            var hImg = handle.gameObject.AddComponent<Image>(); hImg.color = T.Faint;
            var sb = sbGo.GetComponent<Scrollbar>(); sb.handleRect = handle; sb.targetGraphic = hImg; sb.direction = Scrollbar.Direction.BottomToTop;
            _scroll.verticalScrollbar = sb;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        void BuildTooltip()
        {
            _tooltipRect = NewRect("Tooltip", _canvasRect);
            _tooltipRect.anchorMin = _tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
            _tooltipRect.pivot = new Vector2(0, 0);
            var img = _tooltipRect.gameObject.AddComponent<Image>(); img.color = T.SurfaceSolid; img.raycastTarget = false;
            var outline = _tooltipRect.gameObject.AddComponent<Outline>(); outline.effectColor = T.Border; outline.effectDistance = new Vector2(1, -1);
            var fit = _tooltipRect.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var lay = _tooltipRect.gameObject.AddComponent<HorizontalLayoutGroup>();
            lay.padding = new RectOffset(12, 12, 8, 8); lay.childControlWidth = lay.childControlHeight = true;
            _tooltip = NewText("Text", _tooltipRect, "", NevishkFonts.UI, T.SizeMeta, T.Text);
            _tooltip.textWrappingMode = TextWrappingModes.NoWrap;
            _tooltipRect.gameObject.SetActive(false);
        }

        // ================================================================ helpers

        /// Top-anchored strip at a given y, stretching between left and right insets.
        static void Pin(RectTransform r, float left, float y, float height, float right = 24f)
        {
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0, 1);
            r.offsetMin = new Vector2(left, y - height);
            r.offsetMax = new Vector2(-right, y);
        }

        static void Fill(RectTransform r, float padX = 0, float padY = 0)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(padX, padY); r.offsetMax = new Vector2(-padX, -padY);
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static TextMeshProUGUI NewText(string name, Transform parent, string text, TMP_FontAsset font, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = font; t.fontSize = size; t.color = color; t.text = text;
            t.raycastTarget = false; t.richText = true;
            return t;
        }

        static void StyleSelectable(Selectable s, Image img)
        {
            img.color = Color.white;   // ColorBlock supplies the actual colours
            s.targetGraphic = img;     // not auto-assigned for components added at runtime
            var c = s.colors;
            c.normalColor = T.Raised;
            c.highlightedColor = T.RaisedHover;
            c.selectedColor = T.RaisedHover;                     // keyboard focus stays visible
            c.pressedColor = Color.Lerp(T.RaisedHover, T.Accent, 0.25f);
            c.disabledColor = new Color(T.Raised.r, T.Raised.g, T.Raised.b, 0.5f);
            c.colorMultiplier = 1f;
            c.fadeDuration = T.HoverFade;
            s.colors = c;
        }

        static void StyleButton(Button b, Image img) => StyleSelectable(b, img);

        RectTransform SmallButton(Transform parent, string label, Action onClick)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            StyleButton(go.GetComponent<Button>(), go.GetComponent<Image>());
            go.GetComponent<Button>().onClick.AddListener(() => onClick());
            var r = (RectTransform)go.transform;
            var t = NewText("Label", go.transform, label, NevishkFonts.UI, T.SizeUI, T.Text);
            Fill(t.rectTransform);
            t.alignment = TextAlignmentOptions.Center;
            r.sizeDelta = new Vector2(Mathf.Max(T.TouchTarget, t.GetPreferredValues().x + T.S4), T.TouchTarget);
            return r;
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }
    }

    /// <summary>Makes &lt;link&gt; tags inside a TextMeshProUGUI clickable.</summary>
    public class TmpLinkHandler : MonoBehaviour, IPointerClickHandler
    {
        public Action<string> onLink;
        TextMeshProUGUI _text;
        void Awake() => _text = GetComponent<TextMeshProUGUI>();

        public void OnPointerClick(PointerEventData e)
        {
            int i = TMP_TextUtilities.FindIntersectingLink(_text, e.position, null);
            if (i >= 0) onLink?.Invoke(_text.textInfo.linkInfo[i].GetLinkID());
        }
    }

    public static class NevishkColors
    {
        /// Category colours from the board legend, adjusted so every one reads on the dark UI and the map.
        public static Color ForCategory(LoreDatabase db, string category)
        {
            switch (category)
            {
                case "production": return new Color(0.90f, 0.45f, 0.56f);      // rose (board: pink)
                case "infrastructure": return new Color(0.42f, 0.75f, 0.48f);  // green
                case "culture": return new Color(0.86f, 0.84f, 0.80f);         // board black → bone, else invisible on dark
                case "education": return new Color(0.62f, 0.50f, 0.86f);       // violet
                case "gathering": return new Color(0.58f, 0.62f, 0.68f);       // slate (board: grey)
            }
            var c = db.Category(category);
            return c != null ? LoreDatabase.ParseColor(c.color, Color.gray) : Color.gray;
        }
    }
}
