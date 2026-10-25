"""
THE LAST BLOCK - kids'-room UI textures: notebook paper, sticky notes, washi tape, crayon strokes, a sticker badge,
a playing card. Same file names as the old paper-and-ink set, so the UI code does not change.
All crayon sprites are white so the UI can tint them.

Usage: python generate_ui_textures_kids.py <path-to-Assets/SecretJenga/Textures/UI>
Requires: numpy, pillow, scipy
"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.ndimage import gaussian_filter

OUT = sys.argv[1] if len(sys.argv) > 1 else "UI"
os.makedirs(OUT, exist_ok=True)
R = np.random.default_rng(77)


def save(name, img):
    if isinstance(img, np.ndarray):
        img = Image.fromarray(np.clip(img * 255, 0, 255).astype(np.uint8), "RGBA")
    img.save(os.path.join(OUT, name), optimize=True)
    print("wrote", name)


def rounded_mask(w, h, r, soft=1.5):
    m = Image.new("L", (w, h), 0)
    ImageDraw.Draw(m).rounded_rectangle([2, 2, w - 3, h - 3], r, fill=255)
    return np.asarray(m.filter(ImageFilter.GaussianBlur(soft)), np.float32) / 255


def noise(h, w, s=1.0, seed=0):
    r = np.random.default_rng(seed)
    n = gaussian_filter(r.random((h, w)).astype(np.float32), s)
    return (n - n.min()) / (n.max() - n.min() + 1e-6)


# ------------------------------------------------------------------ notebook paper (9-sliced, border 56)
# Plain sheet only: the ruled lines, margin and punch holes are drawn by UIFactory.PaperSheet at a fixed pitch,
# because a 9-slice stretches anything inside its centre and the lines would drift over the text.
w = h = 512
p = np.ones((h, w, 3), np.float32) * np.array([0.995, 0.99, 0.975])
p *= (0.975 + 0.03 * noise(h, w, 2, 1))[..., None]
img = Image.fromarray((p * 255).astype(np.uint8)).convert("RGBA")
a = rounded_mask(w, h, 22)
arr = np.asarray(img, np.float32) / 255
arr[..., 3] = a
save("UI_Paper.png", arr)

# ------------------------------------------------------------------ sticky note (yellow, slight curl shading)
w, h = 512, 160
yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
sn = np.ones((h, w, 3), np.float32) * np.array([1.0, 0.93, 0.55])
sn *= (0.97 + 0.04 * noise(h, w, 2, 2))[..., None]
sn *= (1.0 - 0.08 * (yy / h) ** 2)[..., None]                    # the bottom curls a little
sn[:18] *= np.array([0.96, 0.92, 0.8])                              # the glue strip
a = rounded_mask(w, h, 10)
save("UI_PaperSlip.png", np.dstack([sn, a]))

# ------------------------------------------------------------------ washi tape (colourful, translucent, zig-zag ends)
w, h = 220, 64
tape = np.zeros((h, w, 4), np.float32)
base = np.array([0.98, 0.62, 0.72])
stripe = np.array([1.0, 0.88, 0.92])
xx = np.arange(w)[None, :].repeat(h, 0)
yy = np.arange(h)[:, None].repeat(w, 1)
diag = ((xx + yy) // 14) % 2 == 0
tape[..., :3] = np.where(diag[..., None], base, stripe)
dots = ((xx % 28 - 14) ** 2 + (yy % 28 - 14) ** 2) < 16
tape[dots, :3] = np.array([1.0, 1.0, 1.0])
zig = (np.abs((yy % 10) - 5) * 1.2).astype(np.float32)
alpha = np.clip(np.minimum(xx - zig, (w - 1 - xx) - zig) / 2, 0, 1) * np.clip(np.minimum(yy, h - 1 - yy) / 2, 0, 1)
tape[..., 3] = alpha * 0.86
save("UI_Tape.png", tape)

# ------------------------------------------------------------------ crayon strokes (white, waxy gaps)
def crayon(w, h, seed, taper=True):
    r = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    center = h / 2 + gaussian_filter(r.normal(0, 1.5, w), 18)[None, :] * 1.5
    half = h * 0.36 * np.ones((1, w), np.float32)
    if taper:
        t = xx[0] / w
        half = half * np.clip(np.minimum(t, 1 - t) * 7, 0.35, 1)[None, :]
    body = np.clip((half - np.abs(yy - center)) / 1.5, 0, 1)
    wax = gaussian_filter(r.random((h, w)).astype(np.float32), (0.6, 2.5))
    wax = (wax - wax.min()) / (wax.max() - wax.min())
    a = body * np.clip(0.35 + wax * 1.1, 0, 1)
    return np.dstack([np.ones((h, w, 3), np.float32), a])

save("UI_InkStroke.png", crayon(512, 40, 31))
save("UI_TallyStroke.png", np.rot90(crayon(150, 26, 32)).copy())

# crayon circle (hand-drawn loop, two passes)
w, h = 420, 170
img = Image.new("L", (w, h), 0)
d = ImageDraw.Draw(img)
for k in range(2):
    pts = []
    for i in range(80):
        t = i / 79 * (2 * math.pi + 0.5)
        rx, ry = 190 + R.normal(0, 2) + k * 4, 70 + R.normal(0, 1.5) - k * 3
        pts.append((w / 2 + math.cos(t) * rx, h / 2 + math.sin(t) * ry + k * 2))
    d.line(pts, fill=255, width=9, joint="curve")
a = np.asarray(img, np.float32) / 255
a *= np.clip(0.4 + noise(h, w, 0.8, 5) * 1.0, 0, 1)
save("UI_InkCircle.png", np.dstack([np.ones((h, w, 3)), a]))

# sticker dot (round, soft edge, a little shine) - tinted with the player's colour
S = 96
yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
rr = np.sqrt((xx - S / 2) ** 2 + (yy - S / 2) ** 2)
a = np.clip((S * 0.45 - rr) / 1.5, 0, 1)
shine = np.clip(1 - np.sqrt((xx - S * 0.36) ** 2 + (yy - S * 0.32) ** 2) / (S * 0.18), 0, 1) ** 2
rgb = np.ones((S, S, 3), np.float32) * (0.88 + 0.12 * shine[..., None])
save("UI_InkBlot.png", np.dstack([rgb, a]))

# badge (crayon rounded rectangle, double line) - "PLAYED", "STANDING", "COLLAPSED"
w, h = 520, 190
img = Image.new("L", (w, h), 0)
d = ImageDraw.Draw(img)
d.rounded_rectangle([10, 10, w - 10, h - 10], 40, outline=255, width=11)
d.rounded_rectangle([26, 26, w - 26, h - 26], 30, outline=200, width=4)
a = np.asarray(img, np.float32) / 255
a *= np.clip(0.45 + noise(h, w, 0.7, 9) * 0.9, 0, 1)
save("UI_Stamp.png", np.dstack([np.ones((h, w, 3)), a]))

# ------------------------------------------------------------------ playing card (white, rounded, blue border, little stars)
h, w = 504, 360
card = Image.new("RGBA", (w, h), (0, 0, 0, 0))
d = ImageDraw.Draw(card)
d.rounded_rectangle([4, 4, w - 5, h - 5], 30, fill=(252, 251, 246, 255))
d.rounded_rectangle([18, 18, w - 19, h - 19], 22, outline=(80, 140, 230, 255), width=7)
d.rounded_rectangle([30, 30, w - 31, h - 31], 16, outline=(255, 200, 70, 255), width=3)
def star(cx, cy, r, col):
    pts = []
    for i in range(10):
        ang = -math.pi / 2 + i * math.pi / 5
        rr2 = r if i % 2 == 0 else r * 0.45
        pts.append((cx + math.cos(ang) * rr2, cy + math.sin(ang) * rr2))
    d.polygon(pts, fill=col)
for cx, cy in ((52, 52), (w - 52, 52), (52, h - 52), (w - 52, h - 52)):
    star(cx, cy, 13, (250, 120, 110, 255))
save("UI_CardStock.png", card.filter(ImageFilter.GaussianBlur(0.4)))
