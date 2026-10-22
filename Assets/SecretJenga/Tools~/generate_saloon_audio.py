"""
THE LAST BLOCK - procedural saloon sounds (no samples).

    python generate_saloon_audio.py <Assets/SecretJenga/Audio>      (numpy + scipy)

  MUS_Saloon_Piano_Loop.wav   honky-tonk upright piano, ragtime oom-pah in C, seamless ~37 s loop
  SFX_Saloon_Murmur_Loop.wav  low room murmur with the odd glass clink, seamless loop
  SFX_Lighter.wav             match strike / flare
  SFX_Exhale.wav              a long smoky exhale
  SFX_Gulp.wav                a swallow of beer
"""
import os
import sys
import math
import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, lfilter, fftconvolve

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else "Audio"
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1876)


def bp(x, lo, hi, order=2):
    b, a = butter(order, [lo / (SR / 2), hi / (SR / 2)], btype="band")
    return lfilter(b, a, x)


def lp(x, f, order=2):
    b, a = butter(order, f / (SR / 2), btype="low")
    return lfilter(b, a, x)


def norm(x, peak):
    return x / (np.max(np.abs(x)) + 1e-9) * peak


def save(name, x, peak=0.8):
    wavfile.write(os.path.join(OUT, name), SR, (norm(x, peak) * 32767).astype(np.int16))
    print("wrote", name)


def room(x, seconds=1.2, damp=5.0, wet=0.28, seed=0):
    r = np.random.default_rng(seed)
    n = int(SR * seconds)
    ir = r.standard_normal(n) * np.exp(-np.arange(n) / SR * damp)
    ir = lp(ir, 3500)
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    return x * (1 - wet) + fftconvolve(x, ir)[: len(x)] * wet


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def piano_note(n, dur, vel=1.0, detune_cents=9.0):
    """Upright piano, two slightly out-of-tune strings (that's the honky-tonk)."""
    length = int(SR * (dur + 1.2))
    tt = np.arange(length) / SR
    f = midi(n)
    out = np.zeros(length)
    for d in (-detune_cents, detune_cents * 0.7):
        ff = f * 2 ** (d / 1200)
        for h in range(1, 9):
            inharm = 1 + 0.0004 * h * h
            amp = (1.0 / h ** 1.25) * (0.6 if h % 2 == 0 else 1.0)
            decay = 1.6 + 0.9 * h + n * 0.02
            out += amp * np.sin(2 * math.pi * ff * h * inharm * tt) * np.exp(-tt * decay)
    # damper: the note is released after `dur`
    rel = np.ones(length)
    k = int(SR * dur)
    rel[k:] = np.exp(-np.arange(length - k) / SR * 14)
    hammer = bp(rng.standard_normal(length), 800, 5000) * np.exp(-tt * 90) * 0.15
    attack = np.minimum(1, tt / 0.003)
    return (out * rel * attack + hammer) * vel


def place(buf, start, note):
    s = int(start * SR)
    e = min(len(buf), s + len(note))
    buf[s:e] += note[: e - s]


# ---------------------------------------------------------------- the tune
BPM = 104
beat = 60 / BPM
bars = 16
total = bars * 4 * beat
buf = np.zeros(int(SR * (total + 2)))

C, F, G7, Am, D7 = "C", "F", "G7", "Am", "D7"
prog = [C, C, F, F, C, Am, D7, G7, C, C, F, F, C, G7, C, C]
bass = {C: (36, 43), F: (41, 36), G7: (43, 38), Am: (45, 40), D7: (38, 45)}
chords = {C: (60, 64, 67), F: (60, 65, 69), G7: (59, 62, 65, 67), Am: (60, 64, 69), D7: (60, 62, 66, 69)}
tones = {C: [72, 76, 79, 74, 77], F: [72, 77, 81, 74, 79], G7: [74, 77, 79, 71, 76], Am: [72, 76, 81, 74, 79], D7: [74, 78, 81, 72, 76]}

mel_rng = np.random.default_rng(42)
for b, ch in enumerate(prog):
    t0 = b * 4 * beat
    lo, lo2 = bass[ch]
    # left hand: oom - pah - oom - pah
    place(buf, t0, piano_note(lo, beat * 0.9, 0.9))
    place(buf, t0 + beat, sum(piano_note(n, beat * 0.45, 0.42) for n in chords[ch]))
    place(buf, t0 + 2 * beat, piano_note(lo2, beat * 0.9, 0.8))
    place(buf, t0 + 3 * beat, sum(piano_note(n, beat * 0.45, 0.42) for n in chords[ch]))
    # right hand: syncopated ragtime figure built from the chord tones
    pattern = [0, 0.5, 1.5, 2, 2.5, 3.5] if b % 2 == 0 else [0, 1, 1.5, 2.5, 3, 3.5]
    last = None
    for i, p in enumerate(pattern):
        cand = tones[ch]
        n = cand[mel_rng.integers(0, len(cand))]
        if last is not None and abs(n - last) > 7:
            n = last + (2 if n > last else -2)
        last = n
        vel = 0.75 if p in (0, 2) else 0.6
        place(buf, t0 + p * beat + mel_rng.uniform(0, 0.012), piano_note(n, beat * 0.42, vel, 11))
        if i == len(pattern) - 1 and b % 4 == 3:
            place(buf, t0 + p * beat, piano_note(n + 12, beat * 0.4, 0.35, 12))

# wrap the tail of the last bar into the start so the loop is seamless
n_total = int(SR * total)
tail = buf[n_total:]
buf = buf[:n_total]
buf[: len(tail)] += tail
piano = lp(room(buf, 1.4, 4.0, 0.3, 3), 6500)
# a tired old instrument: a touch of wow
wobble = 1 + 0.0025 * np.sin(2 * math.pi * 0.35 * np.arange(len(piano)) / SR)
idx = np.clip(np.cumsum(wobble) - 1, 0, len(piano) - 1)
piano = np.interp(idx, np.arange(len(piano)), piano)
save("MUS_Saloon_Piano_Loop.wav", piano, 0.7)

# ---------------------------------------------------------------- murmur
dur = 24.0
n = int(SR * dur)
tt = np.arange(n) / SR
voices = np.zeros(n)
for k in range(9):
    src = bp(rng.standard_normal(n), 180 + 40 * k, 700 + 60 * k)
    # syllable rhythm: 3..6 Hz bursts, phrases a few seconds long
    syl = np.clip(np.sin(2 * math.pi * rng.uniform(3, 6) * tt + rng.uniform(0, 6)), 0, 1) ** 2
    phrase = np.clip(np.sin(2 * math.pi * rng.uniform(0.08, 0.2) * tt + rng.uniform(0, 6)), 0, 1)
    voices += src * syl * phrase
for _ in range(5):  # glasses
    at = rng.uniform(1, dur - 2)
    s = int(at * SR)
    g = np.arange(int(SR * 0.8)) / SR
    clink = sum(np.sin(2 * math.pi * f * g) * np.exp(-g * 9) for f in (2890, 3570, 4810)) * 0.08
    voices[s:s + len(clink)] += clink[: n - s]
# seamless: crossfade the end into the start
fade = int(SR * 2)
w = np.linspace(0, 1, fade)
voices[:fade] = voices[:fade] * w + voices[-fade:] * (1 - w)
voices = voices[:-fade]
save("SFX_Saloon_Murmur_Loop.wav", room(voices, 1.6, 3.0, 0.45, 7), 0.5)

# ---------------------------------------------------------------- smoking / drinking
g = np.arange(int(SR * 1.2)) / SR
strike = bp(rng.standard_normal(len(g)), 1500, 7000) * np.exp(-g * 30) * 0.8
flare = bp(rng.standard_normal(len(g)), 300, 3000) * np.clip(g / 0.05, 0, 1) * np.exp(-g * 3.0) * 0.5
save("SFX_Lighter.wav", strike + flare, 0.6)

g = np.arange(int(SR * 2.2)) / SR
breath = bp(rng.standard_normal(len(g)), 250, 2600) * np.sin(math.pi * np.clip(g / 2.0, 0, 1)) ** 1.5
save("SFX_Exhale.wav", lp(breath, 2200), 0.45)

g = np.arange(int(SR * 0.5)) / SR
gulp = np.sin(2 * math.pi * (180 - 90 * g) * g) * np.exp(-((g - 0.12) / 0.06) ** 2) + 0.3 * bp(rng.standard_normal(len(g)), 200, 900) * np.exp(-((g - 0.14) / 0.05) ** 2)
save("SFX_Gulp.wav", lp(gulp, 1200), 0.5)
