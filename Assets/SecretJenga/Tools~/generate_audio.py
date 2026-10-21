"""
Secret Jenga - procedural sound generator. Writes 16-bit mono WAV files.
Usage: python generate_audio.py <path-to-Assets/SecretJenga/Audio>
Requires: numpy, scipy
"""
import sys, os, math
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, lfilter

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else "Audio"
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7)


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


def save(name, x, peak=0.85):
    x = np.asarray(x, np.float64)
    m = np.max(np.abs(x)) + 1e-9
    x = x / m * peak
    wavfile.write(os.path.join(OUT, name), SR, (x * 32767).astype(np.int16))


def knock(freqs, dur=0.25, decay=28, noise=0.35, seed=0):
    r = np.random.default_rng(seed)
    tt = t(dur)
    s = np.zeros_like(tt)
    for i, f in enumerate(freqs):
        s += np.sin(2 * math.pi * f * tt + r.random() * 6) * np.exp(-tt * decay * (1 + i * 0.6)) / (1 + i)
    n = bp(r.standard_normal(len(tt)), 800, 5000) * np.exp(-tt * 90) * noise
    env = np.minimum(1, tt / 0.0015)
    return (s + n) * env


# Block collisions (wooden knocks)
save("SFX_BlockHit_0.wav", knock([740, 1630, 2950], 0.22, 30, seed=1))
save("SFX_BlockHit_1.wav", knock([620, 1420, 2600], 0.24, 26, seed=2))
save("SFX_BlockHit_2.wav", knock([880, 1910, 3400], 0.2, 34, seed=3))

# Block place (deeper thunk)
pl = knock([310, 690, 1250], 0.35, 18, 0.5, seed=4)
pl += 0.5 * np.concatenate([np.zeros(int(SR * 0.05)), knock([420, 980], 0.3, 30, seed=5)])[: len(pl)]
save("SFX_BlockPlace.wav", pl)

# Block slide loop (friction noise, loopable 1.2 s)
d = 1.2
n = rng.standard_normal(int(SR * d))
grain = bp(n, 300, 2600) * (0.6 + 0.4 * np.abs(lp(rng.standard_normal(len(n)), 18)) * 6)
grain += 0.3 * bp(n, 3000, 7000)
fade = int(SR * 0.05)
grain[:fade] *= np.linspace(0, 1, fade)
grain[-fade:] *= np.linspace(1, 0, fade)
save("SFX_BlockSlide_Loop.wav", grain, 0.6)

# Block grab (soft tick + scrape start)
g = knock([1200, 2600], 0.12, 50, 0.6, seed=9)
save("SFX_BlockGrab.wav", g, 0.6)

# Tower collapse: many knocks over 2.6 s + low rumble
D = 2.8
col = np.zeros(int(SR * D))
for i in range(70):
    st = int(SR * (0.02 + (rng.random() ** 1.6) * 2.0))
    k = knock([rng.uniform(380, 1100), rng.uniform(1200, 2600), rng.uniform(2600, 4000)], 0.3, rng.uniform(18, 40), 0.5, seed=100 + i)
    k *= rng.uniform(0.25, 1.0) * (1.2 - st / len(col))
    col[st:st + len(k)] += k[: len(col) - st]
rum = lp(rng.standard_normal(len(col)), 160) * np.exp(-t(D) * 1.5) * 6
col += rum
save("SFX_TowerCollapse.wav", col, 0.95)

# UI hover / click
tt = t(0.06)
save("UI_Hover.wav", np.sin(2 * math.pi * (1800 + 600 * tt / 0.06) * tt) * np.exp(-tt * 70), 0.35)
tt = t(0.12)
click = np.sin(2 * math.pi * 900 * tt) * np.exp(-tt * 45) + 0.5 * np.sin(2 * math.pi * 1350 * tt) * np.exp(-tt * 60)
save("UI_Click.wav", click, 0.6)


def bell(f, dur, decay=4.0):
    tt = t(dur)
    s = sum(a * np.sin(2 * math.pi * f * m * tt) * np.exp(-tt * decay * m ** 0.5) for m, a in [(1, 1), (2.01, 0.5), (3.02, 0.25), (4.17, 0.12)])
    return s * np.minimum(1, tt / 0.004)


def seq(notes, step, dur=1.2, decay=4.0):
    total = int(SR * (step * len(notes) + dur))
    out = np.zeros(total)
    for i, n_ in enumerate(notes):
        if n_ is None:
            continue
        f = 440 * 2 ** ((n_ - 69) / 12)
        b = bell(f, dur, decay)
        s0 = int(SR * step * i)
        out[s0:s0 + len(b)] += b
    return out

save("SFX_MissionComplete.wav", seq([76, 79, 84, 88], 0.08, 1.0, 5))
save("SFX_TurnStart.wav", seq([72, 79], 0.12, 0.9, 5), 0.6)
save("SFX_TimerTick.wav", knock([2200, 4400], 0.08, 60, 0.2, seed=33), 0.4)
w = seq([60, 64, 67, 72, None, 67, 72, 76, 79, 84], 0.11, 1.6, 3)
bass = 0.6 * seq([48, None, None, None, None, 55, None, None, None, 60], 0.11, 2.0, 2)
n_ = max(len(w), len(bass))
w = np.pad(w, (0, n_ - len(w))) + np.pad(bass, (0, n_ - len(bass)))
save("SFX_Win.wav", w)
save("SFX_Lose.wav", seq([67, 63, 60, 55], 0.22, 1.6, 2.5))
save("SFX_Countdown.wav", seq([67], 0.1, 0.5, 8), 0.5)
save("SFX_Error.wav", seq([58, 55], 0.09, 0.4, 9), 0.5)

# Ambient music loop: warm pad progression, 16 s loop (Cmaj7 - Am7 - Fmaj7 - G6)
L = 16.0
tt = t(L)
music = np.zeros_like(tt)
chords = [[48, 55, 64, 71], [45, 52, 60, 67], [41, 48, 57, 64], [43, 50, 59, 64]]
seg = L / 4
for ci, ch in enumerate(chords):
    s0, s1 = int(SR * ci * seg), int(SR * (ci + 1) * seg)
    ts = tt[: s1 - s0]
    env = np.minimum(1, ts / 1.2) * np.minimum(1, (seg - ts) / 1.2)
    part = np.zeros_like(ts)
    for n_ in ch:
        f = 440 * 2 ** ((n_ - 69) / 12)
        for det in (-0.12, 0.0, 0.12):
            ff = f * 2 ** (det / 12)
            part += np.sin(2 * math.pi * ff * ts) + 0.3 * np.sin(2 * math.pi * 2 * ff * ts)
    # crossfade into the loop seamlessly by windowing
    music[s0:s1] += part * env
# soft plucked arpeggio
arp_notes = [72, 76, 79, 83, 69, 72, 76, 79, 65, 69, 72, 76, 67, 71, 74, 76]
for i, n_ in enumerate(arp_notes):
    s0 = int(SR * i * (L / 16))
    b = bell(440 * 2 ** ((n_ - 69) / 12), 1.5, 3) * 0.35
    end = min(len(music), s0 + len(b))
    music[s0:end] += b[: end - s0]
music = lp(music, 3500)
save("MUS_Ambient_Loop.wav", music, 0.5)
print("audio written to", OUT)
