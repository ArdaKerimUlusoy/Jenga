"""
THE LAST BLOCK - procedural textures for the underground room (tileable albedo + normal maps).
Writes into Assets/SecretJenga/Textures:
  T_Concrete_Albedo / T_Concrete_Normal         old poured concrete walls (form-tie holes, water stains)
  T_ConcreteFloor_Albedo / T_ConcreteFloor_Normal  dark, dusty, cracked floor
  T_TableWorn_Albedo / T_TableWorn_Normal       worn dark planks with scratches and rings
Usage:  python generate_horror_textures.py <path-to-Assets/SecretJenga/Textures>
Requires: numpy, pillow, scipy
"""
import sys, os, math
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, zoom

OUT = sys.argv[1] if len(sys.argv) > 1 else "Textures"
os.makedirs(OUT, exist_ok=True)
N = 1024


def fbm(h, w, octaves=6, base=4, seed=0):
    r = np.random.default_rng(seed)
    out = np.zeros((h, w), np.float32)
    amp, tot = 1.0, 0.0
    for o in range(octaves):
        g = base * 2 ** o
        grid = r.random((g, g)).astype(np.float32)
        grid = np.pad(grid, ((0, 1), (0, 1)), mode="wrap")
        layer = zoom(grid, ((h + h / g) / (g + 1), (w + w / g) / (g + 1)), order=3)[:h, :w]
        out += layer * amp
        tot += amp
        amp *= 0.5
    out /= tot
    return (out - out.min()) / (out.max() - out.min() + 1e-6)


def wrap_blur(x, s):
    return gaussian_filter(x, s, mode="wrap")


def normal_from_height(hm, strength=2.0):
    gy, gx = np.gradient(hm.astype(np.float32))
    nx, ny, nz = -gx * strength, -gy * strength, np.ones_like(hm)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / l, -ny / l, nz / l], -1)
    return Image.fromarray(((n * 0.5 + 0.5) * 255).clip(0, 255).astype(np.uint8), "RGB")


def save_rgb(name, rgb):
    Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, name), optimize=True)


def cracks(h, w, count, seed, width=1.2):
    r = np.random.default_rng(seed)
    m = np.zeros((h, w), np.float32)
    for _ in range(count):
        y, x = r.uniform(0, h), r.uniform(0, w)
        ang = r.uniform(0, 2 * math.pi)
        for _ in range(int(r.uniform(150, 480))):
            ang += r.normal(0, 0.1)
            y = (y + math.sin(ang) * 2.0) % h
            x = (x + math.cos(ang) * 2.0) % w
            m[int(y), int(x)] = 1.0
    return np.clip(wrap_blur(m, width) * 6, 0, 1)


# ------------------------------------------------------------------ Concrete walls
base = fbm(N, N, 7, 4, 1)
fine = fbm(N, N, 4, 64, 2)
pores = (np.random.default_rng(3).random((N, N)) > 0.985).astype(np.float32)
pores = np.clip(wrap_blur(pores, 0.8) * 3, 0, 1)
y = np.linspace(0, 1, N, dtype=np.float32)[:, None]
# vertical water stains running down
streak_seed = np.random.default_rng(4).random((1, N)).astype(np.float32)
streaks = wrap_blur(np.repeat(streak_seed, N, 0), (0, 6))
streaks = np.clip((streaks - 0.55) * 4, 0, 1) * (0.4 + 0.6 * fbm(N, N, 4, 3, 5))
# form-tie holes on a grid (typical of poured concrete)
ties = np.zeros((N, N), np.float32)
for gy in range(2):
    for gx in range(2):
        cy, cx = int((gy + 0.5) * N / 2), int((gx + 0.5) * N / 2)
        yy, xx = np.ogrid[:N, :N]
        d = np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2)
        ties = np.maximum(ties, np.clip(1 - d / 14, 0, 1))
seams = np.zeros((N, N), np.float32)
seams[N // 2 - 2:N // 2 + 2, :] = 1
seams = wrap_blur(seams, 1.5)
c = 0.42 + 0.22 * (base - 0.5) + 0.1 * (fine - 0.5)
c = c - 0.18 * streaks - 0.25 * pores - 0.3 * ties - 0.12 * seams
grime = fbm(N, N, 5, 2, 6)
rgb = np.stack([c * 0.98, c * 0.97, c * 0.95], -1)
rgb = rgb * (0.82 + 0.18 * grime[..., None]) * np.array([1.0, 0.97, 0.92])  # faint warm/brown grime
save_rgb("T_Concrete_Albedo.png", rgb)
normal_from_height(base * 0.6 + fine * 0.6 - pores * 0.8 - ties * 2.0 - seams * 0.8, 3.0).save(os.path.join(OUT, "T_Concrete_Normal.png"), optimize=True)

# ------------------------------------------------------------------ Concrete floor
base = fbm(N, N, 7, 3, 11)
fine = fbm(N, N, 4, 80, 12)
cr = cracks(N, N, 9, 13)
dust = fbm(N, N, 5, 2, 14)
c = 0.30 + 0.16 * (base - 0.5) + 0.06 * (fine - 0.5) - 0.2 * cr
c = c * (0.85 + 0.25 * dust)
rgb = np.stack([c, c * 0.97, c * 0.93], -1)
save_rgb("T_ConcreteFloor_Albedo.png", rgb)
normal_from_height(base * 0.5 + fine * 0.8 - cr * 1.5, 3.0).save(os.path.join(OUT, "T_ConcreteFloor_Normal.png"), optimize=True)

# ------------------------------------------------------------------ Worn table (planks along U)
r = np.random.default_rng(21)
yv = np.linspace(0, 1, N, dtype=np.float32)[:, None]
xv = np.linspace(0, 1, N, dtype=np.float32)[None, :]
n1 = fbm(N, N, 5, 3, 22)
rings = yv * 7 + 0.08 * (n1 - 0.5) * 6
grain = (np.sin(rings * 2 * math.pi * 9) * 0.5 + 0.5) ** 1.8
fib = wrap_blur(r.random((N, N)).astype(np.float32), (0.5, 22))
fib = (fib - fib.min()) / (fib.max() - fib.min() + 1e-6)
planks = np.zeros((N, N), np.float32)
for k in range(1, 6):
    planks[int(k * N / 6) - 2:int(k * N / 6) + 2, :] = 1
planks = wrap_blur(planks, 1.2)
scratches = np.zeros((N, N), np.float32)
for _ in range(220):
    y0, x0 = r.uniform(0, N), r.uniform(0, N)
    ang = r.normal(0, 0.6)
    length = r.uniform(20, 160)
    for s in np.linspace(0, length, int(length)):
        scratches[int(y0 + math.sin(ang) * s) % N, int(x0 + math.cos(ang) * s) % N] = r.uniform(0.4, 1.0)
scratches = wrap_blur(scratches, 0.7)
# cup rings / stains
stains = np.zeros((N, N), np.float32)
yy, xx = np.ogrid[:N, :N]
for _ in range(5):
    cy, cx, rad = r.uniform(0, N), r.uniform(0, N), r.uniform(40, 80)
    d = np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2)
    stains = np.maximum(stains, np.exp(-((d - rad) / 4) ** 2) * r.uniform(0.4, 0.8))
wear = fbm(N, N, 5, 2, 23)
t_ = np.clip(grain * 0.5 + fib * 0.35 + (n1 - 0.5) * 0.3, 0, 1)
dark = np.array([0.10, 0.065, 0.045])
mid = np.array([0.24, 0.16, 0.10])
rgb = dark + (mid - dark) * t_[..., None]
rgb = rgb * (1 - 0.55 * planks[..., None]) * (1 - 0.35 * stains[..., None])
rgb = rgb + 0.12 * scratches[..., None] * np.array([1.0, 0.85, 0.7])
rgb = rgb * (0.8 + 0.35 * wear[..., None])
save_rgb("T_TableWorn_Albedo.png", rgb)
normal_from_height(grain * 0.5 + fib * 0.4 - planks * 2.0 - scratches * 0.6, 2.5).save(os.path.join(OUT, "T_TableWorn_Normal.png"), optimize=True)
print("horror textures written to", OUT)
