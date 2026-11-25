using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using T = Nevishk.NevishkTheme;

namespace Nevishk
{
    /// <summary>
    /// Story-mode UI: the toolbar (tools, colours, file actions), the card for naming a figure / object / flag,
    /// and a short notice line. All behaviour lives in StoryMode; this only raises events and shows state.
    /// </summary>
    public partial class LorePanelUI
    {
        public event Action<bool> StoryToggled;
        public event Action<string> StoryToolChosen;
        public event Action<Color> StoryColourChosen;
        public event Action StorySave, StoryOpen, StoryShare, StoryClear;
        public event Action<string> CardNameChanged, CardNoteChanged;
        public event Action CardDelete, CardDone;
        public event Action<string> CardShapeChosen;
        public event Action CardPortraitPick, CardPortraitClear;
        public event Action StepPrev, StepNext, StepAdd, StepDelete, StepPlay;
        public event Action<string> StepCaptionChanged;

        public static readonly (string id, string label)[] StoryTools =
        {
            ("select", "Выбор"), ("figure", "Персонаж"), ("object", "Объект"), ("flag", "Флажок"), ("thread", "Нить"), ("delete", "Удалить")
        };
        public static readonly Color[] StoryColours =
        {
            new Color(0.80f, 0.20f, 0.17f),   // red string
            new Color(0.91f, 0.77f, 0.28f),   // yellow
            new Color(0.25f, 0.50f, 0.85f),   // blue
            new Color(0.30f, 0.68f, 0.33f),   // green
            new Color(0.56f, 0.36f, 0.82f),   // violet
            new Color(0.93f, 0.91f, 0.85f),   // bone white
            new Color(0.13f, 0.13f, 0.13f),   // black
        };

        RectTransform _header, _storyBar, _card, _notice;
        Image _storyButton;
        bool _storyOn;
        readonly Dictionary<string, Button> _toolButtons = new Dictionary<string, Button>();
        readonly List<Image> _swatchRings = new List<Image>();
        TextMeshProUGUI _storyHint, _cardKicker, _noticeText, _clearLabel;
        TMP_InputField _cardName, _cardNote;
        Button _noticeAction;
        LayoutElement _noticeActionLayout;
        Action _noticeCallback;
        Coroutine _noticeHide, _clearReset;
        bool _cardMuted;   // suppresses change events while the card is being filled from code

        public bool StoryActive => _storyOn;
        public bool CardFocused => (_cardName != null && _cardName.isFocused) || (_cardNote != null && _cardNote.isFocused);

        const float Row = 34f, Gap = 6f, Swatch = 28f;

        /// The story toolbar sits in the left column right under the header, in compact rows:
        /// tools · colours · file actions · a one-line hint.
        void BuildStoryUI()
        {
            _storyBar = NewRect("Story Toolbar", _canvasRect);
            _storyBar.anchorMin = _storyBar.anchorMax = _storyBar.pivot = new Vector2(0, 1);
            _storyBar.gameObject.AddComponent<Image>().color = T.Scrim;

            float pad = T.S2, x = pad, y = -pad, width = 0f;
            foreach (var (id, label) in StoryTools)
            {
                var b = CompactButton(_storyBar, label, () => StoryToolChosen?.Invoke(id));
                b.anchorMin = b.anchorMax = b.pivot = new Vector2(0, 1);
                b.anchoredPosition = new Vector2(x, y);
                x += b.sizeDelta.x + Gap;
                _toolButtons[id] = b.GetComponent<Button>();
            }
            width = Mathf.Max(width, x - Gap + pad);

            // Colours.
            y -= Row + Gap; x = pad;
            for (int i = 0; i < StoryColours.Length; i++)
            {
                var c = StoryColours[i];
                var sw = new GameObject("Colour", typeof(RectTransform), typeof(Image), typeof(Button));
                sw.transform.SetParent(_storyBar, false);
                var r = (RectTransform)sw.transform;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
                r.anchoredPosition = new Vector2(x, y - (Row - Swatch) / 2f); r.sizeDelta = new Vector2(Swatch, Swatch);
                var ring = sw.GetComponent<Image>(); ring.color = new Color(1, 1, 1, 0f);
                var btn = sw.GetComponent<Button>(); btn.targetGraphic = ring; btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => StoryColourChosen?.Invoke(c));
                var chip = NewRect("Chip", r); Fill(chip, 4, 4);
                var ci = chip.gameObject.AddComponent<Image>(); ci.color = c; ci.raycastTarget = false;
                _swatchRings.Add(ring);
                x += Swatch + 4f;
            }
            width = Mathf.Max(width, x - 4f + pad);

            // File actions.
            y -= Row + Gap; x = pad;
            foreach (var (label, act) in new (string, Action)[]
                     { ("Сохранить", () => StorySave?.Invoke()), ("Открыть", () => StoryOpen?.Invoke()), ("Ссылка", () => StoryShare?.Invoke()), ("Очистить", null) })
            {
                RectTransform b;
                if (act == null)
                {
                    // Two-step clear: first press asks, second within 4 s clears.
                    b = CompactButton(_storyBar, label, ClearPressed);
                    _clearLabel = b.GetComponentInChildren<TextMeshProUGUI>();
                }
                else b = CompactButton(_storyBar, label, act);
                b.anchorMin = b.anchorMax = b.pivot = new Vector2(0, 1);
                b.anchoredPosition = new Vector2(x, y);
                x += b.sizeDelta.x + Gap;
            }
            width = Mathf.Max(width, x - Gap + pad);

            // Hint: up to two lines.
            y -= Row + Gap;
            _storyHint = NewText("Hint", _storyBar, "", NevishkFonts.UI, 13, T.TextMuted);
            _storyHint.textWrappingMode = TextWrappingModes.Normal;
            _storyHint.alignment = TextAlignmentOptions.TopLeft;
            Pin(_storyHint.rectTransform, pad, y, 34, pad);

            _storyBar.sizeDelta = new Vector2(width, -(y - 34) + pad - 4);
            _storyBar.gameObject.SetActive(false);

            BuildCard();
            BuildNotice();
            BuildStepsBar();
        }

        RectTransform _stepsBar, _caption;
        TextMeshProUGUI _stepCounter, _playLabel, _captionText;
        TMP_InputField _stepText;
        Button _stepPrevBtn, _stepNextBtn, _stepDelBtn;

        /// Bottom centre, above the districts tab: ‹ Шаг 2 / 5 › · caption · + Шаг · – Шаг · Играть
        void BuildStepsBar()
        {
            _stepsBar = NewRect("Story Steps", _canvasRect);
            _stepsBar.anchorMin = _stepsBar.anchorMax = _stepsBar.pivot = new Vector2(0.5f, 0f);
            _stepsBar.anchoredPosition = new Vector2(0, 44f + T.S2);
            _stepsBar.gameObject.AddComponent<Image>().color = T.Scrim;
            float pad = T.S1, x = pad, y = -pad;
            RectTransform Btn(string label, Action a)
            {
                var b = CompactButton(_stepsBar, label, a);
                b.anchorMin = b.anchorMax = b.pivot = new Vector2(0, 1);
                b.anchoredPosition = new Vector2(x, y);
                x += b.sizeDelta.x + 6f;
                return b;
            }
            _stepPrevBtn = Btn("‹", () => StepPrev?.Invoke()).GetComponent<Button>();
            _stepCounter = NewText("Counter", _stepsBar, "", NevishkFonts.UIBold, 15, T.Text);
            var cr = _stepCounter.rectTransform; cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0, 1);
            cr.anchoredPosition = new Vector2(x, y); cr.sizeDelta = new Vector2(96, 34);
            _stepCounter.alignment = TextAlignmentOptions.Center;
            x += 96 + 6f;
            _stepNextBtn = Btn("›", () => StepNext?.Invoke()).GetComponent<Button>();

            _stepText = MakeInput(_stepsBar, "Что происходит на этом шаге…", false);
            _stepText.characterLimit = 200;
            var tr = (RectTransform)_stepText.transform;
            tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0, 1);
            tr.anchoredPosition = new Vector2(x, y); tr.sizeDelta = new Vector2(380, 34);
            _stepText.onValueChanged.AddListener(v => { if (!_cardMuted) StepCaptionChanged?.Invoke(v); });
            x += 380 + 6f;

            Btn("+ Шаг", () => StepAdd?.Invoke());
            _stepDelBtn = Btn("– Шаг", () => StepDelete?.Invoke()).GetComponent<Button>();
            _playLabel = Btn("Играть", () => StepPlay?.Invoke()).GetComponentInChildren<TextMeshProUGUI>();
            _stepsBar.sizeDelta = new Vector2(x - 6f + pad, 34f + pad * 2f);
            _stepsBar.gameObject.SetActive(false);

            // Big caption shown while the story plays.
            _caption = NewRect("Story Caption", _canvasRect);
            _caption.anchorMin = _caption.anchorMax = _caption.pivot = new Vector2(0.5f, 0f);
            _caption.anchoredPosition = new Vector2(0, 44f + T.S2 + 50f + T.S2);
            _caption.gameObject.AddComponent<Image>().color = T.Surface;
            var lay = _caption.gameObject.AddComponent<HorizontalLayoutGroup>();
            lay.padding = new RectOffset(24, 24, 14, 14); lay.childControlWidth = lay.childControlHeight = true;
            var fit = _caption.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _captionText = NewText("Text", _caption, "", NevishkFonts.Reading, 24, T.Text);
            _captionText.textWrappingMode = TextWrappingModes.Normal;
            var le = _captionText.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 760f;
            _caption.gameObject.SetActive(false);
        }

        public void SetSteps(int index, int count, string caption, bool playing)
        {
            if (_stepsBar == null) return;
            _stepsBar.gameObject.SetActive(_storyOn);
            _stepCounter.text = $"Шаг {index + 1} / {count}";
            _stepPrevBtn.interactable = index > 0 && !playing;
            _stepNextBtn.interactable = index < count - 1 && !playing;
            _stepDelBtn.interactable = count > 1 && !playing;
            _stepText.interactable = !playing;
            if (!_stepText.isFocused) { _cardMuted = true; _stepText.text = caption ?? ""; _cardMuted = false; }
            _playLabel.text = playing ? "Стоп" : "Играть";
        }

        /// Shows "Шаг N — caption" large while playing; step 0 / null hides it.
        public void ShowStoryCaption(int step, string text)
        {
            if (_caption == null) return;
            bool show = step > 0;
            _caption.gameObject.SetActive(show);
            if (show) _captionText.text = $"<font-weight=700><color={T.ToHex(T.Accent)}>Шаг {step}</color></font-weight>   {(string.IsNullOrEmpty(text) ? "…" : text)}";
        }

        /// Left column, under the header (and under the toolbar when it's showing).
        float LeftColumnBelow(bool includeToolbar)
        {
            float bottom = _header != null ? -_header.anchoredPosition.y + _header.sizeDelta.y : 220f;
            if (includeToolbar && _storyBar != null && _storyBar.gameObject.activeSelf) bottom += T.S1 + _storyBar.sizeDelta.y;
            return bottom + T.S1;
        }

        void ClearPressed()
        {
            if (_clearLabel.text == "Точно?")
            {
                _clearLabel.text = "Очистить";
                if (_clearReset != null) StopCoroutine(_clearReset);
                StoryClear?.Invoke();
                return;
            }
            _clearLabel.text = "Точно?";
            if (_clearReset != null) StopCoroutine(_clearReset);
            _clearReset = StartCoroutine(ResetClearLabel());
        }

        IEnumerator ResetClearLabel()
        {
            yield return new WaitForSecondsRealtime(4f);
            if (_clearLabel != null) _clearLabel.text = "Очистить";
        }

        RawImage _cardPortrait;
        TextMeshProUGUI _cardPortraitEmpty;
        RectTransform _cardShapes, _cardPortraitRow, _cardDel, _cardDone;
        readonly Dictionary<string, Button> _shapeButtons = new Dictionary<string, Button>();
        TextMeshProUGUI _portraitClearLabel;

        void BuildCard()
        {
            _card = NewRect("Story Card", _canvasRect);
            _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(0, 1);
            _card.sizeDelta = new Vector2(390, 250);
            _card.gameObject.AddComponent<Image>().color = T.Surface;

            _cardKicker = NewText("Kicker", _card, "", NevishkFonts.UIBold, T.SizeKicker, T.Accent);
            _cardKicker.characterSpacing = T.KickerTracking;
            Pin(_cardKicker.rectTransform, T.S2, -T.S2, 20, T.S2);

            // Portrait (figures): round frame left of the name; click to load one.
            var pf = new GameObject("Portrait", typeof(RectTransform), typeof(Image), typeof(Button));
            pf.transform.SetParent(_card, false);
            var pr = (RectTransform)pf.transform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0, 1);
            pr.sizeDelta = new Vector2(64, 64);
            var pfi = pf.GetComponent<Image>(); pfi.color = T.Raised;
            var pfb = pf.GetComponent<Button>(); pfb.targetGraphic = pfi; StyleButton(pfb, pfi);
            pfb.onClick.AddListener(() => CardPortraitPick?.Invoke());
            _cardPortrait = NewRect("Image", pr).gameObject.AddComponent<RawImage>();
            Fill(_cardPortrait.rectTransform); _cardPortrait.raycastTarget = false;
            _cardPortraitEmpty = NewText("Empty", pr, "+ фото", NevishkFonts.UI, 13, T.TextMuted);
            Fill(_cardPortraitEmpty.rectTransform); _cardPortraitEmpty.alignment = TextAlignmentOptions.Center;

            _cardName = MakeInput(_card, "Имя…", false);
            _cardName.onValueChanged.AddListener(v => { if (!_cardMuted) CardNameChanged?.Invoke(v); });

            _cardNote = MakeInput(_card, "Заметка: кто это, что здесь произошло…", true);
            _cardNote.onValueChanged.AddListener(v => { if (!_cardMuted) CardNoteChanged?.Invoke(v); });

            // Shapes (figures).
            _cardShapes = NewRect("Shapes", _card);
            _cardShapes.anchorMin = _cardShapes.anchorMax = _cardShapes.pivot = new Vector2(0, 1);
            float x = 0f;
            foreach (var (id, label) in StoryMode.Shapes)
            {
                var b = CompactButton(_cardShapes, label, () => CardShapeChosen?.Invoke(id));
                var t = b.GetComponentInChildren<TextMeshProUGUI>(); t.fontSize = 14f;
                b.sizeDelta = new Vector2(t.GetPreferredValues().x + 16f, 32f);
                b.anchorMin = b.anchorMax = b.pivot = new Vector2(0, 1);
                b.anchoredPosition = new Vector2(x, 0);
                x += b.sizeDelta.x + 4f;
                _shapeButtons[id] = b.GetComponent<Button>();
            }
            _cardShapes.sizeDelta = new Vector2(x, 32f);

            // Portrait actions (figures).
            _cardPortraitRow = NewRect("Portrait actions", _card);
            _cardPortraitRow.anchorMin = _cardPortraitRow.anchorMax = _cardPortraitRow.pivot = new Vector2(0, 1);
            var load = CompactButton(_cardPortraitRow, "Загрузить портрет", () => CardPortraitPick?.Invoke());
            load.anchorMin = load.anchorMax = load.pivot = new Vector2(0, 1);
            var clear = CompactButton(_cardPortraitRow, "Убрать", () => CardPortraitClear?.Invoke());
            clear.anchorMin = clear.anchorMax = clear.pivot = new Vector2(0, 1);
            clear.anchoredPosition = new Vector2(load.sizeDelta.x + 6f, 0);
            _portraitClearLabel = clear.GetComponentInChildren<TextMeshProUGUI>();
            _cardPortraitRow.sizeDelta = new Vector2(load.sizeDelta.x + 6f + clear.sizeDelta.x, 34f);

            _cardDel = CompactButton(_card, "Удалить", () => CardDelete?.Invoke());
            _cardDel.anchorMin = _cardDel.anchorMax = _cardDel.pivot = new Vector2(0, 0);
            _cardDel.anchoredPosition = new Vector2(T.S2, T.S2);
            _cardDel.GetComponentInChildren<TextMeshProUGUI>().color = T.Stamp;
            _cardDone = CompactButton(_card, "Готово", () => CardDone?.Invoke());
            _cardDone.anchorMin = _cardDone.anchorMax = _cardDone.pivot = new Vector2(1, 0);
            _cardDone.anchoredPosition = new Vector2(-T.S2, T.S2);

            _card.gameObject.SetActive(false);
        }

        /// Lays the card out for a figure (portrait, shapes) or a plain object / flag.
        void LayoutCard(bool figure)
        {
            float pad = T.S2, y = -pad - 20f - T.S1;
            float nameLeft = pad;
            var pr = (RectTransform)_cardPortrait.transform.parent;
            pr.gameObject.SetActive(figure);
            if (figure)
            {
                pr.anchoredPosition = new Vector2(pad, y);
                nameLeft = pad + 64f + T.S1;
            }
            var nr = (RectTransform)_cardName.transform;
            nr.anchorMin = new Vector2(0, 1); nr.anchorMax = new Vector2(1, 1); nr.pivot = new Vector2(0.5f, 1);
            nr.offsetMin = new Vector2(nameLeft, y - 40f); nr.offsetMax = new Vector2(-pad, y);
            y -= (figure ? 64f : 40f) + T.S1;

            var tr = (RectTransform)_cardNote.transform;
            tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 1);
            tr.offsetMin = new Vector2(pad, y - 84f); tr.offsetMax = new Vector2(-pad, y);
            y -= 84f + T.S1;

            _cardShapes.gameObject.SetActive(figure);
            _cardPortraitRow.gameObject.SetActive(figure);
            if (figure)
            {
                _cardShapes.anchoredPosition = new Vector2(pad, y); y -= 32f + 6f;
                _cardPortraitRow.anchoredPosition = new Vector2(pad, y); y -= 34f + T.S1;
            }
            _card.sizeDelta = new Vector2(Mathf.Max(390f, _cardShapes.sizeDelta.x + pad * 2f), -y + 34f + pad);
        }

        TMP_InputField MakeInput(Transform parent, string placeholder, bool multiline)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image));
            go.SetActive(false);   // TMP_InputField must have its parts assigned before OnEnable
            go.transform.SetParent(parent, false);
            var area = NewRect("Text Area", go.transform);
            area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(12, 6); area.offsetMax = new Vector2(-12, -6);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = NewText("Placeholder", area, placeholder, NevishkFonts.UI, T.SizeUI - 1, T.TextMuted);
            Fill(ph.rectTransform); ph.fontStyle = FontStyles.Italic;
            ph.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            ph.textWrappingMode = multiline ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            var txt = NewText("Text", area, "", NevishkFonts.UI, T.SizeUI, T.Text);
            Fill(txt.rectTransform);
            txt.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            txt.textWrappingMode = multiline ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = area; input.textComponent = txt; input.placeholder = ph;
            input.fontAsset = NevishkFonts.UI; input.pointSize = T.SizeUI;
            input.caretColor = T.Accent; input.customCaretColor = true;
            input.selectionColor = new Color(T.Accent.r, T.Accent.g, T.Accent.b, 0.35f);
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.characterLimit = multiline ? 600 : 60;
            StyleSelectable(input, go.GetComponent<Image>());
            go.SetActive(true);
            return input;
        }

        void BuildNotice()
        {
            _notice = NewRect("Story Notice", _canvasRect);
            _notice.anchorMin = _notice.anchorMax = _notice.pivot = new Vector2(0.5f, 0f);
            _notice.anchoredPosition = new Vector2(0, T.BarHeight + T.S6);
            _notice.gameObject.AddComponent<Image>().color = T.SurfaceSolid;
            var lay = _notice.gameObject.AddComponent<HorizontalLayoutGroup>();
            lay.padding = new RectOffset(14, 6, 5, 5); lay.spacing = 10; lay.childAlignment = TextAnchor.MiddleLeft;
            lay.childControlWidth = lay.childControlHeight = true; lay.childForceExpandWidth = lay.childForceExpandHeight = false;
            _notice.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            _notice.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _noticeText = NewText("Text", _notice, "", NevishkFonts.UI, 15f, T.Text);
            _noticeText.textWrappingMode = TextWrappingModes.NoWrap;
            var act = CompactButton(_notice, "Вернуть", () => { var cb = _noticeCallback; HideNotice(); cb?.Invoke(); });
            _noticeActionLayout = act.gameObject.AddComponent<LayoutElement>();
            _noticeActionLayout.minHeight = 34f; _noticeActionLayout.preferredWidth = act.sizeDelta.x;
            _noticeAction = act.GetComponent<Button>();
            _notice.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- state from StoryMode

        public void SetStoryActive(bool on)
        {
            _storyOn = on;
            _storyBar.anchoredPosition = new Vector2(T.S3, -LeftColumnBelow(false));
            _storyBar.gameObject.SetActive(on);
            if (_stepsBar != null) _stepsBar.gameObject.SetActive(on);
            if (!on && _caption != null) _caption.gameObject.SetActive(false);
            if (_storyButton != null)
            {
                var b = _storyButton.GetComponent<Button>();
                SetActiveLook(b, on);
            }
            if (!on) HideCard();
        }

        public void SetStoryTool(string tool)
        {
            foreach (var kv in _toolButtons) SetActiveLook(kv.Value, kv.Key == tool);
        }

        public void SetStoryColour(Color c)
        {
            for (int i = 0; i < _swatchRings.Count; i++)
                _swatchRings[i].color = StoryColours[i] == c ? T.Accent : new Color(1, 1, 1, 0f);
        }

        public void SetStoryHint(string text) { if (_storyHint != null) _storyHint.text = text; }

        static void SetActiveLook(Button b, bool active)
        {
            var c = b.colors;
            c.normalColor = active ? Color.Lerp(T.Raised, T.Accent, 0.35f) : T.Raised;
            c.selectedColor = active ? Color.Lerp(T.RaisedHover, T.Accent, 0.4f) : T.RaisedHover;
            b.colors = c;
        }

        public void ShowCard(string kicker, string name, string note, bool focusName, bool figure = false, string shape = null, Texture portrait = null)
        {
            _cardMuted = true;
            _cardKicker.text = kicker.ToUpperInvariant();
            _cardName.text = name ?? "";
            _cardNote.text = note ?? "";
            _cardMuted = false;
            LayoutCard(figure);
            foreach (var kv in _shapeButtons) SetActiveLook(kv.Value, kv.Key == (shape ?? "pawn"));
            _cardPortrait.texture = portrait;
            _cardPortrait.enabled = portrait != null;
            _cardPortraitEmpty.enabled = portrait == null;
            _portraitClearLabel.transform.parent.gameObject.SetActive(portrait != null);
            _card.anchoredPosition = new Vector2(T.S3, -LeftColumnBelow(true));
            _card.gameObject.SetActive(true);
            if (focusName) { _cardName.Select(); _cardName.ActivateInputField(); }
        }

        public void HideCard()
        {
            if (_card == null) return;
            if (_cardName.isFocused) _cardName.DeactivateInputField();
            if (_cardNote.isFocused) _cardNote.DeactivateInputField();
            _card.gameObject.SetActive(false);
        }

        public bool CardOpen => _card != null && _card.gameObject.activeSelf;

        /// A short message above the district tab, optionally with one action button. Hides itself after a while.
        public void Notice(string text, string actionLabel = null, Action action = null, float seconds = 6f)
        {
            _noticeText.text = text;
            _noticeCallback = action;
            _noticeAction.gameObject.SetActive(action != null);
            if (action != null)
            {
                // The layout group sizes children from their layout elements, so give the button its text's width.
                var label = _noticeAction.GetComponentInChildren<TextMeshProUGUI>();
                label.text = actionLabel;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                _noticeActionLayout.preferredWidth = _noticeActionLayout.minWidth = label.GetPreferredValues().x + 22f;
            }
            _notice.anchoredPosition = new Vector2(0, _storyOn ? 44f + T.S2 + 50f + T.S2 : T.BarHeight + T.S6);
            _notice.gameObject.SetActive(true);
            _notice.SetAsLastSibling();
            if (_noticeHide != null) StopCoroutine(_noticeHide);
            _noticeHide = StartCoroutine(HideNoticeLater(seconds));
        }

        IEnumerator HideNoticeLater(float s) { yield return new WaitForSecondsRealtime(s); HideNotice(); }

        void HideNotice() { if (_notice != null) _notice.gameObject.SetActive(false); _noticeCallback = null; }
    }
}
