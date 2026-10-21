"""
Secret Jenga - procedural texture generator.
Writes real PNG textures into Assets/SecretJenga/Textures (albedo + normal maps + UI icons).

Usage:  python generate_textures.py <path-to-Assets/SecretJenga/Textures>
Requires: numpy, pillow, scipy
"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy.ndimage import gaussian_filter, zoom

OUT = sys.argv[1] if len(sys.argv) > 1 else "Textures"
os.makedirs(OUT, exist_ok=True)
os.makedirs(os.path.join(OUT, "UI"), exist_ok=True)
rng = np.random.default_rng(1337)


def fbm(h, w, octaves=5, base=4, seed=0, tile=True):
    r = np.random.default_rng(seed)
    out = np.zeros((h, w), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        gh, gw = base * 2 ** o, base * 2 ** o
        g = r.random((gh, gw)).astype(np.float32)
        if tile:
            g = np.pad(g, ((0, 1), (0, 1)), mode="wrap")
            layer = zoom(g, ((h + h / gh) / (gh + 1), (w + w / gw) / (gw + 1)), order=3)[:h, :w]
        else:
            layer = zoom(g, (h / gh, w / gw), order=3)[:h, :w]
        out += layer * amp
        tot += amp
        amp *= 0.5
    out /= tot
    return (out - out.min()) / (out.max() - out.min() + 1e-6)


def normal_from_height(hm, strength=2.0):
    gy, gx = np.gradient(hm.astype(np.float32))
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(hm)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, -ny / l, nz / l], -1)  # Unity: green up (OpenGL style)
    return Image.fromarray(((n * 0.5 + 0.5) * 255).clip(0, 255).astype(np.uint8), "RGB")


def colorize(t, stops):
    """t in 0..1, stops list of (pos, (r,g,b))"""
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,), np.float32)
    for i in range(len(stops) - 1):
        p0, c0 = stops[i]
        p1, c1 = stops[i + 1]
        m = (t >= p0) & (t <= p1)
        f = ((t - p0) / max(p1 - p0, 1e-6))[..., None]
        out = np.where(m[..., None], np.array(c0) * (1 - f) + np.array(c1) * f, out)
    return out


def wood(h, w, seed, ring_freq, palette, warp=0.06, fiber=0.25, knots=0):
    """grain runs along X (U axis)."""
    y = np.linspace(0, 1, h, dtype=np.float32)[:, None]
    x = np.linspace(0, 1, w, dtype=np.float32)[None, :]
    n1 = fbm(h, w, 5, 3, seed)
    n2 = fbm(h, w, 4, 8, seed + 1)
    rings = y + warp * (n1 - 0.5) * 2 + 0.015 * np.sin(x * math.pi * 2 * 3 + n2 * 4)
    # knots
    r = np.random.default_rng(seed)
    for _ in range(knots):
        kx, ky, ks = r.random(), r.random(), 0.02 + r.random() * 0.03
        d = np.sqrt(((x - kx) * 3) ** 2 + (y - ky) ** 2)
        rings += 0.05 * np.exp(-(d / ks) ** 2) * np.sin(d * 140)
    g = (np.sin(rings * ring_freq * math.pi * 2) * 0.5 + 0.5) ** 1.6
    fib = gaussian_filter(r.random((h, w)).astype(np.float32), (0.4, 18), mode="wrap")
    fib = (fib - fib.min()) / (fib.max() - fib.min() + 1e-6)
    t = np.clip(g * 0.65 + fib * fiber + (n2 - 0.5) * 0.25, 0, 1)
    col = colorize(t, palette)
    height = g * 0.7 + fib * 0.3
    return col, height


def save_rgb(arr, name):
    Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, name))


# ---------------- Jenga block wood (light maple / beech) ----------------
maple = [(0.0, (0.93, 0.80, 0.60)), (0.45, (0.87, 0.71, 0.50)), (0.8, (0.76, 0.58, 0.38)), (1.0, (0.66, 0.48, 0.30))]
for i, seed in enumerate([11, 23, 37]):
    col, hgt = wood(512, 1024, seed, ring_freq=7 + i * 2, palette=maple, warp=0.05 + 0.02 * i, knots=i)
    save_rgb(col.transpose(1, 0, 2) if False else col, f"T_BlockWood_{i}_Albedo.png")
    if i == 0:
        normal_from_height(gaussian_filter(hgt, 0.7), 3.0).save(os.path.join(OUT, "T_BlockWood_Normal.png"))

# Painted mask-free colored blocks are tinted in material; also produce an end-grain texture
yy, xx = np.mgrid[0:256, 0:256] / 256.0
d = np.sqrt((xx - 0.2) ** 2 + (yy + 0.6) ** 2)
n = fbm(256, 256, 4, 4, 91)
ring = (np.sin((d + n * 0.04) * 90) * 0.5 + 0.5) ** 2
save_rgb(colorize(0.3 + ring * 0.5, maple), "T_BlockEndGrain_Albedo.png")

# ---------------- Table (walnut) ----------------
walnut = [(0.0, (0.36, 0.22, 0.13)), (0.4, (0.30, 0.18, 0.10)), (0.75, (0.22, 0.12, 0.07)), (1.0, (0.15, 0.08, 0.05))]
col, hgt = wood(1024, 1024, 77, ring_freq=10, palette=walnut, warp=0.09, fiber=0.35, knots=2)
# subtle plank seams across Y
seam = np.ones((1024, 1024), np.float32)
for k in range(1, 4):
    yk = int(k * 1024 / 4)
    seam[yk - 2:yk + 2, :] = 0.55
col *= seam[..., None]
hgt = hgt * seam
save_rgb(col, "T_TableWalnut_Albedo.png")
normal_from_height(gaussian_filter(hgt, 0.8), 3.5).save(os.path.join(OUT, "T_TableWalnut_Normal.png"))

# ---------------- Floor planks (oak) ----------------
oak = [(0.0, (0.62, 0.45, 0.28)), (0.5, (0.54, 0.38, 0.23)), (1.0, (0.40, 0.27, 0.16))]
H = W = 1024
floor = np.zeros((H, W, 3), np.float32)
fh = np.zeros((H, W), np.float32)
rows = 8
for r_ in range(rows):
    y0, y1 = r_ * H // rows, (r_ + 1) * H // rows
    off = rng.integers(0, W)
    c, h_ = wood(y1 - y0, W, 200 + r_, ring_freq=5, palette=oak, warp=0.08, knots=1)
    c = np.roll(c, off, axis=1) * (0.85 + 0.3 * rng.random())
    h_ = np.roll(h_, off, axis=1)
    # plank end joint
    jx = (off + W // 2) % W
    c[:, max(jx - 2, 0):jx + 2] *= 0.5
    h_[:, max(jx - 2, 0):jx + 2] = 0
    c[:3, :] *= 0.45
    h_[:3, :] = 0
    floor[y0:y1] = c
    fh[y0:y1] = h_
save_rgb(floor, "T_FloorOak_Albedo.png")
normal_from_height(gaussian_filter(fh, 0.8), 4.0).save(os.path.join(OUT, "T_FloorOak_Normal.png"))

# ---------------- Wallpaper (warm damask-ish stripes) ----------------
S = 512
yy, xx = np.mgrid[0:S, 0:S] / S
base = np.array([0.42, 0.24, 0.20])
stripe = (np.sin(xx * math.pi * 2 * 6) > 0.85).astype(np.float32)
motif = np.zeros((S, S), np.float32)
for cx in np.linspace(1 / 12, 11 / 12, 6):
    for cy in [0.25, 0.75]:
        dx, dy = (xx - cx) * 6, (yy - cy) * 3
        dd = np.sqrt(dx ** 2 + dy ** 2)
        ang = np.arctan2(dy, dx)
        petal = (dd < 0.22 + 0.06 * np.cos(ang * 6)).astype(np.float32)
        motif = np.maximum(motif, petal)
motif = gaussian_filter(motif, 1.0)
paper = fbm(S, S, 4, 16, 5)
wall = base[None, None, :] * (0.9 + paper[..., None] * 0.12)
wall = wall * (1 - stripe[..., None] * 0.18) + motif[..., None] * np.array([0.10, 0.06, 0.03])
save_rgb(wall, "T_Wallpaper_Albedo.png")

# ---------------- Rug ----------------
S = 1024
yy, xx = np.mgrid[0:S, 0:S] / S
cx, cy = np.abs(xx - 0.5) * 2, np.abs(yy - 0.5) * 2
border = np.maximum(cx, cy)
rug = np.zeros((S, S, 3), np.float32)
rug[:] = (0.45, 0.10, 0.10)
rug[(border > 0.82)] = (0.12, 0.14, 0.28)
rug[(border > 0.86) & (border < 0.90)] = (0.80, 0.62, 0.30)
rug[(border > 0.74) & (border < 0.76)] = (0.80, 0.62, 0.30)
diamond = (cx + cy)
rug[(diamond > 0.45) & (diamond < 0.5)] = (0.80, 0.62, 0.30)
rug[(diamond < 0.3)] = (0.12, 0.14, 0.28)
rug[(diamond < 0.15)] = (0.80, 0.62, 0.30)
pat = (np.sin(xx * 160) * np.sin(yy * 160) > 0.6) & (border < 0.7) & (diamond > 0.5)
rug[pat] *= 0.75
wool = gaussian_filter(np.random.default_rng(3).random((S, S)).astype(np.float32), 0.8)
rug *= (0.85 + wool[..., None] * 0.3)
save_rgb(rug, "T_Rug_Albedo.png")

# ---------------- UI icons (white glyphs on transparent, tinted in UI) ----------------
def icon(name, draw_fn, size=256):
    im = Image.new("RGBA", (size * 2, size * 2), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    draw_fn(d, size * 2)
    im = im.resize((size, size), Image.LANCZOS)
    im.save(os.path.join(OUT, "UI", name))

W_ = (255, 255, 255, 255)

def tower(d, s):  # Architect
    bw, bh = s * 0.5, s * 0.11
    for i in range(5):
        y = s * 0.82 - i * bh * 1.15
        if i % 2 == 0:
            d.rounded_rectangle([s * 0.25, y - bh, s * 0.75, y], 10, fill=W_)
        else:
            for k in range(3):
                x0 = s * 0.25 + k * s * 0.172
                d.rounded_rectangle([x0, y - bh, x0 + s * 0.15, y], 8, fill=W_)
    d.polygon([(s * 0.5, s * 0.08), (s * 0.66, s * 0.2), (s * 0.34, s * 0.2)], fill=W_)

def bolt(d, s):  # Speed
    d.polygon([(s*0.58,s*0.06),(s*0.24,s*0.56),(s*0.47,s*0.56),(s*0.38,s*0.94),(s*0.76,s*0.40),(s*0.53,s*0.40),(s*0.64,s*0.06)], fill=W_)

def drop(d, s):  # Color hunter (palette)
    d.ellipse([s*0.12,s*0.16,s*0.88,s*0.86], fill=W_)
    for (x,y) in [(0.33,0.36),(0.52,0.28),(0.69,0.42),(0.32,0.6)]:
        d.ellipse([s*(x-0.07),s*(y-0.07),s*(x+0.07),s*(y+0.07)], fill=(0,0,0,0))
    d.ellipse([s*0.55,s*0.58,s*0.75,s*0.78], fill=(0,0,0,0))

def shield(d, s):  # Guardian
    d.polygon([(s*0.5,s*0.06),(s*0.86,s*0.2),(s*0.82,s*0.55),(s*0.5,s*0.94),(s*0.18,s*0.55),(s*0.14,s*0.2)], fill=W_)
    d.polygon([(s*0.5,s*0.18),(s*0.74,s*0.28),(s*0.71,s*0.53),(s*0.5,s*0.8)], fill=(0,0,0,0))

def bomb(d, s):  # Saboteur
    d.ellipse([s*0.14,s*0.28,s*0.78,s*0.92], fill=W_)
    d.rectangle([s*0.52,s*0.22,s*0.68,s*0.36], fill=W_)
    d.line([(s*0.60,s*0.22),(s*0.70,s*0.10),(s*0.82,s*0.12)], fill=W_, width=int(s*0.04))
    for a in range(0, 360, 45):
        r0, r1 = s*0.05, s*0.11
        cx, cy = s*0.86, s*0.10
        d.line([(cx+r0*math.cos(math.radians(a)), cy+r0*math.sin(math.radians(a))),(cx+r1*math.cos(math.radians(a)), cy+r1*math.sin(math.radians(a)))], fill=W_, width=int(s*0.025))

def crown(d, s):  # Master
    d.polygon([(s*0.12,s*0.75),(s*0.16,s*0.28),(s*0.35,s*0.5),(s*0.5,s*0.18),(s*0.65,s*0.5),(s*0.84,s*0.28),(s*0.88,s*0.75)], fill=W_)
    d.rounded_rectangle([s*0.12,s*0.78,s*0.88,s*0.88], 12, fill=W_)
    for x in [0.16,0.5,0.84]:
        d.ellipse([s*(x-0.05),s*0.13 if x==0.5 else s*0.22,s*(x+0.05),s*0.23 if x==0.5 else s*0.32], fill=W_)

def wave(d, s):  # Risk taker
    pts = []
    for i in range(0, 101):
        t = i / 100
        pts.append((s*(0.08+0.84*t), s*(0.5 + 0.22*math.sin(t*math.pi*4)*(1-abs(t-0.5)))))
    d.line(pts, fill=W_, width=int(s*0.09), joint="curve")
    d.rectangle([s*0.44,s*0.06,s*0.56,s*0.26], fill=W_)

def star(d, s):
    pts=[]
    for i in range(10):
        r = s*0.44 if i%2==0 else s*0.19
        a = math.radians(-90 + i*36)
        pts.append((s*0.5+r*math.cos(a), s*0.52+r*math.sin(a)))
    d.polygon(pts, fill=W_)

def crown_small(d, s):
    crown(d, s)

def clock(d, s):
    d.ellipse([s*0.1,s*0.1,s*0.9,s*0.9], outline=W_, width=int(s*0.08))
    d.line([(s*0.5,s*0.5),(s*0.5,s*0.24)], fill=W_, width=int(s*0.07))
    d.line([(s*0.5,s*0.5),(s*0.68,s*0.6)], fill=W_, width=int(s*0.07))

def check(d, s):
    d.line([(s*0.18,s*0.52),(s*0.42,s*0.76),(s*0.84,s*0.26)], fill=W_, width=int(s*0.13), joint="curve")

def cross(d, s):
    d.line([(s*0.22,s*0.22),(s*0.78,s*0.78)], fill=W_, width=int(s*0.13))
    d.line([(s*0.78,s*0.22),(s*0.22,s*0.78)], fill=W_, width=int(s*0.13))

def copy_ic(d, s):
    d.rounded_rectangle([s*0.30,s*0.12,s*0.86,s*0.70], 20, outline=W_, width=int(s*0.07))
    d.rounded_rectangle([s*0.14,s*0.30,s*0.70,s*0.88], 20, fill=W_)

def gear(d, s):
    cx = cy = s*0.5
    for k in range(8):
        a = math.radians(k*45)
        d.line([(cx+s*0.2*math.cos(a), cy+s*0.2*math.sin(a)),(cx+s*0.42*math.cos(a), cy+s*0.42*math.sin(a))], fill=W_, width=int(s*0.14))
    d.ellipse([s*0.2,s*0.2,s*0.8,s*0.8], fill=W_)
    d.ellipse([s*0.38,s*0.38,s*0.62,s*0.62], fill=(0,0,0,0))

def refresh(d, s):
    d.arc([s*0.15,s*0.15,s*0.85,s*0.85], 30, 320, fill=W_, width=int(s*0.1))
    d.polygon([(s*0.82,s*0.18),(s*0.88,s*0.46),(s*0.60,s*0.38)], fill=W_)

def person(d, s):
    d.ellipse([s*0.32,s*0.10,s*0.68,s*0.46], fill=W_)
    d.pieslice([s*0.14,s*0.50,s*0.86,s*1.22], 180, 360, fill=W_)

def eye(d, s):
    d.ellipse([s*0.08,s*0.28,s*0.92,s*0.72], fill=W_)
    d.ellipse([s*0.34,s*0.30,s*0.66,s*0.70], fill=(0,0,0,0))
    d.ellipse([s*0.42,s*0.40,s*0.58,s*0.60], fill=W_)

icons = {
    "Icon_Architect.png": tower, "Icon_Speed.png": bolt, "Icon_ColorHunter.png": drop,
    "Icon_Guardian.png": shield, "Icon_Saboteur.png": bomb, "Icon_Master.png": crown,
    "Icon_RiskTaker.png": wave, "Icon_Star.png": star, "Icon_Crown.png": crown_small,
    "Icon_Clock.png": clock, "Icon_Check.png": check, "Icon_Cross.png": cross,
    "Icon_Copy.png": copy_ic, "Icon_Gear.png": gear, "Icon_Refresh.png": refresh,
    "Icon_Person.png": person, "Icon_Secret.png": eye,
}
for k, v in icons.items():
    icon(k, v)

# ---------------- UI panel / button sprites (9-slice, rounded) ----------------
def rounded(name, size, radius, fill, border=None, bw=0, shadow=False, gradient=None):
    s = size * 2
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    if gradient:
        g = Image.new("RGBA", (s, s))
        gd = ImageDraw.Draw(g)
        for y in range(s):
            t = y / (s - 1)
            c = tuple(int(gradient[0][i] * (1 - t) + gradient[1][i] * t) for i in range(4))
            gd.line([(0, y), (s, y)], fill=c)
        mask = Image.new("L", (s, s), 0)
        ImageDraw.Draw(mask).rounded_rectangle([0, 0, s - 1, s - 1], radius * 2, fill=255)
        im.paste(g, (0, 0), mask)
    else:
        d.rounded_rectangle([0, 0, s - 1, s - 1], radius * 2, fill=fill)
    if border:
        d.rounded_rectangle([bw, bw, s - 1 - bw, s - 1 - bw], radius * 2 - bw, outline=border, width=bw * 2)
    im = im.resize((size, size), Image.LANCZOS)
    im.save(os.path.join(OUT, "UI", name))

rounded("UI_Panel.png", 96, 24, None, border=(255, 255, 255, 40), bw=2, gradient=[(255, 255, 255, 255), (232, 232, 232, 255)])
rounded("UI_Button.png", 96, 22, None, gradient=[(255, 255, 255, 255), (215, 215, 215, 255)])
rounded("UI_Rounded.png", 64, 16, (255, 255, 255, 255))
rounded("UI_Pill.png", 64, 32, (255, 255, 255, 255))
# circle + soft glow
s = 256
im = Image.new("RGBA", (s, s), (0, 0, 0, 0)); ImageDraw.Draw(im).ellipse([2, 2, s - 3, s - 3], fill=(255, 255, 255, 255))
im.save(os.path.join(OUT, "UI", "UI_Circle.png"))
yy, xx = np.mgrid[0:s, 0:s]
r = np.sqrt((xx - s / 2) ** 2 + (yy - s / 2) ** 2) / (s / 2)
a = np.clip(1 - r, 0, 1) ** 2
glow = np.zeros((s, s, 4), np.uint8); glow[..., :3] = 255; glow[..., 3] = (a * 255).astype(np.uint8)
Image.fromarray(glow, "RGBA").save(os.path.join(OUT, "UI", "UI_Glow.png"))
# card frame for missions
rounded("UI_Card.png", 128, 28, None, border=(255, 215, 140, 255), bw=4, gradient=[(70, 46, 32, 255), (40, 26, 18, 255)])
# vignette
s = 512
yy, xx = np.mgrid[0:s, 0:s]
r = np.sqrt(((xx - s / 2) / (s / 2)) ** 2 + ((yy - s / 2) / (s / 2)) ** 2)
a = np.clip((r - 0.55) / 0.6, 0, 1) ** 1.5
vig = np.zeros((s, s, 4), np.uint8); vig[..., 3] = (a * 255).astype(np.uint8)
Image.fromarray(vig, "RGBA").save(os.path.join(OUT, "UI", "UI_Vignette.png"))
# soft particle (dust)
s = 128
yy, xx = np.mgrid[0:s, 0:s]
r = np.sqrt((xx - s / 2) ** 2 + (yy - s / 2) ** 2) / (s / 2)
n = fbm(s, s, 3, 4, 8)
a = np.clip(1 - r, 0, 1) ** 1.5 * (0.6 + 0.4 * n)
p = np.zeros((s, s, 4), np.uint8); p[..., :3] = 255; p[..., 3] = (a * 255).astype(np.uint8)
Image.fromarray(p, "RGBA").save(os.path.join(OUT, "T_DustParticle.png"))
print("textures written to", OUT)
