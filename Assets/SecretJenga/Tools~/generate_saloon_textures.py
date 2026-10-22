"""
THE LAST BLOCK - procedural textures for the old frontier saloon (no external images).

    python generate_saloon_textures.py <Assets/SecretJenga folder>

Writes into Textures/:
  T_Planks_Albedo/Normal       worn floor planks (horizontal, warm brown, nail heads, scuffs)
  T_WallBoards_Albedo/Normal   vertical wainscot boards (darker, reddish)
  T_SaloonPaper_Albedo         faded striped wallpaper for the upper walls
  T_Poster_Wanted_0..2         WANTED posters (names / rewards differ)
  T_Poster_Whiskey             painted whiskey sign
  T_Window_Dusk                desert dusk seen through a window (used emissive)
Fonts are taken from Fonts/ (Special Elite, IM Fell) when available.
"""
import os
import sys
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = sys.argv[1] if len(sys.argv) > 1 else "."
OUT = os.path.join(ROOT, "Textures")
FONTS = os.path.join(ROOT, "Fonts")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1881)


def save(arr, name):
    img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8))
    img.save(os.path.join(OUT, name), optimize=True)
    print("wrote", name)


def noise2d(h, w, scale, octaves=4, seed=0):
    r = np.random.default_rng(seed)
    out = np.zeros((h, w), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        s = max(2, int(scale * (2 ** o)))
        small = r.random((s + 1, s + 1)).astype(np.float32)
        img = Image.fromarray((small * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        out += np.asarray(img, np.float32) / 255.0 * amp
        total += amp
        amp *= 0.5
    return out / total


def wood_grain(h, w, along_x, seed, stretch=28.0):
    """Long, wavy grain lines running along x (or y)."""
    n = noise2d(h, w, 3, 4, seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    coord = yy if along_x else xx
    other = xx if along_x else yy
    rings = np.sin((coord / h * stretch + n * 6.0 + np.sin(other / w * 3.1) * 0.6) * math.pi * 2.0)
    return rings * 0.5 + 0.5, n


def normal_from_height(hgt, strength=3.0):
    gy, gx = np.gradient(hgt.astype(np.float32))
    nx, ny = -gx * strength, -gy * strength
    nz = np.ones_like(nx)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, ny / l, nz / l], -1)
    return (n * 0.5 + 0.5) * 255


def planks(name, size, n_planks, base, dark, horizontal, seed, nails=True):
    h = w = size
    img = np.zeros((h, w, 3), np.float32)
    height = np.ones((h, w), np.float32)
    plank = size / n_planks
    r = np.random.default_rng(seed)
    for i in range(n_planks):
        a, b = int(i * plank), int((i + 1) * plank)
        tone = r.uniform(0.78, 1.15)
        hue = np.array(base) * tone
        g, n = wood_grain(h, w, horizontal, seed + i * 17, stretch=r.uniform(18, 34))
        col = hue[None, None, :] * (0.82 + 0.18 * g[..., None]) * (0.9 + 0.2 * n[..., None])
        # occasional knots
        for _ in range(r.integers(0, 3)):
            cy, cx = r.uniform(a, b), r.uniform(0, w)
            if not horizontal:
                cy, cx = r.uniform(0, h), r.uniform(a, b)
            yy, xx = np.mgrid[0:h, 0:w]
            d = np.sqrt(((yy - cy) / 9.0) ** 2 + ((xx - cx) / 22.0 if horizontal else (xx - cx) / 9.0) ** 2)
            col *= (1 - 0.35 * np.exp(-d ** 2 * 0.5))[..., None]
        if horizontal:
            img[a:b, :, :] = col[a:b]
            height[a:a + 3, :] = 0.2
            height[b - 3:b, :] = 0.35
            img[a:a + 3, :, :] *= 0.35
            img[b - 2:b, :, :] *= 0.5
        else:
            img[:, a:b, :] = col[:, a:b]
            height[:, a:a + 3] = 0.2
            height[:, b - 3:b] = 0.35
            img[:, a:a + 3, :] *= 0.35
            img[:, b - 2:b, :] *= 0.5
        if nails:
            for k in range(4):
                pos = int((k + 0.5) / 4 * size + r.uniform(-12, 12))
                c = int((a + b) / 2 + r.uniform(-4, 4))
                y, x = (c, pos) if horizontal else (pos, c)
                yy, xx = np.ogrid[-4:5, -4:5]
                m = (yy ** 2 + xx ** 2) <= 9
                ys, xs = slice(max(0, y - 4), y + 5), slice(max(0, x - 4), x + 5)
                sub = img[ys, xs]
                mm = m[:sub.shape[0], :sub.shape[1]]
                sub[mm] = np.array(dark) * 0.6
                height[ys, xs][mm] = 0.9
    # wear and dirt
    dirt = noise2d(h, w, 6, 5, seed + 99)
    img *= (0.78 + 0.32 * dirt)[..., None]
    scuff = noise2d(h, w, 40, 2, seed + 5) > 0.7
    img[scuff] *= 1.08
    save(img, name + "_Albedo.png")
    hgt = height * 0.8 + wood_grain(h, w, horizontal, seed + 3)[0] * 0.08 + dirt * 0.12
    save(normal_from_height(hgt, 4.0), name + "_Normal.png")


def wallpaper(name, size=1024):
    h = w = size
    base = np.array([168, 128, 82], np.float32)
    stripe = np.array([120, 52, 40], np.float32)
    xx = np.arange(w)
    s = (np.sin(xx / w * math.pi * 2 * 8) > 0.55).astype(np.float32)
    thin = (np.abs(np.sin(xx / w * math.pi * 2 * 8 + 1.2)) < 0.05).astype(np.float32)
    col = base[None, :] * (1 - s[:, None]) + stripe[None, :] * s[:, None]
    col = col * (1 - 0.4 * thin[:, None])
    img = np.repeat(col[None, :, :], h, 0)
    # small diamond motif between stripes
    yy, xx2 = np.mgrid[0:h, 0:w]
    motif = (np.abs(((xx2 % 128) - 96)) + np.abs(((yy % 128) - 64)) < 10) & (s[None, :].repeat(h, 0) < 0.5)
    img[motif] *= 0.82
    fade = noise2d(h, w, 4, 5, 7)
    img = img * (0.62 + 0.45 * fade[..., None])
    # water stains from the top
    stain = noise2d(h, w, 2, 3, 9) * np.clip(1.4 - yy / h * 2.2, 0, 1)
    img *= (1 - 0.35 * stain)[..., None]
    save(img, name + "_Albedo.png")


def font(name, size):
    for f in (name, "SpecialElite-Regular.ttf", "IMFeENsc28P.ttf"):
        p = os.path.join(FONTS, f)
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def parchment(w, h, seed):
    n = noise2d(h, w, 5, 5, seed)
    base = np.array([214, 190, 140], np.float32)
    img = base[None, None, :] * (0.78 + 0.3 * n[..., None])
    yy, xx = np.mgrid[0:h, 0:w]
    edge = np.minimum(np.minimum(xx, w - xx), np.minimum(yy, h - yy)) / (min(w, h) * 0.12)
    img *= np.clip(0.55 + edge * 0.45, 0.55, 1.0)[..., None]
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))


def centered(d, y, text, f, fill):
    bb = d.textbbox((0, 0), text, font=f)
    d.text(((d.im.size[0] - (bb[2] - bb[0])) / 2, y), text, font=f, fill=fill)


def wanted(i, name, reward):
    w, h = 512, 720
    img = parchment(w, h, 30 + i)
    d = ImageDraw.Draw(img)
    ink = (48, 30, 18)
    centered(d, 26, "WANTED", font("IMFeENsc28P.ttf", 100), ink)
    centered(d, 150, "DEAD OR ALIVE", font("SpecialElite-Regular.ttf", 40), ink)
    # portrait frame with a hat-and-shoulders silhouette
    d.rectangle([120, 210, 392, 470], outline=ink, width=5)
    cx = 256
    d.ellipse([cx - 52, 280, cx + 52, 400], fill=(70, 48, 32))
    d.ellipse([cx - 100, 262, cx + 100, 300], fill=(60, 40, 26))          # hat brim
    d.rounded_rectangle([cx - 52, 222, cx + 52, 286], 18, fill=(60, 40, 26))  # crown
    d.pieslice([cx - 120, 390, cx + 120, 600], 180, 360, fill=(70, 48, 32))  # shoulders
    d.rectangle([121, 466, 391, 470], fill=ink)
    centered(d, 492, name, font("IMFeENsc28P.ttf", 44), ink)
    centered(d, 566, "REWARD", font("SpecialElite-Regular.ttf", 34), ink)
    centered(d, 606, reward, font("IMFeENsc28P.ttf", 76), (110, 26, 18))
    img = img.filter(ImageFilter.GaussianBlur(0.6))
    img.save(os.path.join(OUT, f"T_Poster_Wanted_{i}.png"), optimize=True)
    print("wrote", f"T_Poster_Wanted_{i}.png")


def whiskey_sign():
    w, h = 1024, 384
    n = noise2d(h, w, 4, 4, 77)
    base = np.array([70, 34, 22], np.float32)
    img = Image.fromarray(np.clip(base[None, None, :] * (0.7 + 0.5 * n[..., None]), 0, 255).astype(np.uint8))
    d = ImageDraw.Draw(img)
    gold = (222, 178, 92)
    d.rectangle([14, 14, w - 14, h - 14], outline=gold, width=8)
    centered(d, 40, "SALOON", font("IMFeENsc28P.ttf", 170), gold)
    centered(d, 250, "WHISKEY  -  CARDS  -  ROOMS", font("SpecialElite-Regular.ttf", 46), (230, 205, 150))
    img.save(os.path.join(OUT, "T_Poster_Whiskey.png"), optimize=True)
    print("wrote T_Poster_Whiskey.png")


def window_dusk():
    w, h = 512, 768
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    t = yy / h
    top = np.array([52, 40, 78], np.float32)
    mid = np.array([236, 120, 56], np.float32)
    low = np.array([255, 196, 110], np.float32)
    sky = np.where(t[..., None] < 0.55, top + (mid - top) * (t[..., None] / 0.55), mid + (low - mid) * ((t[..., None] - 0.55) / 0.45))
    # mesa silhouettes
    ridge = h * 0.68 + np.sin(xx[0] / w * 5.0) * 18 + np.sin(xx[0] / w * 13.0 + 1) * 8
    ridge[(xx[0] > w * 0.25) & (xx[0] < w * 0.55)] -= 70
    mask = yy > ridge[None, :]
    sky[mask] = np.array([60, 30, 24], np.float32)
    # a cactus
    c = (np.abs(xx - w * 0.78) < 9) & (yy > h * 0.6)
    c |= (np.abs(xx - w * 0.74) < 6) & (yy > h * 0.64) & (yy < h * 0.7)
    c |= (np.abs(yy - h * 0.7) < 6) & (xx > w * 0.74) & (xx < w * 0.78)
    sky[c] = np.array([40, 22, 18], np.float32)
    save(sky, "T_Window_Dusk.png")


planks("T_Planks", 1024, 8, [150, 100, 62], [40, 28, 20], True, 11)
planks("T_WallBoards", 1024, 6, [112, 62, 40], [30, 18, 12], False, 23, nails=True)
wallpaper("T_SaloonPaper")
for i, (n, r) in enumerate([("BLACK JACK KETCH", "$500"), ("ROSIE McGRAW", "$350"), ("THE TOWER KID", "$1000")]):
    wanted(i, n, r)
whiskey_sign()
window_dusk()


def light_shaft():
    """Soft dusty beam (white, alpha) for the sun shafts through the windows - tinted by the material."""
    w, h = 256, 512
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    across = np.clip(np.sin(np.clip(xx / (w - 1), 0, 1) * math.pi), 0, 1) ** 1.6
    along = np.clip(1.0 - yy / h, 0, 1) ** 0.8 * np.clip(yy / (h * 0.08), 0, 1)
    dust = 0.75 + 0.25 * noise2d(h, w, 12, 3, 5)
    a = across * along * dust
    rgba = np.zeros((h, w, 4), np.float32)
    rgba[..., :3] = 255
    rgba[..., 3] = a * 255
    Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, "T_LightShaft.png"), optimize=True)
    print("wrote T_LightShaft.png")


light_shaft()
