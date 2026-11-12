"""Cleans the owner's transparent DentalKeep logo into a NEW file '<name> v2.png' in the same folder (the original is never written).
Keeps the large solid letter shapes, drops small isolated islands, keeps semi-transparent pixels only as the letters' anti-aliased edge (within 2 px),
and recolours every kept pixel to its letter's own colour (removes the cyan/red fringe)."""
import os, sys
from collections import deque
from PIL import Image

SRC = r"C:\Users\firas\Desktop\Epic\dentalKeep logo.png"
DST = r"C:\Users\firas\Desktop\Epic\dentalKeep logo v2.png"
if os.path.exists(DST):
    sys.exit("refusing to overwrite " + DST)

im = Image.open(SRC).convert("RGBA")
w, h = im.size
px = im.load()
SOLID = 200
MIN_AREA = 600          # the smallest real letter part is far larger; specks are far smaller

# 1. connected components of solid pixels (4-neighbour)
solid = [[px[x, y][3] >= SOLID for x in range(w)] for y in range(h)]
label = [[0] * w for _ in range(h)]
keep = set()
n = 0
areas = []
for y in range(h):
    for x in range(w):
        if solid[y][x] and not label[y][x]:
            n += 1
            q = deque([(x, y)]); label[y][x] = n; cnt = 0
            while q:
                cx, cy = q.popleft(); cnt += 1
                for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                    if 0 <= nx < w and 0 <= ny < h and solid[ny][nx] and not label[ny][nx]:
                        label[ny][nx] = n; q.append((nx, ny))
            areas.append(cnt)
            if cnt >= MIN_AREA:
                keep.add(n)

# letter colours: median of kept solid pixels split by hue (teal: g > r, coral: r > g)
teal, coral = [], []
for y in range(0, h, 2):
    for x in range(0, w, 2):
        if label[y][x] in keep:
            r, g, b, _ = px[x, y]
            (teal if g > r else coral).append((r, g, b))
med = lambda xs: tuple(sorted(c[i] for c in xs)[len(xs) // 2] for i in range(3))
TEAL, CORAL = med(teal), med(coral)

# 2. distance (<= 2 px) to a kept letter pixel, and which colour family it is
near = [[None] * w for _ in range(h)]
for y in range(h):
    for x in range(w):
        if label[y][x] in keep:
            r, g, _, _ = px[x, y]
            fam = TEAL if g > r else CORAL
            for dy in range(-2, 3):
                for dx in range(-2, 3):
                    nx, ny = x + dx, y + dy
                    if 0 <= nx < w and 0 <= ny < h and near[ny][nx] is None:
                        near[ny][nx] = fam

out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
op = out.load()
kept_edge = dropped = 0
for y in range(h):
    for x in range(w):
        r, g, b, a = px[x, y]
        if a == 0:
            continue
        if label[y][x] in keep:
            op[x, y] = (*(TEAL if g > r else CORAL), a)
        elif a < SOLID and near[y][x] is not None:
            op[x, y] = (*near[y][x], a); kept_edge += 1
        else:
            dropped += 1

out.save(DST, optimize=True)
print(f"components {n}, kept {len(keep)} (areas {sorted(a for a in areas if a >= MIN_AREA)}), largest dropped {max([a for a in areas if a < MIN_AREA] or [0])}")
print(f"teal {TEAL} coral {CORAL}; edge pixels kept {kept_edge}; speck pixels removed {dropped}")
print("written", DST, os.path.getsize(DST), "bytes")
