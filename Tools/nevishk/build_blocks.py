"""Scatter placeholder city blocks inside each district outline of the base map.
Output: nevishk_layout.json with blocks [u, v, width_px, depth_px, height, yaw] and trees [u, v, scale]."""
import json, sys
import numpy as np
from PIL import Image, ImageFilter, ImageDraw
from collections import deque

SP = '/tmp/claude-0/-home-user/f16b717f-43c8-5a61-bcf6-93fe88b9c84b/scratchpad/'
LORE = sys.argv[1]; OUT = sys.argv[2]
rng = np.random.RandomState(42)

im = np.array(Image.open(SP + 'dl/basemap.png').convert('RGB')).astype(int)
H, W, _ = im.shape
dark = im.sum(2) < 250
wall = np.array(Image.fromarray((dark * 255).astype('uint8')).filter(ImageFilter.MaxFilter(3))) > 0
r, g, b = im[..., 0], im[..., 1], im[..., 2]
white = (r > 230) & (g > 230) & (b > 230)
beige = (r > 200) & (g > 200) & (b > 140) & (b < 200) & (np.abs(r - g) < 30)
green = (g > 150) & (r < 90) & (b < 120)
blue = (b > 170) & (r < 120)

def flood(seed, passable):
    lab = np.zeros((H, W), bool)
    x, y = seed
    if not passable[y, x]: return lab
    q = deque([seed]); lab[y, x] = True
    while q:
        x, y = q.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            a, c = x + dx, y + dy
            if 0 <= a < W and 0 <= c < H and passable[c, a] and not lab[c, a]:
                lab[c, a] = True; q.append((a, c))
    return lab

free = ~wall
seeds = {'krasnyi_elektrovoi': (960, 115), 'baltag': (841, 277), 'mednye_luzhi': (640, 390),
         'nevishk_tech': (300, 250), 'loom_of_echoes': (470, 95), 'novyi_tsentr': (700, 180), 'imperial': (505, 240)}
masks = {k: flood(s, free) for k, s in seeds.items()}
masks['ushchelye'] = flood((420, 330), beige | (free & ~white & ~green & ~blue & ~dark))
masks['ushchelye'] &= beige
# Новые Камыши: small island with an open outline, so trace it by hand.
kam = Image.new('L', (W, H), 0)
ImageDraw.Draw(kam).polygon([(612, 84), (640, 76), (700, 70), (760, 82), (770, 100), (735, 108),
                             (700, 122), (660, 112), (618, 106)], fill=255)
masks['novye_kamyshi'] = (np.array(kam) > 0) & white

# Park (Аллея Бужорилор) for trees
park = flood((760, 470), green | blue)

lore = json.load(open(LORE, encoding='utf-8'))
pins = np.array([[l['uv'][0] * W, l['uv'][1] * H] for l in lore['landmarks']])

STYLE = {  # density (px^2 per block), footprint px range, height range (world units)
    'novyi_tsentr':       (260, (4, 9), (10, 34)),
    'imperial':           (180, (4, 8), (8, 18)),
    'mednye_luzhi':       (300, (4, 8), (8, 22)),
    'baltag':             (380, (5, 12), (4, 14)),
    'nevishk_tech':       (420, (6, 14), (6, 20)),
    'krasnyi_elektrovoi': (360, (5, 10), (4, 12)),
    'loom_of_echoes':     (360, (4, 9), (6, 18)),
    'novye_kamyshi':      (90,  (3, 6), (3, 7)),
    'ushchelye':          (900, (5, 8), (14, 21)),   # Безмолвные плиты: 5-7 floors, sparse
}

def clearance(mask, k):
    # erode mask so blocks stay off the outlines
    return np.array(Image.fromarray((mask * 255).astype('uint8')).filter(ImageFilter.MinFilter(k))) > 0

out = {'imageSize': [W, H], 'blocks': {}, 'trees': []}
for did, mask in masks.items():
    dens, (fmin, fmax), (hmin, hmax) = STYLE[did]
    inner = clearance(mask, 9)
    ys, xs = np.nonzero(inner)
    n = int(mask.sum() / dens)
    blocks, placed = [], []
    tries = 0
    while len(blocks) < n and tries < n * 40 and len(xs):
        tries += 1
        i = rng.randint(len(xs)); x, y = xs[i], ys[i]
        if len(pins) and np.min(np.hypot(pins[:, 0] - x, pins[:, 1] - y)) < 9: continue
        fw, fd = rng.randint(fmin, fmax + 1), rng.randint(fmin, fmax + 1)
        if placed and np.min([np.hypot(px - x, py - y) - (pr + max(fw, fd)) * 0.55 for px, py, pr in placed]) < 1.5:
            continue
        h = float(rng.uniform(hmin, hmax))
        if did == 'novyi_tsentr' and rng.rand() < 0.15: h *= 1.8
        yaw = float(rng.choice([0, 0, 90, rng.uniform(-25, 25)]))
        blocks.append([round(x / W, 5), round(y / H, 5), int(fw), int(fd), round(h, 1), round(yaw, 1)])
        placed.append((x, y, max(fw, fd)))
    out['blocks'][did] = blocks
    print(did, 'area', int(mask.sum()), 'blocks', len(blocks))

# trees: dense in Ущелье forest + park, sparse elsewhere along rivers is skipped
for mask, n in ((masks['ushchelye'], 260), (park & ~blue, 140)):
    ys, xs = np.nonzero(clearance(mask, 5))
    for _ in range(n):
        i = rng.randint(len(xs))
        out['trees'].append([round(xs[i] / W, 5), round(ys[i] / H, 5), round(float(rng.uniform(0.7, 1.5)), 2)])
print('trees', len(out['trees']))

# debug render
dbg = Image.open(SP + 'dl/basemap.png').convert('RGB'); d = ImageDraw.Draw(dbg)
for did, bl in out['blocks'].items():
    for u, v, fw, fd, h, yaw in bl:
        x, y = u * W, v * H; d.rectangle((x - fw / 2, y - fd / 2, x + fw / 2, y + fd / 2), fill=(int(255 - h * 5), 60, 60))
for u, v, s in out['trees']:
    x, y = u * W, v * H; d.ellipse((x - 2, y - 2, x + 2, y + 2), fill=(20, 120, 40))
dbg.resize((W * 2, H * 2)).save(SP + 'blocks_check.png')
json.dump(out, open(OUT, 'w'), separators=(',', ':'))
