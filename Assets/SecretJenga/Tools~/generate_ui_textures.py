"""
THE LAST BLOCK - hand-made looking UI textures (paper, tape, ink, card stock, stamp, card symbols).
All ink sprites are white so the UI can tint them (red ink / bone white).
Usage: python generate_ui_textures.py <path-to-Assets/SecretJenga/Textures/UI>
Requires: numpy, pillow, scipy
"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.ndimage import gaussian_filter, zoom

OUT = sys.argv[1] if len(sys.argv) > 1 else "UI"
os.makedirs(OUT, exist_ok=True)
R = np.random.default_rng(404)


def fbm(h, w, octaves=5, base=3, seed=0):
    r = np.random.default_rng(seed)
    out = np.zeros((h, w), np.float32)
    amp = tot = 0.0
    amp = 1.0
    for o in range(octaves):
        g = base * 2 ** o
        grid = r.random((g + 1, g + 1)).astype(np.float32)
        layer = zoom(grid, (h / (g + 1) * 1.0001, w / (g + 1) * 1.0001), order=3)[:h, :w]
        out += layer * amp
        tot += amp
        amp *= 0.5
    out /= tot
    return (out - out.min()) / (out.max() - out.min() + 1e-6)


def save(name, rgba):
    Image.fromarray(np.clip(rgba * 255, 0, 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, name), optimize=True)


def torn_mask(h, w, margin, seed, rough=1.0):
    """Alpha mask of a sheet with rough, slightly torn edges."""
    r = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy])
    n = fbm(h, w, 6, 6, seed) - 0.5
    fine = gaussian_filter(r.random((h, w)).astype(np.float32), 1.2) - 0.5
    edge = d - margin * 0.55 + n * margin * 0.9 * rough + fine * 6 * rough
    return np.clip(edge / 2.5, 0, 1)


def paper(h, w, base, seed, margin=26, stains=True):
    n1 = fbm(h, w, 6, 3, seed)
    n2 = fbm(h, w, 4, 24, seed + 1)
    fibers = gaussian_filter(np.random.default_rng(seed + 2).random((h, w)).astype(np.float32), (0.6, 3.0))
    fibers = (fibers - fibers.min()) / (fibers.max() - fibers.min())
    col = np.ones((h, w, 3), np.float32) * np.array(base, np.float32)
    col *= (0.9 + 0.12 * n1[..., None]) * (0.96 + 0.06 * fibers[..., None]) * (0.97 + 0.04 * n2[..., None])
    if stains:
        r = np.random.default_rng(seed + 3)
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        for _ in range(2):
            # keep stains inside the 9-slice center so they never smear along the borders
            cy, cx, rad = r.uniform(0.32 * h, 0.68 * h), r.uniform(0.32 * w, 0.68 * w), r.uniform(min(h, w) * 0.07, min(h, w) * 0.15)
            dd = np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2)
            ring = np.exp(-((dd - rad) / (rad * 0.05)) ** 2) * 0.045 + np.exp(-(dd / rad) ** 2) * 0.06
            col *= (1 - ring[..., None] * np.array([0.5, 0.75, 1.1]))
    # darker, yellowed toward the edges
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    d = np.minimum.reduce([xx, yy, w - 1 - xx, h - 1 - yy]) / (min(h, w) * 0.5)
    col *= (0.82 + 0.18 * np.clip(d * 3, 0, 1))[..., None] * np.array([1.0, 0.97, 0.9]) ** (1 - np.clip(d * 3, 0, 1))[..., None]
    a = torn_mask(h, w, margin, seed + 4)
    return np.dstack([np.clip(col, 0, 1), a])


# ------------------------------------------------------------------ paper sheet (9-sliced) and card stock
save("UI_Paper.png", paper(512, 512, (0.90, 0.86, 0.76), 1, margin=26))
save("UI_PaperSlip.png", paper(160, 512, (0.86, 0.82, 0.72), 7, margin=14, stains=False))

h, w = 504, 360
cs = paper(h, w, (0.88, 0.83, 0.72), 11, margin=10)
im = Image.fromarray((cs * 255).astype(np.uint8), "RGBA")
dr = ImageDraw.Draw(im)
ink = (58, 36, 30, 235)
for inset, width in ((22, 3), (31, 1)):
    jitter = lambda v: v + R.normal(0, 0.6)
    pts = [(inset, inset), (w - inset, inset), (w - inset, h - inset), (inset, h - inset), (inset, inset)]
    dr.line([(jitter(x), jitter(y)) for x, y in pts], fill=ink, width=width)
# corner ornaments
for cx, cy in ((inset := 31, inset), (w - inset, inset), (w - inset, h - inset), (inset, h - inset)):
    dr.ellipse((cx - 5, cy - 5, cx + 5, cy + 5), outline=ink, width=2)
im = im.filter(ImageFilter.GaussianBlur(0.4))
im.save(os.path.join(OUT, "UI_CardStock.png"), optimize=True)

# ------------------------------------------------------------------ masking tape
h, w = 64, 220
tape = np.zeros((h, w, 4), np.float32)
n = fbm(h, w, 4, 8, 21)
tape[..., :3] = np.array([0.86, 0.80, 0.64]) * (0.9 + 0.1 * n[..., None])
yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
jag = (np.random.default_rng(22).random(h) * 10).astype(np.float32)
jag = gaussian_filter(jag, 0.7)
left = xx - jag[:, None]
right = (w - 1 - xx) - jag[::-1][:, None]
a = np.clip(np.minimum(left, right) / 2, 0, 1) * np.clip(np.minimum(yy, h - 1 - yy) / 2, 0, 1)
tape[..., 3] = a * (0.78 + 0.12 * n)
save("UI_Tape.png", tape)

# ------------------------------------------------------------------ ink: brush stroke, tally stroke, scribble circle, blot
def brush(w, h, seed, taper=True, dry=0.25):
    r = np.random.default_rng(seed)
    img = np.zeros((h, w), np.float32)
    xs = np.arange(w)
    center = h / 2 + gaussian_filter(r.normal(0, 1, w), 30) * h * 0.08
    thick = h * 0.3 * (0.6 + 0.8 * gaussian_filter(r.random(w), 9) ** 1.5 * 2.2)
    if taper:
        t = xs / (w - 1)
        thick *= np.clip(np.minimum(t, 1 - t) * 14, 0.35, 1) ** 0.6
    yy = np.arange(h)[:, None]
    edge_noise = gaussian_filter(r.normal(0, 1, (h, w)), 1.0) * 1.6
    img = np.clip((thick[None, :] - np.abs(yy - center[None, :]) + edge_noise) / 1.2, 0, 1)
    streaks = gaussian_filter(r.random((h, w)), (0.5, 12))
    streaks = (streaks - streaks.min()) / (streaks.max() - streaks.min())
    img *= np.clip(1 - dry + streaks * dry * 2.2, 0, 1)
    return img

def white_alpha(a):
    return np.dstack([np.ones_like(a), np.ones_like(a), np.ones_like(a), np.clip(a, 0, 1)])

save("UI_InkStroke.png", white_alpha(brush(512, 40, 31, True, 0.45)))
save("UI_TallyStroke.png", white_alpha(np.rot90(brush(150, 26, 32, True, 0.5))))

w, h = 420, 170
img = Image.new("L", (w, h), 0)
d = ImageDraw.Draw(img)
r = np.random.default_rng(41)
pts = []
for i in range(220):
    t = i / 200 * 2 * math.pi * 1.12 + 0.3
    rx, ry = w * 0.44 + r.normal(0, 1.5), h * 0.38 + r.normal(0, 1.2)
    pts.append((w / 2 + math.cos(t) * rx * (1 + 0.04 * math.sin(3 * t)), h / 2 + math.sin(t) * ry - 4 * t / 6))
for k in range(len(pts) - 1):
    d.line([pts[k], pts[k + 1]], fill=int(200 + 55 * math.sin(k * 0.07)), width=5)
img = img.filter(ImageFilter.GaussianBlur(0.8))
save("UI_InkCircle.png", white_alpha(np.asarray(img, np.float32) / 255))

w = 96
yy, xx = np.mgrid[0:w, 0:w].astype(np.float32)
dd = np.sqrt((xx - w / 2) ** 2 + (yy - w / 2) ** 2)
ang = np.arctan2(yy - w / 2, xx - w / 2)
rad = w * 0.3 * (1 + 0.12 * np.sin(ang * 5 + 1) + 0.06 * np.sin(ang * 11))
save("UI_InkBlot.png", white_alpha(np.clip((rad - dd) / 2, 0, 1)))

# ------------------------------------------------------------------ rubber stamp frame (text is drawn by the UI on top)
w, h = 520, 190
img = Image.new("L", (w, h), 0)
d = ImageDraw.Draw(img)
d.rounded_rectangle((10, 10, w - 10, h - 10), radius=14, outline=255, width=10)
d.rounded_rectangle((26, 26, w - 26, h - 26), radius=8, outline=255, width=3)
a = np.asarray(img, np.float32) / 255
grunge = fbm(h, w, 5, 10, 51)
speck = gaussian_filter(np.random.default_rng(52).random((h, w)), 0.8)
a *= np.clip((grunge - 0.25) * 2.2, 0, 1) * np.clip((speck - 0.35) * 4, 0.2, 1)
save("UI_Stamp.png", white_alpha(a))

# ------------------------------------------------------------------ card symbols (white ink, drawn by hand-ish strokes)
def symbol(name, draw_fn, seed):
    S = 256
    img = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(img)
    rr = np.random.default_rng(seed)
    def line(p, q, wdt=7):
        steps = 18
        pts = [(p[0] + (q[0] - p[0]) * i / steps + rr.normal(0, 0.8), p[1] + (q[1] - p[1]) * i / steps + rr.normal(0, 0.8)) for i in range(steps + 1)]
        d.line(pts, fill=255, width=wdt, joint="curve")
    def ellipse(box, wdt=7):
        cx, cy, rx, ry = (box[0] + box[2]) / 2, (box[1] + box[3]) / 2, (box[2] - box[0]) / 2, (box[3] - box[1]) / 2
        pts = [(cx + math.cos(t) * rx + rr.normal(0, 0.7), cy + math.sin(t) * ry + rr.normal(0, 0.7)) for t in np.linspace(0, 2 * math.pi * 1.04, 80)]
        d.line(pts, fill=255, width=wdt, joint="curve")
    draw_fn(d, line, ellipse)
    img = img.filter(ImageFilter.GaussianBlur(0.9))
    save(name, white_alpha(np.asarray(img, np.float32) / 255))

def steady(d, line, ellipse):  # a spirit level: still hands
    line((40, 128), (216, 128), 9)
    ellipse((96, 104, 160, 152), 7)
    line((128, 96), (128, 160), 4)
    for x in (60, 196):
        line((x, 112), (x, 144), 6)

def pressure(d, line, ellipse):  # an eye that watches the next player
    pts_top = [(40 + i * 8.8, 128 - math.sin(i / 20 * math.pi) * 52) for i in range(21)]
    pts_bot = [(40 + i * 8.8, 128 + math.sin(i / 20 * math.pi) * 52) for i in range(21)]
    d.line(pts_top, fill=255, width=8, joint="curve")
    d.line(pts_bot, fill=255, width=8, joint="curve")
    ellipse((100, 100, 156, 156), 7)
    d.ellipse((118, 118, 138, 138), fill=255)
    for i in range(5):
        a = math.pi * (0.15 + 0.175 * i)
        line((128 - math.cos(a) * 70, 128 - math.sin(a) * 70 - 6), (128 - math.cos(a) * 92, 128 - math.sin(a) * 92 - 6), 5)

def second(d, line, ellipse):  # hourglass
    line((72, 40), (184, 40), 9)
    line((72, 216), (184, 216), 9)
    line((84, 44), (128, 126), 7)
    line((172, 44), (128, 126), 7)
    line((128, 130), (84, 212), 7)
    line((128, 130), (172, 212), 7)
    d.polygon([(104, 212), (152, 212), (128, 176)], fill=255)

symbol("Icon_Card_SteadyGrip.png", steady, 61)
symbol("Icon_Card_Pressure.png", pressure, 62)
symbol("Icon_Card_SecondChance.png", second, 63)
print("UI textures written to", OUT)
