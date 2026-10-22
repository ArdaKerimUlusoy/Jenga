"""
THE LAST BLOCK - procedural textures for the kids' bedroom.

    python generate_kidsroom_textures.py <Assets/SecretJenga/Textures>      (numpy + scipy + pillow)

  T_KidWallpaper_Albedo.png   pastel sky with soft clouds and little stars (tiles)
  T_KidWallPaint_Albedo.png   brushed pastel paint for the lower walls (tiles)
  T_KidFloor_Albedo.png       light honey floorboards (tiles)
  T_RugRound_Albedo.png       round rainbow rug under the table
  T_PlayMat_Albedo.png        a town play mat with roads, houses and trees
  T_KidTable_Albedo.png       light birch table top covered in crayon doodles and stickers
  T_Drawing_0..3.png          kids' crayon drawings pinned on the walls
  T_PosterSpace.png           a planets poster
  T_Blanket_Albedo.png        patchwork quilt
  T_Window_Night.png          evening sky with a big moon, seen through the window
  T_GlowStar.png              glow-in-the-dark ceiling star (alpha)
"""
import os
import sys
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.ndimage import gaussian_filter, zoom

OUT = sys.argv[1] if len(sys.argv) > 1 else "Textures"
os.makedirs(OUT, exist_ok=True)
R = np.random.default_rng(1994)


def fbm(h, w, octaves=5, base=4, seed=0, tile=True):
    r = np.random.default_rng(seed)
    out = np.zeros((h, w), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        g = base * 2 ** o
        grid = r.random((g, g)).astype(np.float32)
        if tile:
            grid = np.pad(grid, ((0, 1), (0, 1)), mode="wrap")
        else:
            grid = r.random((g + 1, g + 1)).astype(np.float32)
        layer = zoom(grid, (h / g, w / g), order=3, mode="grid-wrap" if tile else "reflect")[:h, :w]
        out += layer * amp
        tot += amp
        amp *= 0.5
    out /= tot
    return (out - out.min()) / (out.max() - out.min() + 1e-6)


def save(img, name):
    if isinstance(img, np.ndarray):
        img = Image.fromarray(np.clip(img * 255, 0, 255).astype(np.uint8))
    img.save(os.path.join(OUT, name), optimize=True)
    print("wrote", name)


def crayon_line(d, pts, color, width, seed=0, passes=3):
    """Waxy crayon stroke: several jittered thin passes."""
    r = np.random.default_rng(seed)
    for p in range(passes):
        jit = [(x + r.normal(0, width * 0.25), y + r.normal(0, width * 0.25)) for x, y in pts]
        c = tuple(int(v) for v in color) + (int(170 + r.integers(0, 70)),)
        d.line(jit, fill=c, width=max(1, int(width * r.uniform(0.5, 0.9))), joint="curve")


def crayon_fill(d, poly, color, seed=0, spacing=5, angle=35, width=4):
    """Scribble-fill a polygon with crayon hatching."""
    r = np.random.default_rng(seed)
    xs = [p[0] for p in poly]
    ys = [p[1] for p in poly]
    mask = Image.new("L", d.im.size, 0)
    ImageDraw.Draw(mask).polygon(poly, fill=255)
    layer = Image.new("RGBA", d.im.size, (0, 0, 0, 0))
    ld = ImageDraw.Draw(layer)
    a = math.radians(angle)
    diag = max(max(xs) - min(xs), max(ys) - min(ys)) * 2
    cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
    k = -diag
    while k < diag:
        x0, y0 = cx + math.cos(a) * -diag - math.sin(a) * k, cy + math.sin(a) * -diag + math.cos(a) * k
        x1, y1 = cx + math.cos(a) * diag - math.sin(a) * k, cy + math.sin(a) * diag + math.cos(a) * k
        ld.line([(x0, y0), (x1, y1)], fill=tuple(int(v) for v in color) + (int(150 + r.integers(0, 90)),), width=width)
        k += spacing * r.uniform(0.7, 1.3)
    return layer, mask


def paste_fill(img, d, poly, color, seed=0, spacing=5, angle=35, width=4):
    layer, mask = crayon_fill(d, poly, color, seed, spacing, angle, width)
    img.paste(layer, (0, 0), Image.composite(layer, Image.new("RGBA", img.size, (0, 0, 0, 0)), mask))


def paper_bg(w, h, tint=(0.97, 0.96, 0.92), seed=0):
    n = fbm(h, w, 5, 4, seed, tile=False)
    a = np.ones((h, w, 3), np.float32) * np.array(tint)
    a *= (0.95 + 0.06 * n[..., None])
    return Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).convert("RGBA")


# ------------------------------------------------------------------ wallpaper: pastel sky, clouds, stars (tileable)
S = 512
n = fbm(S, S, 4, 4, 1)
wp = np.ones((S, S, 3), np.float32) * np.array([0.70, 0.83, 0.93])
wp *= (0.97 + 0.04 * n[..., None])
img = Image.fromarray((wp * 255).astype(np.uint8)).convert("RGBA")
d = ImageDraw.Draw(img)
def tiled(fn, x, y):
    for ox in (-S, 0, S):
        for oy in (-S, 0, S):
            fn(x + ox, y + oy)
for (cx, cy, sc) in [(110, 120, 1.0), (380, 300, 1.2), (250, 450, 0.8), (460, 70, 0.7)]:
    def cloud(x, y, sc=sc):
        for dx, dy, rr in [(-30, 6, 22), (0, -6, 30), (32, 4, 24), (-12, 14, 20), (16, 14, 22)]:
            d.ellipse([x + dx * sc - rr * sc, y + dy * sc - rr * sc, x + dx * sc + rr * sc, y + dy * sc + rr * sc], fill=(250, 252, 255, 255))
    tiled(cloud, cx, cy)
def star(x, y, r, col):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        rr = r if i % 2 == 0 else r * 0.45
        pts.append((x + math.cos(a) * rr, y + math.sin(a) * rr))
    d.polygon(pts, fill=col)
for i in range(16):
    x, y = R.uniform(0, S), R.uniform(0, S)
    col = [(255, 226, 120, 255), (255, 255, 255, 255), (255, 190, 200, 255)][i % 3]
    rr = R.uniform(6, 11)
    tiled(lambda a, b, rr=rr, col=col: star(a, b, rr, col), x, y)
save(img.convert("RGB"), "T_KidWallpaper_Albedo.png")

# ------------------------------------------------------------------ lower wall paint (pale mint, brushed)
n = fbm(S, S, 5, 6, 2)
br = gaussian_filter(R.random((S, S)).astype(np.float32), (0.6, 14), mode="wrap")
br = (br - br.min()) / (br.max() - br.min())
pt = np.ones((S, S, 3), np.float32) * np.array([0.80, 0.91, 0.84])
pt *= (0.96 + 0.04 * n[..., None]) * (0.98 + 0.03 * br[..., None])
save(pt, "T_KidWallPaint_Albedo.png")

# ------------------------------------------------------------------ floorboards: light honey wood (tileable, 6 boards)
H = W = 512
fl = np.zeros((H, W, 3), np.float32)
grain = gaussian_filter(R.random((H, W)).astype(np.float32), (0.7, 30), mode="wrap")
grain = (grain - grain.min()) / (grain.max() - grain.min())
nn = fbm(H, W, 4, 4, 3)
for b in range(6):
    y0, y1 = b * H // 6, (b + 1) * H // 6
    tint = np.array([0.86, 0.69, 0.48]) * R.uniform(0.92, 1.06)
    fl[y0:y1] = tint * (0.86 + 0.16 * grain[y0:y1, :, None]) * (0.95 + 0.07 * nn[y0:y1, :, None])
    fl[y0:y0 + 2] *= 0.72
    off = R.integers(0, W)
    fl[y0:y1, off:off + 2] *= 0.75
save(fl, "T_KidFloor_Albedo.png")

# ------------------------------------------------------------------ round rainbow rug
S2 = 1024
yy, xx = np.mgrid[0:S2, 0:S2].astype(np.float32)
rr = np.sqrt((xx - S2 / 2) ** 2 + (yy - S2 / 2) ** 2) / (S2 / 2)
fib = gaussian_filter(R.random((S2, S2)).astype(np.float32), 0.8)
fib = (fib - fib.min()) / (fib.max() - fib.min())
cols = np.array([[0.93, 0.36, 0.36], [0.98, 0.63, 0.30], [0.98, 0.86, 0.42], [0.55, 0.80, 0.50], [0.43, 0.68, 0.90], [0.64, 0.52, 0.85], [0.97, 0.95, 0.90]])
band = np.clip((rr * 7.0).astype(int), 0, 6)
rug = cols[6 - band]
rug = rug * (0.85 + 0.2 * fib[..., None])
# braided edge
edge = (rr > 0.94) & (rr < 1.0)
rug[edge] *= (0.75 + 0.25 * np.sin(np.arctan2(yy - S2 / 2, xx - S2 / 2) * 90)[edge, None])
save(rug, "T_RugRound_Albedo.png")

# ------------------------------------------------------------------ play mat: a little town from above
pm = Image.new("RGB", (S2, S2), (122, 186, 98))
d = ImageDraw.Draw(pm)
g = fbm(S2, S2, 4, 6, 4, tile=False)
pm = Image.fromarray(np.clip(np.asarray(pm, np.float32) * (0.92 + 0.12 * g[..., None]), 0, 255).astype(np.uint8))
d = ImageDraw.Draw(pm)
road = (110, 110, 118)
for y in (260, 700):
    d.rectangle([0, y - 52, S2, y + 52], fill=road)
    for x in range(0, S2, 90):
        d.rectangle([x, y - 5, x + 50, y + 5], fill=(245, 240, 220))
for x in (380, 820):
    d.rectangle([x - 52, 0, x + 52, S2], fill=road)
    for y in range(0, S2, 90):
        d.rectangle([x - 5, y, x + 5, y + 50], fill=(245, 240, 220))
for (x, y) in [(150, 120), (560, 120), (150, 480), (590, 480), (150, 880), (600, 870), (930, 470)]:
    c = [(230, 90, 80), (90, 140, 220), (240, 200, 80), (200, 120, 200)][int(R.integers(0, 4))]
    d.rectangle([x - 60, y - 45, x + 60, y + 45], fill=c, outline=(60, 50, 50), width=4)
    d.polygon([(x - 66, y - 45), (x + 66, y - 45), (x, y - 95)], fill=(170, 70, 60), outline=(60, 50, 50))
for _ in range(14):
    x, y = R.uniform(40, S2 - 40), R.uniform(40, S2 - 40)
    if min(abs(y - 260), abs(y - 700)) < 70 or min(abs(x - 380), abs(x - 820)) < 70:
        continue
    d.ellipse([x - 26, y - 26, x + 26, y + 26], fill=(60, 130, 60), outline=(40, 90, 40), width=3)
d.ellipse([440, 820, 760, 990], fill=(100, 170, 230), outline=(70, 120, 180), width=5)   # a pond
save(pm.filter(ImageFilter.GaussianBlur(0.6)), "T_PlayMat_Albedo.png")

# ------------------------------------------------------------------ kids' table top: birch + doodles + stickers
tb = np.zeros((S2, S2, 3), np.float32)
grain = gaussian_filter(R.random((S2, S2)).astype(np.float32), (0.8, 40))
grain = (grain - grain.min()) / (grain.max() - grain.min())
g2 = fbm(S2, S2, 5, 4, 5, tile=False)
tb[:] = np.array([0.90, 0.80, 0.64])
tb *= (0.88 + 0.12 * grain[..., None]) * (0.95 + 0.07 * g2[..., None])
img = Image.fromarray((np.clip(tb, 0, 1) * 255).astype(np.uint8)).convert("RGBA")
d = ImageDraw.Draw(img, "RGBA")
cc = [(220, 60, 60), (60, 110, 210), (60, 160, 80), (240, 170, 40), (150, 80, 190)]
# doodles only near the rim (the middle stays clean so the blocks read well)
for k in range(26):
    a = R.uniform(0, 2 * math.pi)
    rad = R.uniform(0.62, 0.9) * S2 / 2
    x, y = S2 / 2 + math.cos(a) * rad, S2 / 2 + math.sin(a) * rad
    col = cc[k % len(cc)]
    kind = k % 5
    if kind == 0:   # spiral
        pts = [(x + math.cos(t) * t * 3.2, y + math.sin(t) * t * 3.2) for t in np.linspace(0, 12, 60)]
        crayon_line(d, pts, col, 4, k)
    elif kind == 1:  # star
        pts = []
        for i in range(11):
            aa = -math.pi / 2 + i * math.pi / 5
            r2 = 26 if i % 2 == 0 else 11
            pts.append((x + math.cos(aa) * r2, y + math.sin(aa) * r2))
        crayon_line(d, pts, col, 4, k)
    elif kind == 2:  # smiley
        crayon_line(d, [(x + math.cos(t) * 24, y + math.sin(t) * 24) for t in np.linspace(0, 6.3, 30)], col, 4, k)
        d.ellipse([x - 9, y - 9, x - 4, y - 4], fill=col + (220,))
        d.ellipse([x + 4, y - 9, x + 9, y - 4], fill=col + (220,))
        crayon_line(d, [(x + math.cos(t) * 13, y + 2 + math.sin(t) * 10) for t in np.linspace(0.3, 2.8, 12)], col, 3, k + 1)
    elif kind == 3:  # zigzag
        pts = [(x - 40 + i * 10, y + (8 if i % 2 else -8)) for i in range(9)]
        crayon_line(d, pts, col, 4, k)
    else:            # sticker (round, glossy)
        d.ellipse([x - 22, y - 22, x + 22, y + 22], fill=(255, 255, 255, 255))
        d.ellipse([x - 18, y - 18, x + 18, y + 18], fill=col + (255,))
        d.ellipse([x - 10, y - 13, x - 2, y - 6], fill=(255, 255, 255, 160))
save(img.convert("RGB").filter(ImageFilter.GaussianBlur(0.5)), "T_KidTable_Albedo.png")

# ------------------------------------------------------------------ crayon drawings (512 x 384 paper)
def drawing(idx):
    w, h = 512, 384
    img = paper_bg(w, h, (0.98, 0.97, 0.93) if idx != 2 else (0.80, 0.86, 0.96), 20 + idx)
    d = ImageDraw.Draw(img, "RGBA")
    s = 30 + idx
    if idx == 0:   # house, sun, tree, grass
        paste_fill(img, d, [(0, 320), (512, 300), (512, 384), (0, 384)], (70, 170, 70), s)
        paste_fill(img, d, [(150, 190), (310, 190), (310, 320), (150, 320)], (230, 80, 70), s + 1)
        paste_fill(img, d, [(135, 195), (230, 110), (325, 195)], (150, 90, 60), s + 2, angle=-30)
        paste_fill(img, d, [(205, 250), (245, 250), (245, 320), (205, 320)], (120, 70, 40), s + 3)
        crayon_line(d, [(150, 190), (310, 190), (310, 320), (150, 320), (150, 190)], (60, 40, 40), 4, s)
        paste_fill(img, d, [(390, 40 + 50 * math.sin(a) ** 0 * 0 + 0) for a in [0]] + [(420 + 40 * math.cos(t), 80 + 40 * math.sin(t)) for t in np.linspace(0, 6.28, 20)], (250, 200, 40), s + 4)
        for k in range(10):
            a = k * math.pi / 5
            crayon_line(d, [(420 + 52 * math.cos(a), 80 + 52 * math.sin(a)), (420 + 74 * math.cos(a), 80 + 74 * math.sin(a))], (250, 180, 30), 4, s + k)
        paste_fill(img, d, [(60, 320), (75, 320), (75, 230), (60, 230)], (120, 70, 40), s + 5)
        paste_fill(img, d, [(67 + 45 * math.cos(t), 210 + 40 * math.sin(t)) for t in np.linspace(0, 6.28, 20)], (50, 140, 60), s + 6)
    elif idx == 1:  # family of stick figures holding hands
        paste_fill(img, d, [(0, 330), (512, 330), (512, 384), (0, 384)], (90, 180, 80), s)
        xs = [90, 190, 300, 400]
        hs = [150, 160, 105, 95]
        cols = [(60, 90, 200), (210, 60, 120), (240, 140, 40), (120, 70, 190)]
        for x, hh, c in zip(xs, hs, cols):
            top = 330 - hh
            crayon_line(d, [(x + 22 * math.cos(t), top + 22 * math.sin(t)) for t in np.linspace(0, 6.3, 24)], (40, 30, 30), 3, s + x)
            crayon_line(d, [(x, top + 22), (x, 330 - hh * 0.35)], c, 5, s)
            crayon_line(d, [(x, 330 - hh * 0.35), (x - 18, 330)], c, 5, s + 1)
            crayon_line(d, [(x, 330 - hh * 0.35), (x + 18, 330)], c, 5, s + 2)
            crayon_line(d, [(x - 42, top + 50), (x + 42, top + 50)], c, 5, s + 3)
            d.ellipse([x - 9, top - 6, x - 5, top - 2], fill=(30, 30, 30, 230))
            d.ellipse([x + 5, top - 6, x + 9, top - 2], fill=(30, 30, 30, 230))
            crayon_line(d, [(x + math.cos(t) * 10, top + 4 + math.sin(t) * 7) for t in np.linspace(0.3, 2.8, 10)], (200, 50, 50), 3, s + 4)
        for k in range(5):
            x0 = 40 + k * 100
            crayon_line(d, [(x0, 50), (x0 + 20, 30), (x0 + 40, 50)], (90, 120, 200), 3, s + 10 + k)
    elif idx == 2:  # rocket and stars on blue paper
        paste_fill(img, d, [(220, 300), (300, 300), (300, 140), (260, 70), (220, 140)], (240, 240, 245), s)
        crayon_line(d, [(220, 300), (300, 300), (300, 140), (260, 70), (220, 140), (220, 300)], (200, 60, 60), 5, s)
        paste_fill(img, d, [(220, 240), (185, 310), (220, 300)], (220, 60, 60), s + 1)
        paste_fill(img, d, [(300, 240), (335, 310), (300, 300)], (220, 60, 60), s + 2)
        crayon_line(d, [(260 + 18 * math.cos(t), 170 + 18 * math.sin(t)) for t in np.linspace(0, 6.3, 24)], (60, 120, 220), 5, s + 3)
        paste_fill(img, d, [(230, 305), (290, 305), (275, 370), (260, 340), (245, 370)], (250, 160, 40), s + 4)
        for k in range(12):
            x, y = R.uniform(30, 480), R.uniform(20, 360)
            if abs(x - 260) < 90:
                continue
            pts = []
            for i in range(11):
                aa = -math.pi / 2 + i * math.pi / 5
                r2 = 14 if i % 2 == 0 else 6
                pts.append((x + math.cos(aa) * r2, y + math.sin(aa) * r2))
            paste_fill(img, d, pts, (250, 220, 70), s + 10 + k, spacing=3, width=3)
    else:           # a cat
        paste_fill(img, d, [(256 + 90 * math.cos(t), 250 + 70 * math.sin(t)) for t in np.linspace(0, 6.28, 30)], (240, 150, 60), s)
        paste_fill(img, d, [(256 + 60 * math.cos(t), 140 + 55 * math.sin(t)) for t in np.linspace(0, 6.28, 30)], (240, 150, 60), s + 1)
        paste_fill(img, d, [(205, 115), (215, 55), (245, 95)], (240, 150, 60), s + 2)
        paste_fill(img, d, [(267, 95), (297, 55), (307, 115)], (240, 150, 60), s + 3)
        d.ellipse([228, 125, 242, 142], fill=(40, 120, 60, 240))
        d.ellipse([270, 125, 284, 142], fill=(40, 120, 60, 240))
        for sx in (-1, 1):
            for k in range(3):
                crayon_line(d, [(256 + sx * 20, 160 + k * 6), (256 + sx * 75, 150 + k * 12)], (60, 40, 30), 2, s + k)
        crayon_line(d, [(345, 260), (400, 220), (420, 150)], (240, 150, 60), 14, s + 7)
        crayon_line(d, [(252, 162), (256, 168), (260, 162)], (200, 70, 80), 3, s + 8)
    # a child's name scrawled in a corner + tape
    crayon_line(d, [(30, 360), (45, 345), (55, 362), (70, 344), (82, 360)], (80, 80, 160), 3, s + 40)
    return img.filter(ImageFilter.GaussianBlur(0.4)).convert("RGB")

for i in range(4):
    save(drawing(i), f"T_Drawing_{i}.png")

# ------------------------------------------------------------------ planets poster (384 x 512)
w, h = 384, 512
pst = Image.new("RGB", (w, h), (24, 26, 64))
pa = np.asarray(pst, np.float32)
gg = fbm(h, w, 5, 3, 7, tile=False)
pa = pa * (0.8 + 0.4 * gg[..., None]) + np.array([20, 10, 40]) * gg[..., None]
pst = Image.fromarray(np.clip(pa, 0, 255).astype(np.uint8))
d = ImageDraw.Draw(pst)
for _ in range(120):
    x, y = R.uniform(0, w), R.uniform(0, h)
    r0 = R.uniform(0.5, 1.8)
    d.ellipse([x - r0, y - r0, x + r0, y + r0], fill=(255, 255, 240))
planets = [(70, 90, 40, (250, 190, 70)), (290, 140, 26, (220, 110, 80)), (150, 250, 52, (90, 150, 230)), (290, 330, 36, (230, 200, 140)), (90, 420, 30, (160, 120, 200))]
for x, y, r0, c in planets:
    d.ellipse([x - r0, y - r0, x + r0, y + r0], fill=c)
    d.ellipse([x - r0 * 0.6, y - r0 * 0.7, x - r0 * 0.1, y - r0 * 0.3], fill=tuple(min(255, v + 40) for v in c))
d.arc([290 - 60, 330 - 16, 290 + 60, 330 + 16], 160, 380, fill=(250, 230, 180), width=5)
d.rectangle([0, 470, w, h], fill=(250, 210, 80))
for k, x in enumerate(range(40, 340, 40)):
    d.ellipse([x, 482, x + 18, 500], fill=[(220, 70, 60), (60, 110, 210), (60, 160, 80)][k % 3])
save(pst.filter(ImageFilter.GaussianBlur(0.5)), "T_PosterSpace.png")

# ------------------------------------------------------------------ patchwork blanket (tileable)
q = Image.new("RGB", (S, S))
d = ImageDraw.Draw(q)
pal = [(236, 120, 120), (250, 200, 110), (140, 200, 150), (120, 170, 230), (200, 160, 220), (245, 240, 225)]
cell = S // 4
for i in range(4):
    for j in range(4):
        c = pal[int(R.integers(0, len(pal)))]
        d.rectangle([i * cell, j * cell, (i + 1) * cell, (j + 1) * cell], fill=c)
        if (i + j) % 2 == 0:
            for k in range(4):
                d.ellipse([i * cell + 18 + k * 28, j * cell + 50, i * cell + 30 + k * 28, j * cell + 62], fill=(255, 255, 255))
        for k in range(0, cell, 12):
            d.line([(i * cell + k, j * cell + 4), (i * cell + k + 6, j * cell + 4)], fill=(255, 255, 255), width=2)
qa = np.asarray(q, np.float32) / 255
qa *= (0.9 + 0.12 * fbm(S, S, 4, 8, 9)[..., None])
save(qa, "T_Blanket_Albedo.png")

# ------------------------------------------------------------------ night window: deep blue evening, moon, stars, rooftops
w, h = 512, 720
yy = np.linspace(0, 1, h)[:, None]
sky = np.zeros((h, w, 3), np.float32)
top, bot = np.array([0.10, 0.12, 0.32]), np.array([0.45, 0.42, 0.62])
sky[:] = top * (1 - yy[..., None] * 0) + 0
sky = top + (bot - top) * (yy[..., None] ** 1.4) * np.ones((1, w, 1))
img = Image.fromarray((np.clip(sky, 0, 1) * 255).astype(np.uint8))
d = ImageDraw.Draw(img)
for _ in range(90):
    x, y = R.uniform(0, w), R.uniform(0, h * 0.6)
    r0 = R.uniform(0.6, 1.8)
    d.ellipse([x - r0, y - r0, x + r0, y + r0], fill=(255, 250, 220))
mx, my = 340, 170
glow = Image.new("L", (w, h), 0)
ImageDraw.Draw(glow).ellipse([mx - 120, my - 120, mx + 120, my + 120], fill=120)
glow = glow.filter(ImageFilter.GaussianBlur(50))
img = Image.composite(Image.new("RGB", (w, h), (230, 225, 200)), img, glow)
d = ImageDraw.Draw(img)
d.ellipse([mx - 60, my - 60, mx + 60, my + 60], fill=(250, 246, 220))
d.ellipse([mx - 25, my - 30, mx - 5, my - 10], fill=(232, 228, 200))
d.ellipse([mx + 15, my + 10, mx + 40, my + 35], fill=(232, 228, 200))
# rooftops with lit windows
for k, x in enumerate(range(-20, w, 110)):
    rh = int(R.uniform(170, 260))
    d.rectangle([x, h - rh, x + 100, h], fill=(28, 26, 48))
    d.polygon([(x - 6, h - rh), (x + 50, h - rh - 50), (x + 106, h - rh)], fill=(36, 30, 52))
    for wy in range(h - rh + 30, h - 30, 60):
        for wx in (x + 18, x + 62):
            if R.random() < 0.55:
                d.rectangle([wx, wy, wx + 20, wy + 28], fill=(255, 210, 120))
save(img, "T_Window_Night.png")

# ------------------------------------------------------------------ glow-in-the-dark star (alpha)
S3 = 128
st = Image.new("RGBA", (S3, S3), (0, 0, 0, 0))
d = ImageDraw.Draw(st)
pts = []
for i in range(10):
    a = -math.pi / 2 + i * math.pi / 5
    r0 = 58 if i % 2 == 0 else 25
    pts.append((S3 / 2 + math.cos(a) * r0, S3 / 2 + math.sin(a) * r0))
d.polygon(pts, fill=(255, 255, 255, 255))
st = st.filter(ImageFilter.GaussianBlur(1.2))
save(st, "T_GlowStar.png")

# ------------------------------------------------------------------ the lit hallway seen through the open bedroom door (portrait)
w, h = 320, 600
hw = Image.new("RGB", (w, h), (0, 0, 0))
d = ImageDraw.Draw(hw)
vx, vy = w * 0.52, h * 0.46                      # vanishing point
# far wall, side walls, floor, ceiling (one-point perspective)
far = [vx - 48, vy - 80, vx + 48, vy + 90]
d.polygon([(0, 0), (w, 0), (far[2], far[1]), (far[0], far[1])], fill=(222, 196, 150))       # ceiling
d.polygon([(0, h), (w, h), (far[2], far[3]), (far[0], far[3])], fill=(150, 104, 66))        # floor
d.polygon([(0, 0), (far[0], far[1]), (far[0], far[3]), (0, h)], fill=(236, 214, 170))       # left wall
d.polygon([(w, 0), (far[2], far[1]), (far[2], far[3]), (w, h)], fill=(214, 188, 142))       # right wall
d.rectangle(far, fill=(246, 230, 190))
d.rectangle([vx - 18, vy - 30, vx + 18, vy + 90], fill=(170, 120, 80))                        # a far door
for k in range(1, 7):                                                                          # floorboard lines
    t = k / 7
    d.line([(w * t, h), (far[0] + (far[2] - far[0]) * t, far[3])], fill=(130, 88, 54), width=2)
# a ceiling light glow
glow = Image.new("L", (w, h), 0)
ImageDraw.Draw(glow).ellipse([vx - 70, 20, vx + 70, 110], fill=200)
glow = glow.filter(ImageFilter.GaussianBlur(30))
hw = Image.composite(Image.new("RGB", (w, h), (255, 244, 210)), hw, glow)
d = ImageDraw.Draw(hw)
d.ellipse([vx - 16, 52, vx + 16, 70], fill=(255, 252, 235))
# a picture frame on the left wall
d.polygon([(40, 210), (95, 225), (95, 300), (40, 310)], fill=(90, 60, 40))
d.polygon([(46, 218), (89, 230), (89, 295), (46, 303)], fill=(120, 170, 210))
save(hw.filter(ImageFilter.GaussianBlur(0.8)), "T_Hallway.png")
