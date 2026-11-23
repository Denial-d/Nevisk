# Невишк — 3D lore map (Unity)

A clickable 3D overview of Невишк built from the team's Miro board. Every district and landmark on the
map opens its lore, and the full archive documents can be read in-app.

## Install (Unity 6, Universal 3D template)

1. Copy the `Assets/Nevishk` folder into your project's `Assets` folder.
2. If Unity asks to **Import TMP Essentials**, click Import. Or use *Window ▸ TextMeshPro ▸ Import TMP Essential Resources*.
3. Menu **Nevishk ▸ 1. Set Up Scene**. This creates `Assets/Nevishk/Nevishk.unity`, the materials and the map object.
4. Press **Play**.

### Controls

| Action | Mouse | Touch |
|---|---|---|
| Pan | left-drag | one finger |
| Rotate / tilt | right-drag, or Shift + left-drag | two-finger twist |
| Zoom | wheel | pinch |
| Open lore | click a pin or district name | tap |
| Close panel | Esc or × | × |

The bottom bar jumps to any district. **Архив** lists every document, and the search box searches every name and all lore text.
Pins with a yellow pole have written lore.

### Sharing a link to an entry (web build)

Every opened entry updates the page URL, e.g. `…/index.html#lm_046` (Prime City) or `#doc_balaur_log`.
Anyone opening that link lands on the entry, and the browser's Back button moves between entries.

## Design

All visual decisions live in `Scripts/NevishkTheme.cs` (colours, type scale, spacing, motion). Change the look there.

- **Palette:** taken from the board's lore cards: yellow `#FFDC4A` on charcoal, warmed toward archive paper.
  The red stamp `#E0533F` marks archive documents. Every text colour is checked against the panel: body 14.2:1, secondary 7.0:1.
- **Type:** PT Serif for reading (about 62 characters per line, 1.6 line height), Playfair Display for titles and
  district lettering (the nearest Cyrillic match to the board's Abril Fatface), PT Sans for interface text.
- **Interaction:** every control is at least 44 px; nothing depends on hover alone; keyboard focus stays visible.
- **Motion:** panel enters in 220 ms and exits in 150 ms. In the web build, a visitor's "reduce motion" setting
  makes camera moves near-instant and turns off panel animation and film grain.
- **Atmosphere:** URP post-processing with a light vignette, desaturated warm grade, bloom (Балаур's fire) and film grain.
  Untick **Atmosphere** on the *Nevishk Map* object to turn it off.

## Publish as a web page

1. *File ▸ Build Profiles*: switch the platform to **Web** (needs the Web Build Support module).
2. Menu **Nevishk ▸ 3. Build for Web**. The output goes to `Builds/Web`.
3. Upload the contents of `Builds/Web` to any static host, for example GitHub Pages, Netlify or itch.io (HTML game).
   The build uses Brotli with decompression fallback, so it works without any special server configuration.

## What's in here

| Path | What |
|---|---|
| `Resources/nevishk_lore.json` | 9 districts, 50 landmarks and 12 archive documents, taken from the Miro board |
| `Resources/nevishk_layout.json` | Placeholder building footprints and trees, scattered inside each district outline |
| `Resources/nevishk_basemap.png` | The hand-drawn district map from the board, used as the ground texture |
| `Resources/Fonts` | PT Sans, PT Serif, Playfair Display (all Cyrillic, SIL Open Font License; licences included) |
| `Plugins/WebGL/NevishkBrowser.jslib` | Deep links and reduced-motion detection in the browser |
| `Scripts/NevishkMap.cs` | Builds the city, pins and labels; handles hover, click and fly-to |
| `Scripts/MapCameraController.cs` | Orbit, pan and zoom camera with touch support |
| `Scripts/LorePanelUI.cs` | Runtime UI: district bar, tooltip, reading panel, archive |
| `Scripts/Editor/NevishkSetup.cs` | The Nevishk menu: scene setup, web settings, web build |
| `Tools/*.py` | The scripts that generated the JSON from the board export (for reference) |

## Editing lore

Edit `nevishk_lore.json` directly. Text supports TextMeshPro rich-text tags (`<b>`, `<i>`, `<color=#FFDC4A>`).
Positions are `uv` coordinates on the base map (0–1, origin top-left).
Each landmark's `docs` lists the archive documents linked from it.

## Known gaps

- Buildings are placeholder blocks. Swap them for real models district by district.
- Some districts have no lore on the board yet: Красный электровой, Балтаг, Медные лужи, Loom of Echoes and Империал.
- Board contradictions and leftover chat notes inside the documents were deliberately left as they are for now.
- 13 images and 3 uploaded files on the board were not imported.
