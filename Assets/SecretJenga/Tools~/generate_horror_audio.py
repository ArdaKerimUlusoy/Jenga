"""
THE LAST BLOCK - procedural sound generator (room ambience, tension, darker UI/round sounds).
Writes 16-bit mono WAV files. The wooden block sounds from generate_audio.py are kept as they are.

Usage: python generate_horror_audio.py <path-to-Assets/SecretJenga/Audio>
Requires: numpy, scipy
"""
import sys, os, math
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, lfilter, fftconvolve

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else "Audio"
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1913)


def t(d):
    return np.arange(int(SR * d)) / SR


def bp(x, lo, hi, order=2):
    b, a = butter(order, [lo / (SR / 2), hi / (SR / 2)], btype="band")
    return lfilter(b, a, x)


def lp(x, f, order=2):
    b, a = butter(order, f / (SR / 2), btype="low")
    return lfilter(b, a, x)


def hp(x, f, order=2):
    b, a = butter(order, f / (SR / 2), btype="high")
    return lfilter(b, a, x)


def norm(x, peak):
    x = np.asarray(x, np.float64)
    return x / (np.max(np.abs(x)) + 1e-9) * peak


def save(name, x, peak=0.85):
    wavfile.write(os.path.join(OUT, name), SR, (norm(x, peak) * 32767).astype(np.int16))


def loopify(x, fade=0.5):
    """Crossfade the tail into the head so the clip loops without a click."""
    n = int(SR * fade)
    head, body, tail = x[:n], x[n:-n], x[-n:]
    w = np.linspace(0, 1, n)
    return np.concatenate([tail * (1 - w) + head * w, body])


def reverb(x, seconds=2.5, damp=3.0, wet=0.6, seed=0):
    r = np.random.default_rng(seed)
    tt = t(seconds)
    ir = r.standard_normal(len(tt)) * np.exp(-tt * damp)
    ir = lp(ir, 2500)
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    y = fftconvolve(x, ir)[: len(x) + len(tt)]
    dry = np.pad(x, (0, len(y) - len(x)))
    return dry * (1 - wet) + y * wet


def stick_slip(dur, rate, body_freqs, seed, jitter=0.35, sharp=0.002):
    """Wood creak: a train of friction 'slips' exciting a few body resonances."""
    r = np.random.default_rng(seed)
    n = int(SR * dur)
    exc = np.zeros(n)
    pos = 0.0
    while pos < dur:
        i = int(pos * SR)
        if i < n:
            exc[i] += r.uniform(0.5, 1.0)
        pos += (1.0 / rate(pos / dur)) * (1 + r.uniform(-jitter, jitter))
    k = np.exp(-t(0.03) / sharp)
    exc = np.convolve(exc, k)[:n]
    y = np.zeros(n)
    for f, q in body_freqs:
        y += bp(exc, f * (1 - 1 / q), f * (1 + 1 / q))
    return y


# ------------------------------------------------------------------ Drone bed (replaces the cheerful ambient loop)
L = 24.0
tt = t(L)
drone = np.zeros_like(tt)
for f, a in [(41.2, 1.0), (55.0, 0.6), (61.7, 0.35), (82.4, 0.25)]:
    for det in (-0.35, 0.0, 0.41):
        ff = f + det
        drone += a * (np.sin(2 * math.pi * ff * tt) + 0.35 * np.sin(2 * math.pi * 2 * ff * tt + 0.7))
drone *= 0.75 + 0.25 * np.sin(2 * math.pi * tt / 12.0)  # slow breathing of the bed
air = lp(bp(rng.standard_normal(len(tt)), 120, 900), 600) * (0.4 + 0.6 * (np.sin(2 * math.pi * tt / 8.0) * 0.5 + 0.5))
drone = lp(drone, 260) + 0.45 * air
save("MUS_Ambient_Loop.wav", loopify(drone, 1.5), 0.5)

# ------------------------------------------------------------------ Lamp hum (electrical, loop)
tt = t(4.0)
hum = sum(a * np.sin(2 * math.pi * f * tt) for f, a in [(50, 1.0), (100, 0.55), (150, 0.3), (200, 0.12)])
buzz = bp(rng.standard_normal(len(tt)), 2500, 6000) * (0.2 + 0.8 * (np.sin(2 * math.pi * 100 * tt) > 0.92))
save("SFX_LampHum_Loop.wav", loopify(hum + 0.25 * buzz, 0.3), 0.5)

# ------------------------------------------------------------------ Flicker zap
tt = t(0.7)
crackle = np.zeros_like(tt)
for _ in range(14):
    s = int(rng.uniform(0, 0.55) * SR)
    burst = hp(rng.standard_normal(int(SR * rng.uniform(0.004, 0.02))), 1800)
    crackle[s:s + len(burst)] += burst * rng.uniform(0.3, 1.0)
zap = crackle + 0.4 * np.sin(2 * math.pi * 100 * tt) * np.exp(-tt * 6)
save("SFX_Flicker.wav", zap * np.exp(-tt * 2.5), 0.6)

# ------------------------------------------------------------------ Heartbeat loop (~66 bpm)
beat = 60 / 66
tt = t(beat * 4)
hb = np.zeros_like(tt)
def thump(f, d, decay):
    x = t(d)
    return np.sin(2 * math.pi * f * x * (1 - 0.3 * x / d)) * np.exp(-x * decay)
for k in range(4):
    for off, amp in [(0.0, 1.0), (0.24, 0.7)]:
        s = int((k * beat + off) * SR)
        th = thump(48, 0.35, 14) * amp
        hb[s:s + len(th)] += th[: len(hb) - s]
save("SFX_Heartbeat_Loop.wav", lp(hb, 160), 0.8)

# ------------------------------------------------------------------ Collapse sting (boom + dissonant swell)
tt = t(4.0)
boom = lp(rng.standard_normal(len(tt)), 90) * np.exp(-tt * 1.6) * 3
cluster = sum(np.sin(2 * math.pi * f * tt) for f in (110, 116.5, 155.6, 164.8, 233.1))
cluster *= np.minimum(1, tt / 0.6) * np.exp(-tt * 0.9)
sting = reverb(boom + 0.35 * lp(cluster, 900), 3.0, 1.6, 0.5, seed=3)
save("SFX_CollapseSting.wav", sting, 0.9)

# ------------------------------------------------------------------ Chair creaks
for i, (d, base) in enumerate([(0.8, 380), (1.0, 300), (0.65, 460)]):
    c = stick_slip(d, lambda p: 18 + 40 * math.sin(p * math.pi), [(base, 6), (base * 2.3, 8), (base * 4.1, 10)], seed=40 + i)
    c *= np.minimum(1, t(d) / 0.05) * np.minimum(1, (d - t(d)) / 0.15)
    save(f"SFX_ChairCreak_{i}.wav", reverb(c, 0.8, 6, 0.25, seed=i), 0.55)

# ------------------------------------------------------------------ Distant metal (pipes, doors far away)
for i in range(3):
    r = np.random.default_rng(70 + i)
    d = 1.2
    tt = t(d)
    partials = [r.uniform(180, 260) * m for m in (1.0, 2.76, 5.4, 8.93)]
    clang = sum(np.sin(2 * math.pi * f * tt + r.uniform(0, 6)) * np.exp(-tt * (2.5 + j)) / (1 + j) for j, f in enumerate(partials))
    if i == 2:  # a slow scrape instead of a hit
        clang = stick_slip(d, lambda p: 25 + 20 * p, [(210, 12), (580, 14), (1240, 16)], seed=90) * np.exp(-tt * 1.2)
    clang = lp(clang, 1400)
    save(f"SFX_MetalDistant_{i}.wav", reverb(clang, 3.5, 1.3, 0.75, seed=10 + i), 0.6)

# ------------------------------------------------------------------ Wood strain loop (dangerous movement)
d = 3.0
strain = stick_slip(d, lambda p: 9 + 7 * math.sin(p * 2 * math.pi), [(160, 5), (340, 7), (720, 9)], seed=55, jitter=0.5, sharp=0.004)
strain += 0.4 * lp(rng.standard_normal(int(SR * d)), 120) * (0.5 + 0.5 * np.sin(2 * math.pi * t(d) / 1.5))
save("SFX_WoodStrain_Loop.wav", loopify(strain, 0.25), 0.6)

# ------------------------------------------------------------------ Darker UI / round sounds (same file names as before)
def soft_tone(freqs, d, decay, attack=0.01):
    x = t(d)
    s = sum(a * np.sin(2 * math.pi * f * x) for f, a in freqs)
    return s * np.minimum(1, x / attack) * np.exp(-x * decay)

def knock(f, d=0.25, decay=24, seed=0):
    r = np.random.default_rng(seed)
    x = t(d)
    s = np.sin(2 * math.pi * f * x) * np.exp(-x * decay) + 0.4 * np.sin(2 * math.pi * f * 2.3 * x) * np.exp(-x * decay * 1.6)
    n = bp(r.standard_normal(len(x)), 300, 2500) * np.exp(-x * 80) * 0.4
    return s + n

save("SFX_TurnStart.wav", reverb(soft_tone([(110, 1), (164.8, 0.5), (220, 0.25)], 1.6, 2.4, 0.04), 1.5, 2.5, 0.4), 0.5)
save("SFX_MissionComplete.wav", reverb(soft_tone([(196, 1), (293.7, 0.6), (392, 0.3)], 1.4, 2.2, 0.02), 1.5, 2.5, 0.45), 0.45)
def mix(*parts):
    n = max(len(p) for p in parts)
    return sum(np.pad(p, (0, n - len(p))) for p in parts)

save("SFX_CardPlayed.wav", reverb(mix(knock(140, 0.4, 14, 5), 0.3 * soft_tone([(233.1, 1), (246.9, 0.8)], 0.9, 3.5)), 1.2, 3, 0.4), 0.55)
save("SFX_Countdown.wav", reverb(knock(95, 0.35, 16, 6), 1.0, 3, 0.35), 0.5)
save("SFX_TimerTick.wav", mix(knock(1800, 0.06, 90, 7) * 0.6, knock(900, 0.06, 90, 8) * 0.3), 0.35)
save("SFX_Error.wav", lp(soft_tone([(70, 1), (73.5, 1)], 0.35, 7), 400) + 0.2 * bp(rng.standard_normal(int(SR * 0.35)), 200, 800) * np.exp(-t(0.35) * 9), 0.45)
win = soft_tone([(110, 1), (138.6, 0.6), (164.8, 0.6), (220, 0.35)], 4.0, 0.8, 0.8)
save("SFX_Win.wav", reverb(lp(win, 1200), 3.0, 1.5, 0.5), 0.55)
lose = soft_tone([(73.4, 1), (77.8, 0.8), (110, 0.4)], 3.5, 0.9, 0.05) + lp(rng.standard_normal(int(SR * 3.5)), 80) * np.exp(-t(3.5) * 2) * 2
save("SFX_Lose.wav", reverb(lose, 3.0, 1.4, 0.5), 0.8)
x = t(0.05)
save("UI_Hover.wav", np.sin(2 * math.pi * 520 * x) * np.exp(-x * 90), 0.18)
x = t(0.12)
save("UI_Click.wav", knock(420, 0.12, 50, 9), 0.4)
print("horror audio written to", OUT)
