using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Nevishk
{
    /// <summary>
    /// Runtime TextMeshPro font assets built from the TTFs in Resources/Fonts, so Cyrillic works
    /// without the Font Asset Creator.
    ///   UI      — PT Sans (interface, labels, buttons)
    ///   Reading — PT Serif (lore text; real italic and bold faces wired to &lt;i&gt; and &lt;b&gt;)
    ///   Display — Playfair Display Bold (titles; nearest Cyrillic match to the board's Abril Fatface)
    /// </summary>
    public static class NevishkFonts
    {
        static TMP_FontAsset _ui, _uiBold, _reading, _readingItalic, _readingBold, _display;
        static bool _wired;

        public static TMP_FontAsset UI => _ui ??= Create("Fonts/PTSans-Regular") ?? TMP_Settings.defaultFontAsset;
        public static TMP_FontAsset UIBold => _uiBold ??= Create("Fonts/PTSans-Bold") ?? UI;
        public static TMP_FontAsset Display => _display ??= Create("Fonts/PlayfairDisplay-Bold") ?? UIBold;

        public static TMP_FontAsset Reading
        {
            get
            {
                _reading ??= Create("Fonts/PTSerif-Regular") ?? UI;
                if (!_wired)
                {
                    _wired = true;
                    _readingItalic = Create("Fonts/PTSerif-Italic");
                    _readingBold = Create("Fonts/PTSerif-Bold");
                    // fontWeightTable's setter is internal in Unity 6's TextMeshPro; edit the array in place.
                    var table = _reading.fontWeightTable;
                    if (table != null && table.Length > 7)
                    {
                        if (_readingItalic != null) table[4].italicTypeface = _readingItalic;   // 400 italic → <i>
                        if (_readingBold != null) table[7].regularTypeface = _readingBold;      // 700 → <b>
                    }
                    var uiTable = UI.fontWeightTable;
                    if (uiTable != null && uiTable.Length > 7 && UIBold != UI) uiTable[7].regularTypeface = UIBold;
                }
                return _reading;
            }
        }

        // Kept for older call sites.
        public static TMP_FontAsset Regular => UI;
        public static TMP_FontAsset Bold => UIBold;

        static TMP_FontAsset Create(string path)
        {
            var font = Resources.Load<Font>(path);
            if (font == null)
            {
                Debug.LogWarning($"[Nevishk] Font Resources/{path} missing.");
                return null;
            }
            var fa = TMP_FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024,
                                                   AtlasPopulationMode.Dynamic, true);
            fa.name = font.name + " SDF (runtime)";
            return fa;
        }
    }
}
