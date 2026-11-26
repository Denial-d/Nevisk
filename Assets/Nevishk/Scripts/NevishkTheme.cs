using UnityEngine;

namespace Nevishk
{
    /// <summary>
    /// Design tokens for the whole UI. Change the look here, not in the components.
    /// Palette is taken from the Miro lore cards (yellow on charcoal), warmed toward
    /// aged archive paper. All text colours are checked against Surface for WCAG contrast:
    ///   Text 14.2:1 · TextMuted 7.0:1 · Accent 13.5:1 · Stamp 4.8:1 (bold only) · Faint 4.1:1 (non-text / large)
    /// </summary>
    public static class NevishkTheme
    {
        // ---- colour roles
        public static readonly Color Surface      = Hex("#17150F", 0.96f); // reading panel
        public static readonly Color SurfaceSolid = Hex("#17150F");
        public static readonly Color Raised       = Hex("#242019");        // rows, buttons
        public static readonly Color RaisedHover  = Hex("#312C23");
        public static readonly Color Scrim        = Hex("#0B0A07", 0.72f); // bars over the 3D view
        public static readonly Color Border       = Hex("#E9E3D3", 0.10f);
        public static readonly Color Text         = Hex("#E9E3D3");        // bone
        public static readonly Color TextMuted    = Hex("#A8A08F");
        public static readonly Color Faint        = Hex("#7D7668");        // dividers, large captions only
        public static readonly Color Accent       = Hex("#FFDC4A");        // board yellow: links, kicker, selection
        public static readonly Color OnAccent     = Hex("#17150F");
        public static readonly Color Stamp        = Hex("#E0533F");        // archive stamps, "закрыто"

        // 3D scene
        public static readonly Color SceneBackground = Hex("#232624");   // overcast murk over the horizon
        public static readonly Color Fog             = Hex("#2B2E2C");   // close to the sky, so distant land melts into it
        public static readonly Color GroundTint      = Hex("#A89F8C");   // multiplies the paper-white base map
        public static readonly Color Surroundings    = Hex("#201E1A");
        public static readonly Color Concrete        = Hex("#B5AFA4");
        public const float DistrictTint = 0.16f;
        public static readonly Color TerrainTint     = Hex("#E8E2D6");   // multiplies the generated ground colours
        public static readonly Color Water           = Hex("#1B2A2E");
        public static readonly Color SlabSide        = Hex("#2C2924");                         // how much district colour reaches buildings
        public static readonly Color SunColor        = Hex("#D8D6CC");   // pale, filtered through cloud
        public const float SunIntensity = 0.9f;
        public static readonly Vector3 SunAngles     = new Vector3(40f, -40f, 0f); // high, hidden sun → short, soft shadows
        public static readonly Color LabelOutline    = Hex("#0B0A07", 0.9f);
        public static readonly Color LabelPlate      = Hex("#12110D", 0.72f);   // subtle dark box behind map labels

        // ---- type scale (reference resolution 1600×900; uGUI scales it)
        public const float SizeTitle   = 40f;   // Playfair Display Bold
        public const float SizeTitleMin = 26f;
        public const float SizeBody    = 22f;   // PT Serif, ~62 chars per line in the panel
        public const float SizeMeta    = 16f;
        public const float SizeKicker  = 14f;   // PT Sans Bold, tracked caps
        public const float SizeUI      = 17f;   // PT Sans
        public const float SizeRowSub  = 14f;

        public const float BodyLineSpacing = 32f;      // TMP em/100 → ~1.6 line height with PT Serif
        public const float BodyParagraphSpacing = 18f;
        public const float KickerTracking = 8f;

        // ---- spacing (8-pt rhythm) & sizes
        public const float S1 = 8f, S2 = 16f, S3 = 24f, S4 = 32f, S6 = 48f;
        public const float PanelWidth = 720f;          // keeps body at 60–70 characters per line
        public const float PanelGutter = 40f;
        public const float TouchTarget = 44f;          // minimum hit size for every control
        public const float RowHeight = 60f;
        public const float BarHeight = 64f;

        // ---- motion
        public const float HoverFade = 0.12f;          // 80–150 ms press/hover feedback
        public const float CameraSmoothing = 7f;       // lower = slower fly-to

        static Color Hex(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c.a = alpha;
            return c;
        }

        public static string ToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
