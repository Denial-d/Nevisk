"""Build a 3D terrain for Невишк from the hand-drawn base map.

District outlines become raised land, the channels between them become the river Мырла,
Ущелье becomes a ravine, and the area outside the city becomes hills with mountains (Горы)
to the east.

Outputs (into Assets/Nevishk/Resources):
  nevishk_terrain.bytes   heights + water levels (int16, decimetres) plus the world extent they cover
  nevishk_albedo.png      ground colour texture, 2048 px wide, with the map's ink lines

The map is padded with PAD px of countryside on every side; that margin slopes down into the dark
with an irregular edge, so the terrain fades out instead of ending in a cut.

Usage: python build_terrain.py <basemap.png> <resources_dir> [preview.png]
World units match the base map: 1 px = 1 unit, map is 1280 x 581.
"""
import struct, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi

BASEMAP, OUTDIR = sys.argv[1], sys.argv[2]
PREVIEW = sys.argv[3] if len(sys.argv) > 3 else None
PAD = 200                      # px of extra countryside around the drawn map
CELL = 2.5                     # px per terrain grid cell
rng = np.random.RandomState(7)

src = np.asarray(Image.open(BASEMAP).convert('RGB')).astype(np.float32).copy()
SRC_H, SRC_W, _ = src.shape
src[:80, :170] = 255           # drop the little inset thumbnail in the top-left corner
im = np.pad(src, ((PAD, PAD), (PAD, PAD), (0, 0)), constant_values=255)
H, W, _ = im.shape
GRID_W, GRID_H = int((W - 1) / CELL) + 1, int((H - 1) / CELL) + 1
def P(x, y): return (x + PAD, y + PAD)   # base-map px → padded px
r, g, b = im[..., 0], im[..., 1], im[..., 2]
lum = im.mean(2) / 255.0

ink = im.sum(2) < 250
wall = ndi.binary_dilation(ink, iterations=1)
white = (r > 230) & (g > 230) & (b > 230)
beige = (r > 200) & (g > 200) & (b > 140) & (b < 200) & (np.abs(r - g) < 30)
green = (g > 150) & (r < 90) & (b < 120)
blue = (b > 170) & (r < 120)

def flood(seed, passable):
    lab, _ = ndi.label(passable)
    v = lab[seed[1], seed[0]]
    return lab == v if v else np.zeros_like(passable)

free = ~wall
# district id -> (seed px, plateau height in units)
DISTRICTS = {
    'krasnyi_elektrovoi': (P(960, 115), 13.0),
    'baltag':             (P(841, 277), 11.0),
    'mednye_luzhi':       (P(640, 390), 8.0),
    'nevishk_tech':       (P(300, 250), 10.0),
    'loom_of_echoes':     (P(470, 95), 16.0),
    'novyi_tsentr':       (P(700, 180), 9.0),
    'imperial':           (P(505, 240), 14.0),   # old centre on a rise
}
plateau = np.zeros((H, W), np.float32)
land = np.zeros((H, W), bool)
for did, (seed, ph) in DISTRICTS.items():
    m = flood(seed, free)
    plateau[m] = ph
    land |= m

# Новые Камыши: small island whose outline isn't closed; traced by hand.
kam = Image.new('L', (W, H), 0)
ImageDraw.Draw(kam).polygon([P(608, 84), P(640, 74), P(700, 68), P(762, 80), P(774, 100), P(736, 110),
                             P(700, 124), P(660, 114), P(614, 108)], fill=255)
kam = np.asarray(kam) > 0
plateau[kam & ~land] = 9.0
land |= kam

ravine = ndi.binary_closing(beige, iterations=4)
ravine = ravine & ndi.binary_dilation(beige, iterations=6)   # keep only around the beige area
ravine = ndi.binary_fill_holes(ravine)                       # drawn icons inside become ravine floor, not mesas
park = flood(P(760, 470), green | blue)
park = ndi.binary_fill_holes(park)
ponds = blue & (park | kam | ndi.binary_dilation(kam, iterations=12))

# Ink pixels inside districts (lettering, icons) are land: fill small holes only.
landish = land | ravine | park
holes = ndi.binary_fill_holes(landish) & ~landish
lab, n = ndi.label(holes)
sizes = ndi.sum(holes, lab, range(1, n + 1))
for i, s in enumerate(sizes, 1):
    if s < 3000: landish |= lab == i
landish = ndi.binary_dilation(landish, iterations=3)           # absorb the ink outlines into the banks

# Channels = narrow gaps between districts that closing would fill → the river.
city = ndi.binary_closing(landish, structure=np.ones((3, 3)), iterations=17) | landish
channels = city & ~landish
# Small pockets (shallow bays in the outline) aren't rivers: give them back to the outside.
lab, n = ndi.label(channels)
sizes = ndi.sum(channels, lab, range(1, n + 1))
for i, s_ in enumerate(sizes, 1):
    if s_ < 1500: channels[lab == i] = False
city = landish | channels
exterior = ~city

# Spread plateau heights into every land pixel (outlines, filled lettering).
idx = ndi.distance_transform_edt(plateau == 0, return_distances=False, return_indices=True)
plateau_full = plateau[idx[0], idx[1]]

def fbm(octaves, base, amp_falloff=0.5):
    total = np.zeros((H, W), np.float32); amp = 1.0; norm = 0.0
    for o in range(octaves):
        sigma = base / (2 ** o)
        n_ = ndi.gaussian_filter(rng.randn(H, W).astype(np.float32), sigma, mode='wrap')
        n_ /= (n_.std() + 1e-6)
        total += n_ * amp; norm += amp; amp *= amp_falloff
    return total / norm

def smooth(x, a, b_):
    t = np.clip((x - a) / (b_ - a), 0, 1)
    return t * t * (3 - 2 * t)

n_low = fbm(4, 60)
n_hill = fbm(5, 90)
n_ridge = 1.0 - np.abs(fbm(5, 50))

h = np.zeros((H, W), np.float32)
water = np.full((H, W), np.nan, np.float32)

# ---- city land: banks rise from the water to each district's plateau
d_land = ndi.distance_transform_edt(landish)
bank = smooth(d_land, 0, 16)                                 # wide, soft river banks
h_land = 1.2 + (plateau_full - 1.2) * bank + n_low * 1.4 * bank
h[landish] = h_land[landish]

# ---- river Мырла in the channels
d_chan = ndi.distance_transform_edt(channels)
h[channels] = (-1.0 - 4.0 * smooth(d_chan, 0, 6))[channels]
water[channels] = 0.0

# ---- Ущелье: ravine ~32 units below the rim, steep walls
d_rav = ndi.distance_transform_edt(ravine)
rim = 12.0
floor_relief = fbm(4, 22) * 3.0 + fbm(2, 6) * 0.8                 # hummocks for the forest floor
h_rav = rim + (-32.0 - rim) * smooth(d_rav, 0, 20) + floor_relief * smooth(d_rav, 12, 30)
h[ravine] = h_rav[ravine]

# ---- park (Аллея Бужорилор): low, with ponds
h[park] = (5.0 + n_low * 0.8)[park]

# ---- ponds in the park and around Камыши
pond_d = ndi.distance_transform_edt(ponds)
base_here = np.where(park, 5.0, 9.0)
h[ponds] = (base_here - 1.5 - 3.0 * smooth(pond_d, 0, 4))[ponds]
water[ponds] = (base_here - 1.0)[ponds]

# ---- outside the city: hills, inlets where the river leaves, mountains to the east
d_out = ndi.distance_transform_edt(exterior)
# Only where a channel actually opens onto the outside (a river mouth) does the land dip to water.
mouths = channels & ndi.binary_dilation(exterior, iterations=2)
d_to_mouth = ndi.distance_transform_edt(~mouths)
inlet = smooth(d_to_mouth, 0, 40)                     # 0 at a river mouth → 1 farther out
# Positions relative to the drawn map (0..1 inside it, beyond that in the padding).
xs = ((np.arange(W) - PAD) / SRC_W)[None, :].repeat(H, 0)
ys = ((np.arange(H) - PAD) / SRC_H)[:, None].repeat(W, 1)
# Edge falloff: an irregular rim where the countryside slopes away into the dark.
jj, ii = np.mgrid[0:H, 0:W]
d_border = np.minimum(np.minimum(ii, W - 1 - ii), np.minimum(jj, H - 1 - jj)) + fbm(3, 45) * 35
fall = smooth(d_border, 5, PAD * 0.9)
hills = 9 + 26 * smooth(d_out, 0, 160) + n_hill * 9 * smooth(d_out, 10, 80)   # meets the city edge at ~bank height
mountains = 140 * smooth(xs, 0.80, 0.97) * (0.45 + 0.55 * n_ridge) * smooth(d_out, 15, 90)
north_hills = 26 * smooth(1 - ys, 0.75, 1.0) * (0.6 + 0.4 * n_hill) * smooth(d_out, 10, 60)
h_ext = (hills + mountains + north_hills) * inlet + (-3.0) * (1 - inlet)
# (No edge sink: the countryside continues at natural height; Unity's outer landscape carries on from here.)
h[exterior] = h_ext[exterior]
mouth = exterior & (h < 0) & (inlet < 0.999)        # river mouths only
water[mouth] = 0.0

# ---- the Мырла continues: carve each river mouth onward as a meandering valley to the edge of the grid
lab_m, n_m = ndi.label(ndi.binary_dilation(mouths, iterations=3))
chan_ys, chan_xs = np.nonzero(channels)
river_exits = []
river_mask = np.zeros((H, W), bool)
mrng = np.random.RandomState(3)
for i in range(1, n_m + 1):
    ys_, xs_ = np.nonzero((lab_m == i) & mouths)
    if len(xs_) < 5: continue
    mx, my = xs_.mean(), ys_.mean()
    near = (chan_xs - mx) ** 2 + (chan_ys - my) ** 2 < 45 ** 2
    cx_, cy_ = chan_xs[near].mean(), chan_ys[near].mean()
    d = np.array([mx - cx_, my - cy_]); d /= np.hypot(*d) + 1e-9
    base = np.arctan2(d[1], d[0]); ang = base
    x, y = mx, my; path = [(x, y)]
    while 2 < x < W - 3 and 2 < y < H - 3:
        ang += mrng.randn() * 0.12 + (base - ang) * 0.08      # meander, drifting back to the main heading
        x += np.cos(ang) * 3; y += np.sin(ang) * 3
        path.append((x, y))
    img = Image.new('L', (W, H), 0)
    ImageDraw.Draw(img).line(path, fill=255, width=14)
    river_mask |= (np.asarray(img) > 0) & exterior
    ex, ey = path[-1]
    river_exits.append(dict(x=float(ex - PAD - SRC_W / 2), z=float(SRC_H / 2 - (ey - PAD)),
                            dx=float(np.cos(ang)), dz=float(-np.sin(ang)), width=14.0))
d_riv = ndi.distance_transform_edt(~river_mask)
valley = smooth(d_riv, 0, 60)                          # 0 in the river → 1 outside the valley
h_riv = -5.0 + 4.0 * smooth(d_riv, 0, 5)
h[exterior] = np.where(river_mask, h_riv, -1.0 + (h + 1.0) * valley)[exterior]
water[river_mask] = 0.0
mouth |= river_mask

h = ndi.gaussian_filter(h, 0.8)

# ---- ground colour
def col(hexs):
    hexs = hexs.lstrip('#'); return np.array([int(hexs[i:i + 2], 16) for i in (0, 2, 4)], np.float32) / 255

gy, gx = np.gradient(h)
slope = np.sqrt(gx * gx + gy * gy)
rock = smooth(slope, 0.9, 2.2)
alb = np.zeros((H, W, 3), np.float32)
tone = (1 + 0.05 * fbm(3, 6))[..., None]
alb[:] = col('#CBBFA6')                               # city ground: dry earth / paper
alb[exterior] = col('#7D7A5C')                        # steppe grass
alb[exterior & (h > 60)] = col('#8A847A')             # mountain rock
alb[channels | mouth] = col('#3A3A32')                # river bed
alb[ravine] = col('#5E6544')                          # mossy ravine floor
alb[park] = col('#4E6A3C')
alb[ponds] = col('#2F3A33')
alb = alb * (1 - rock[..., None]) + col('#5B5650') * rock[..., None]
alb = alb * tone
# The drawn ink outlines are not printed on the ground any more: district edges are real banks now.
alb_img = Image.fromarray((np.clip(alb, 0, 1) * 255).astype(np.uint8)).resize((2048, int(2048 * H / W)), Image.BICUBIC)
alb_img.save(f'{OUTDIR}/nevishk_albedo.png', optimize=True)

# ---- export grid (area-average down to GRID_W x GRID_H)
def down(a):
    return np.asarray(Image.fromarray(a.astype(np.float32), mode='F').resize((GRID_W, GRID_H), Image.BOX))

hg = down(h)
wmask = down(np.where(np.isnan(water), 0, 1).astype(np.float32)) > 0.3
wlev = down(np.nan_to_num(water, nan=0.0)) / np.maximum(down(np.where(np.isnan(water), 0, 1).astype(np.float32)), 1e-3)
wq = np.where(wmask, np.round(wlev * 10), -32768).astype('<i2')
hq = np.clip(np.round(hg * 10), -32000, 32000).astype('<i2')
with open(f'{OUTDIR}/nevishk_terrain.bytes', 'wb') as f:
    # World extent covered by the grid. World x = base-map px − SRC_W/2, world z = SRC_H/2 − base-map py.
    min_x, max_x = -PAD - SRC_W / 2, (W - 1 - PAD) - SRC_W / 2
    max_z, min_z = SRC_H / 2 + PAD, SRC_H / 2 - (H - 1 - PAD)
    f.write(b'NVT2'); f.write(struct.pack('<ii', GRID_W, GRID_H))
    f.write(struct.pack('<ffff', min_x, max_x, min_z, max_z))
    f.write(hq.tobytes()); f.write(wq.tobytes())
import json as _json
_json.dump({'rivers': river_exits}, open(f'{OUTDIR}/nevishk_landscape.json', 'w'), indent=1)
print('river exits:', len(river_exits))
print('grid %dx%d  height range %.1f .. %.1f   water cells %d' % (GRID_W, GRID_H, hg.min(), hg.max(), wmask.sum()))

if PREVIEW:
    # Hillshade preview for checking the shapes.
    gy2, gx2 = np.gradient(h)
    shade = np.clip(0.55 + (-gx2 * 0.6 + gy2 * 0.6) / np.sqrt(1 + gx2 ** 2 + gy2 ** 2), 0, 1)
    pv = alb * shade[..., None]
    wm = ~np.isnan(water)
    pv[wm] = pv[wm] * 0.3 + col('#2E4A55') * 0.7
    Image.fromarray((np.clip(pv, 0, 1) * 255).astype(np.uint8)).resize((W * 2, H * 2)).save(PREVIEW)
