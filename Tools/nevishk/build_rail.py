"""Trace the railway (ЖД) drawn on the base map and save it as a polyline.

The rail is a thin line with cross-ticks. We find the cheapest route between its two ends,
where thin ink is cheap, thick outlines are dearer and blank paper is expensive; the ticks are
side branches the route ignores. The route is simplified, smoothed and extended past both ends
so the line runs off into the countryside.

Usage: python build_rail.py <basemap.png> <rail_out.json> [debug.png] [clean_map.png]
If clean_map.png is given (the output of build_models.py), the drawn rail is erased from it in place,
since the railway is now built in 3D. Run build_terrain.py afterwards.
Output: {"points": [[u, v], ...]} in map UV (0..1 over the drawn map; may go beyond at the ends).
"""
import json, sys
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage as ndi
from skimage.graph import route_through_array

BASEMAP, OUT = sys.argv[1], sys.argv[2]
DEBUG = sys.argv[3] if len(sys.argv) > 3 and sys.argv[3] != '-' else None
CLEAN = sys.argv[4] if len(sys.argv) > 4 else None

im = np.asarray(Image.open(BASEMAP).convert('RGB')).astype(np.int32)
H, W, _ = im.shape
r, g, b = im[..., 0], im[..., 1], im[..., 2]
blue = (b > 170) & (r < 120)
green = (g > 140) & (r < 90) & (b < 120)
ink = (im.sum(2) < 560) & ~blue & ~green            # includes the rail's lighter grey strokes
thick = ndi.binary_opening(ink, structure=np.ones((3, 3)), iterations=1)
thin = ink & ~thick

cost = np.full((H, W), 60.0)
cost[thick] = 8.0
cost[thin] = 1.0

# Ends of the drawn line (base-map px), snapped to the nearest thin-ink pixel.
ENDS = [(262, 67), (1034, 499)]
ys, xs = np.nonzero(thin)
def snap(p):
    i = np.argmin((xs - p[0]) ** 2 + (ys - p[1]) ** 2)
    return int(ys[i]), int(xs[i])
start, end = snap(ENDS[0]), snap(ENDS[1])
path, total = route_through_array(cost, start, end, fully_connected=True, geometric=True)
path = np.array([(x, y) for y, x in path], np.float32)
on_thin = np.mean([thin[int(y), int(x)] for x, y in path])
print('raw path: %d px, %.0f%% on the drawn line' % (len(path), on_thin * 100))

# Simplify (Ramer–Douglas–Peucker) then smooth with a moving average.
def rdp(pts, eps):
    if len(pts) < 3: return pts
    a, b_ = pts[0], pts[-1]
    ab = b_ - a; n = np.hypot(*ab) + 1e-9
    d = np.abs(ab[0] * (pts[:, 1] - a[1]) - ab[1] * (pts[:, 0] - a[0])) / n
    i = int(np.argmax(d))
    if d[i] > eps:
        return np.vstack([rdp(pts[:i + 1], eps)[:-1], rdp(pts[i:], eps)])
    return np.vstack([a, b_])
simple = rdp(path, 1.5)

def resample(pts, step):
    seg = np.hypot(*np.diff(pts, axis=0).T); s = np.concatenate([[0], np.cumsum(seg)])
    t = np.arange(0, s[-1], step)
    return np.stack([np.interp(t, s, pts[:, 0]), np.interp(t, s, pts[:, 1])], 1)
dense = resample(simple, 2.0)
k = 9
pad = np.vstack([np.repeat(dense[:1], k, 0), dense, np.repeat(dense[-1:], k, 0)])
smooth = np.stack([np.convolve(pad[:, i], np.ones(2 * k + 1) / (2 * k + 1), 'valid') for i in (0, 1)], 1)
smooth[0], smooth[-1] = dense[0], dense[-1]

# Extend straight on past both ends, out into the countryside (the terrain is padded by 200 px).
def extend(pts, length):
    d0 = pts[0] - pts[min(15, len(pts) - 1)]; d0 /= np.hypot(*d0)
    d1 = pts[-1] - pts[-min(16, len(pts))]; d1 /= np.hypot(*d1)
    head = [pts[0] + d0 * s for s in np.arange(length, 0, -8)]
    tail = [pts[-1] + d1 * s for s in np.arange(8, length + 1, 8)]
    return np.vstack([head, pts, tail])
full = extend(smooth[::2], 260)

pts = [[round(float(x) / W, 5), round(float(y) / H, 5)] for x, y in full]
json.dump({'points': pts, 'drawn': [[round(float(x) / W, 5), round(float(y) / H, 5)] for x, y in path[::2]]},
          open(OUT, 'w'), separators=(',', ':'))
print('polyline points:', len(pts))

if DEBUG:
    dbg = Image.fromarray(im.astype(np.uint8)).convert('RGB')
    d = ImageDraw.Draw(dbg)
    d.line([tuple(p) for p in full], fill=(230, 30, 30), width=2)
    for p in ENDS: d.ellipse((p[0] - 4, p[1] - 4, p[0] + 4, p[1] + 4), outline=(0, 120, 255), width=2)
    dbg.resize((W * 2, H * 2)).save(DEBUG)

if CLEAN:
    # Erase the drawn rail (thin strokes and ticks within 7 px of the traced route) from the clean map,
    # keeping thick district outlines it runs beside.
    cm = np.asarray(Image.open(CLEAN).convert('RGB')).astype(np.int32)
    near = np.zeros((H, W), bool)
    for x, y in path.astype(int):
        near[max(y - 7, 0):y + 8, max(x - 7, 0):x + 8] = True
    c_ink = (cm.sum(2) < 600) & ~((cm[..., 2] > 170) & (cm[..., 0] < 120))
    # Protect the district outlines: every black stroke that is part of a long outline in the original map,
    # minus the rail's own thin pixels.
    black = im.sum(2) < 250
    lab, _ = ndi.label(black, structure=np.ones((3, 3)))
    outline = np.zeros((H, W), bool)
    for i, sl in enumerate(ndi.find_objects(lab), 1):
        if max(sl[0].stop - sl[0].start, sl[1].stop - sl[1].start) > 140:
            outline |= lab == i
    rail_line = np.zeros((H, W), bool)
    for x, y in path.astype(int):
        rail_line[max(y - 1, 0):y + 2, max(x - 1, 0):x + 2] = True
    protect = ndi.binary_dilation(outline & ndi.binary_opening(black, structure=np.ones((2, 2))), iterations=1)
    erase = near & c_ink & ~protect
    keep = ~erase & ~(near & c_ink & ~protect)
    idx = ndi.distance_transform_edt(~keep, return_distances=False, return_indices=True)
    Image.fromarray(cm[idx[0], idx[1]].astype(np.uint8)).save(CLEAN, optimize=True)
    print('erased rail px from clean map:', int(erase.sum()))
