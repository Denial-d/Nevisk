"""Take the drawn buildings and lettering off the base map.

1. Writes a clean copy of the map (outlines, railway and ponds kept; icons, trees and all text removed),
   which build_terrain.py then uses for the ground texture.
2. Writes Resources/nevishk_models.json: one 3D model per drawn building, at the icon's position,
   linked to the matching lore landmark where there is one.

Usage: python build_models.py <basemap.png> <lore.json> <clean_out.png> <models_out.json> [debug.png]
"""
import json, sys
import numpy as np
from PIL import Image
from scipy import ndimage as ndi

BASEMAP, LORE, CLEAN_OUT, MODELS_OUT = sys.argv[1:5]
DEBUG = sys.argv[5] if len(sys.argv) > 5 else None

im = np.asarray(Image.open(BASEMAP).convert('RGB')).astype(np.int32)
H, W, _ = im.shape
r, g, b = im[..., 0], im[..., 1], im[..., 2]
blue = (b > 170) & (r < 120)                                    # ponds: keep
green = (g > 140) & (r < 90) & (b < 120)                        # park + drawn trees
ink = (im.sum(2) < 380) & ~blue & ~green
grey = (np.abs(r - g) < 12) & (np.abs(g - b) < 12) & (r > 90) & (r < 205)
mark = ink | grey

# Big connected strokes are the district outlines and the railway: keep them.
lab, n = ndi.label(mark, structure=np.ones((3, 3)))
big = np.zeros_like(mark)
for i, sl in enumerate(ndi.find_objects(lab), 1):
    if max(sl[0].stop - sl[0].start, sl[1].stop - sl[1].start) > 140:
        big |= lab == i
erase = mark & ~big

# Drawn trees (small green triangles) inside the ravine; the park's big green area stays.
glab, _ = ndi.label(green)
for i, sl in enumerate(ndi.find_objects(glab), 1):
    if max(sl[0].stop - sl[0].start, sl[1].stop - sl[1].start) < 40:
        erase |= glab == i

# Lettering and icons that touch an outline: inside these boxes remove only thin pen strokes,
# keeping the thick outline.  (x0, y0, x1, y1) in base-map px.
THIN_BOXES = [(436, 200, 492, 238),    # Канавник icon + name, touching Империал's outline
              (612, 72, 700, 92),      # "Новые Камыши"
              (640, 88, 760, 104),     # "Академическая усадьба"
              (690, 72, 730, 92)]      # manor icon
thick3 = ndi.binary_opening(mark, structure=np.ones((3, 3)), iterations=1)   # outline cores (≥3 px)
thick5 = ndi.binary_opening(mark, structure=np.ones((3, 3)), iterations=2)   # only the heaviest strokes (≥5 px)
for x0, y0, x1, y1 in THIN_BOXES:
    box = np.zeros_like(mark); box[y0:y1, x0:x1] = True
    erase |= box & mark & ~thick3
# Boxes clear of any outline: erase everything that isn't paper, pond or park.
ALL_BOXES = [(620, 81, 700, 92),       # rest of "Новые Камыши" (below the island outline)
             (452, 226, 492, 240),     # rest of "Канавник"
             (676, 368, 718, 402)]     # faint Батрачник towers
all_zone = np.zeros_like(mark)
for x0, y0, x1, y1 in ALL_BOXES: all_zone[y0:y1, x0:x1] = True
white = (r > 235) & (g > 235) & (b > 235)
beige = (r > 200) & (g > 200) & (b > 140) & (b < 200) & (np.abs(r - g) < 30)
for x0, y0, x1, y1 in ALL_BOXES:
    box = np.zeros_like(mark); box[y0:y1, x0:x1] = True
    erase |= box & ~white & ~blue & ~thick5
# Around every removed icon/word, also remove faint anti-aliased leftovers.
cl, _ = ndi.label(ndi.binary_dilation(erase, iterations=4))
for sl in ndi.find_objects(cl):
    reg = np.zeros_like(mark); reg[sl] = True
    erase |= reg & ~white & ~beige & ~blue & ~green & ~big
# White paper showing through inside the ravine (the Безмолвные плиты icon's fill) → ravine floor.
ravine_zone = ndi.binary_fill_holes(ndi.binary_closing(beige, iterations=6))
erase |= ravine_zone & ~beige & ~blue & ~big       # icon fills, faint lettering, stray strokes
erase[:80, :170] = True                                         # inset thumbnail in the corner
erase = ndi.binary_dilation(erase, iterations=1) & ~blue        # catch anti-aliased edges
# Never cut a district outline: protect outline cores everywhere except the lettering-only boxes.
outline_core = ndi.binary_dilation(big & thick3, iterations=1)
erase &= ~(outline_core & ~all_zone)

# Fill each erased pixel with the nearest surviving paper / fill colour.
keep = ~erase & ~(mark & ~big) & ~(ravine_zone & ~beige & ~big)
idx = ndi.distance_transform_edt(~keep, return_distances=False, return_indices=True)
clean = im[idx[0], idx[1]].astype(np.uint8)
Image.fromarray(clean).save(CLEAN_OUT, optimize=True)

# ---------------------------------------------------------------- models
# type, centre (base-map px), footprint (w, d px), yaw°, landmark name on the board (or None), label
MODELS = [
    ('factory',  (261, 224), (34, 22), 0,   'Виброприбор', None),
    ('factory',  (369, 205), (36, 22), 0,   'МассПриводТрест', None),
    ('factory',  (318, 269), (32, 20), 0,   'Cyber-Agro', None),
    ('houses',   (660, 47),  (30, 22), 0,   'Муты', None),
    ('manor',    (707, 85),  (30, 18), 0,   'Академическая усадьба / НИИ «Рассвет»', 'Академическая усадьба'),
    ('pair',     (461, 222), (24, 16), 0,   'Канавник', None),
    ('depot',    (497, 240), (22, 18), 0,   'ДЕПО 1', 'Депо'),
    ('slabs',    (429, 317), (34, 20), 0,   'Безмолвные плиты', None),
    ('ruin',     (331, 346), (22, 18), 8,   'Мёртвый завод', None),
    ('shed',     (413, 291), (44, 7),  0,   None, None),
    ('ruin',     (465, 308), (12, 16), 0,   None, None),
    ('ruin',     (461, 379), (14, 16), -6,  None, None),
    ('spillway', (497, 312), (8, 30),  0,   'Гидросброс (водопад)', 'Гидросброс'),
    ('gallery',  (627, 351), (30, 18), 0,   'Галерея', None),
    ('towers',   (698, 387), (28, 22), 0,   'Батрачник', None),
    ('chapel',   (880, 474), (14, 18), 0,   'Храм бомжа', None),
    # The heavy straight line on Ущелье's south-west rim, labelled «Мост»: the viaduct Ущелье lies under.
    ('bridge',   (412, 415), (131, 10), 30.6, 'Мост', 'Мост (виадук)'),
]
lore = json.load(open(LORE, encoding='utf-8'))
by_name = {l['name']: l['id'] for l in lore['landmarks']}
out = []
for i, (kind, (x, y), (w, d), yaw, lm, label) in enumerate(MODELS):
    lid = by_name.get(lm) if lm else None
    if lm and not lid:
        print('WARNING: no landmark named', lm)
    out.append(dict(id=f'model_{i:02d}', type=kind, uv=[round(x / W, 5), round(y / H, 5)],
                    size=[w, d], yaw=yaw, landmark=lid or '', label=label or (lm or '')))
json.dump(dict(models=out), open(MODELS_OUT, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('erased px:', int(erase.sum()), ' models:', len(out), ' linked:', sum(1 for m in out if m['landmark']))

if DEBUG:
    dbg = clean.copy()
    for m in out:
        x, y = int(m['uv'][0] * W), int(m['uv'][1] * H)
        dbg[max(y - 2, 0):y + 3, max(x - 2, 0):x + 3] = (230, 40, 40)
    Image.fromarray(dbg).resize((W * 2, H * 2), Image.NEAREST).save(DEBUG)
