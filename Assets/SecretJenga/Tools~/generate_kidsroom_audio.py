"""
THE LAST BLOCK - procedural kids'-room sounds (no samples).

    python generate_kidsroom_audio.py <Assets/SecretJenga/Audio>      (numpy + scipy)

  MUS_MusicBox_Loop.wav      an original little lullaby on a wind-up music box, seamless ~34 s loop
  SFX_Room_Tone_Loop.wav     a quiet bedroom in the evening: wall clock tick, the odd creak, seamless loop
  SFX_Squeak.wav             a squeaky plastic hammer (the toy's whistle)
  SFX_Bonk.wav               hollow plastic BONK on a head
  SFX_Birdies_Loop.wav       cartoon "seeing stars" tweets, loop
  SFX_Whoosh.wav             a quick swing through the air
  SFX_Step_0..2.wav          sneaker steps on a wooden floor
  SFX_DoorCreak.wav          a bedroom door swinging open
  SFX_SadTrombone.wav        wah-wah-wah-waaah (the tower fell)
  SFX_Toy_0..2.wav           toys in the room: rattle, xylophone notes, a squeaky duck
  SFX_Slurp.wav              licking a lollipop
  SFX_Gulp.wav               a swallow of juice
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
rng = np.random.default_rng(2007)


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
    return x / (np.max(np.abs(x)) + 1e-9) * peak


def save(name, x, peak=0.8):
    x = np.asarray(x, np.float64)
    wavfile.write(os.path.join(OUT, name), SR, (norm(x, peak) * 32767).astype(np.int16))
    print("wrote", name)


def room(x, seconds=0.8, damp=7.0, wet=0.22, seed=0):
    r = np.random.default_rng(seed)
    n = int(SR * seconds)
    ir = r.standard_normal(n) * np.exp(-np.arange(n) / SR * damp)
    ir = lp(ir, 5000)
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    return x * (1 - wet) + fftconvolve(x, ir)[: len(x)] * wet


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def t_(sec):
    return np.arange(int(SR * sec)) / SR


def add(buf, k, x):
    k = max(0, k)
    m = min(len(x), len(buf) - k)
    if m > 0:
        buf[k:k + m] += x[:m]


def env_ad(n, a, d):
    e = np.ones(n)
    na = max(1, int(SR * a))
    e[:na] = np.linspace(0, 1, na)
    e[na:] = np.exp(-np.arange(n - na) / SR / d)
    return e


# ------------------------------------------------------------------ music box lullaby (original melody)
def tine(n, dur, vel=1.0):
    """A music-box tine: bright inharmonic partials, a click, long ringing decay."""
    f = midi(n)
    tt = t_(dur + 2.5)
    partials = [(1.0, 1.0, 1.6), (2.0, 0.32, 0.9), (3.01, 0.12, 0.5), (5.43, 0.08, 0.25), (8.9, 0.04, 0.12)]
    s = np.zeros_like(tt)
    for mul, amp, dec in partials:
        s += amp * np.sin(2 * np.pi * f * mul * tt + rng.uniform(0, 6.28)) * np.exp(-tt / (dec * (1.4 - 0.25 * (n - 72) / 12)))
    click = rng.standard_normal(len(tt)) * np.exp(-tt / 0.0015) * 0.15
    return (s + click) * vel * np.minimum(1, tt / 0.002)


# melody in F major, 3/4 lullaby, 16 bars; (midi note, beats); 0 = rest
BEAT = 0.62
mel = [
    (72, 1), (69, 1), (72, 1),   (74, 2), (72, 1),   (77, 1.5), (76, 0.5), (74, 1),   (72, 3),
    (70, 1), (69, 1), (67, 1),   (69, 2), (72, 1),   (74, 1), (72, 1), (70, 1),       (69, 3),
    (72, 1), (69, 1), (72, 1),   (77, 2), (76, 1),   (74, 1.5), (72, 0.5), (70, 1),  (69, 3),
    (67, 1), (69, 1), (70, 1),   (72, 1), (70, 1), (67, 1),   (69, 1.5), (67, 0.5), (64, 1),   (65, 3),
]
# accompaniment: broken chords per bar (root, 3rd, 5th), F - Bb - C - F ...
chords = [53, 53, 58, 53, 55, 53, 48, 53,  53, 53, 58, 53, 55, 48, 48, 53]
total_beats = sum(b for _, b in mel)
bars = int(math.ceil(total_beats / 3))
length = int(SR * (bars * 3 * BEAT + 0.01))
box = np.zeros(length + SR * 4)
pos = 0.0
for n, b in mel:
    if n:
        k = int(SR * (pos * BEAT + rng.uniform(-0.008, 0.008)))
        add(box, k, tine(n, b * BEAT, rng.uniform(0.8, 1.0)))
    pos += b
for bar in range(bars):
    root = chords[bar % len(chords)]
    third = 4 if root in (53, 48) else (4 if root == 58 else 3)
    for j, iv in enumerate((0, third + 12, 7 + 12)):
        k = int(SR * ((bar * 3 + j) * BEAT + rng.uniform(-0.006, 0.006)))
        add(box, k, tine(root + 12 + iv, BEAT, 0.42 if j else 0.5))
# wrap the ringing tail into the start -> seamless loop
tail = box[length:]
box = box[:length]
box[:len(tail)] += tail[:length]
# the wind-up mechanism: a faint whirr
box += lp(rng.standard_normal(length), 600) * 0.004
box = room(box, 1.1, 5.0, 0.3, 1)
save("MUS_MusicBox_Loop.wav", box, 0.62)

# ------------------------------------------------------------------ room tone: clock tick, quiet house
dur = 24.0
n = int(SR * dur)
tone = lp(rng.standard_normal(n), 300) * 0.02 + hp(lp(rng.standard_normal(n), 3000), 600) * 0.0025
for i in range(int(dur)):
    k = int(SR * i)
    tk = t_(0.03)
    pitch = 2600 if i % 2 == 0 else 2100
    click = np.sin(2 * np.pi * pitch * tk) * np.exp(-tk / 0.004) + rng.standard_normal(len(tk)) * np.exp(-tk / 0.002) * 0.5
    tone[k:k + len(click)] += click * 0.18
for _ in range(3):
    # a floorboard / the house settling
    k = int(rng.uniform(1, dur - 2) * SR)
    tk = t_(0.6)
    cr = bp(rng.standard_normal(len(tk)), 220, 900) * np.exp(-tk / 0.18) * (0.5 + 0.5 * np.sin(2 * np.pi * 23 * tk))
    tone[k:k + len(cr)] += cr * 0.05
tone = room(tone, 0.6, 9, 0.2, 2)
save("SFX_Room_Tone_Loop.wav", tone, 0.35)

# ------------------------------------------------------------------ squeaky hammer: the whistle in the head of the toy
def squeak(dur=0.32, f0=1650, f1=2350, wob=11):
    tt = t_(dur)
    f = f0 + (f1 - f0) * np.sin(np.pi * tt / dur) + 60 * np.sin(2 * np.pi * wob * tt)
    ph = 2 * np.pi * np.cumsum(f) / SR
    s = np.sin(ph) + 0.35 * np.sin(2 * ph) + 0.12 * np.sin(3 * ph)
    s += bp(rng.standard_normal(len(tt)), 1500, 4000) * 0.35          # air through the whistle
    e = np.sin(np.pi * np.clip(tt / dur, 0, 1)) ** 0.6
    return s * e

sq = np.concatenate([squeak(0.26, 1500, 2600), np.zeros(int(SR * 0.03)), squeak(0.2, 2200, 1700, 15) * 0.7])
save("SFX_Squeak.wav", room(sq, 0.5, 9, 0.15, 3), 0.8)

# ------------------------------------------------------------------ bonk: hollow plastic on a head
tt = t_(0.7)
body = np.sin(2 * np.pi * (180 * np.exp(-tt / 0.08) + 95) * tt) * np.exp(-tt / 0.11)
shell = (np.sin(2 * np.pi * 520 * tt) + 0.6 * np.sin(2 * np.pi * 1160 * tt + 1)) * np.exp(-tt / 0.045)
hit = bp(rng.standard_normal(len(tt)), 300, 3500) * np.exp(-tt / 0.008)
boing = np.sin(2 * np.pi * np.cumsum(260 + 120 * np.exp(-tt / 0.25) * np.sin(2 * np.pi * 14 * tt)) / SR) * np.exp(-tt / 0.22) * np.minimum(1, tt / 0.03)
bonk = body * 1.0 + shell * 0.55 + hit * 0.8 + boing * 0.35
save("SFX_Bonk.wav", room(bonk, 0.5, 8, 0.18, 4), 0.95)

# ------------------------------------------------------------------ birdies (seeing stars), loop
dur = 2.4
bird = np.zeros(int(SR * dur))
for k in range(7):
    st = k * dur / 7 + rng.uniform(-0.03, 0.03)
    tt = t_(0.11)
    f = 3200 + 1300 * np.sin(np.pi * tt / 0.11) * (1 if k % 2 else -0.4) + rng.uniform(-200, 200)
    s = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * tt / 0.11) ** 2
    i0 = int(SR * (st % dur))
    seg = bird[i0:i0 + len(s)]
    bird[i0:i0 + len(seg)] += s[:len(seg)]
save("SFX_Birdies_Loop.wav", room(bird, 0.4, 10, 0.2, 5), 0.5)

# ------------------------------------------------------------------ whoosh
tt = t_(0.38)
w = bp(rng.standard_normal(len(tt)), 300, 2400) * np.sin(np.pi * tt / 0.38) ** 2
w = lp(w, 1800)
save("SFX_Whoosh.wav", w, 0.6)

# ------------------------------------------------------------------ steps (sneakers on wood)
for i in range(3):
    tt = t_(0.22)
    thud = np.sin(2 * np.pi * (110 + 25 * i) * tt) * np.exp(-tt / 0.03)
    scuff = bp(rng.standard_normal(len(tt)), 600 + 150 * i, 3500) * np.exp(-tt / 0.02) * 0.4
    squeak2 = np.sin(2 * np.pi * (2400 + 300 * i) * tt) * np.exp(-((tt - 0.03) / 0.012) ** 2) * (0.15 if i == 1 else 0.05)
    save(f"SFX_Step_{i}.wav", room(thud + scuff + squeak2, 0.4, 10, 0.2, 6 + i), 0.6)

# ------------------------------------------------------------------ door creak
tt = t_(1.1)
f = 300 + 180 * np.sin(np.pi * tt / 1.1) + 25 * np.sin(2 * np.pi * 7 * tt)
pulses = np.sin(2 * np.pi * np.cumsum(f) / SR)
pulses = np.sign(pulses) * np.abs(pulses) ** 6
creak = bp(pulses, 400, 2500) * np.sin(np.pi * tt / 1.1) ** 0.5
latch = bp(rng.standard_normal(int(SR * 0.05)), 800, 5000) * np.exp(-t_(0.05) / 0.006)
door = np.concatenate([latch * 0.8, creak * 0.5])
save("SFX_DoorCreak.wav", room(door, 0.7, 7, 0.25, 9), 0.6)

# ------------------------------------------------------------------ sad trombone
def bone(n0, n1, dur, vib=0.0):
    tt = t_(dur)
    f = midi(n0) + (midi(n1) - midi(n0)) * np.clip(tt / dur, 0, 1) ** 2
    f = f * (1 + vib * 0.03 * np.sin(2 * np.pi * 6 * tt) * np.clip((tt - 0.2) / 0.3, 0, 1))
    ph = 2 * np.pi * np.cumsum(f) / SR
    s = sum((1 / h) ** 0.8 * np.sin(h * ph) for h in range(1, 12))
    s = lp(s, 1800)
    e = np.minimum(1, tt / 0.04) * np.minimum(1, (dur - tt) / 0.08)
    return s * e

tb = np.concatenate([bone(55, 54, 0.45), np.zeros(int(SR * 0.05)), bone(54, 53, 0.45), np.zeros(int(SR * 0.05)),
                     bone(53, 52, 0.45), np.zeros(int(SR * 0.05)), bone(52, 51, 1.5, 1.0)])
save("SFX_SadTrombone.wav", room(tb, 0.9, 5, 0.25, 10), 0.7)

# ------------------------------------------------------------------ toys
tt = t_(0.5)
rattle = np.zeros(len(tt))
for k in range(9):
    i0 = int(SR * (k * 0.045 + rng.uniform(0, 0.01)))
    g = bp(rng.standard_normal(int(SR * 0.02)), 2500, 7000) * np.exp(-t_(0.02) / 0.004)
    rattle[i0:i0 + len(g)] += g * (1 - k / 10)
save("SFX_Toy_0.wav", room(rattle, 0.4, 9, 0.2, 11), 0.35)
xy = np.zeros(int(SR * 1.4))
for k, n_ in enumerate((79, 76, 72)):
    tt = t_(1.0)
    note = (np.sin(2 * np.pi * midi(n_) * tt) + 0.25 * np.sin(2 * np.pi * midi(n_) * 3.93 * tt)) * np.exp(-tt / 0.35)
    i0 = int(SR * k * 0.16)
    xy[i0:i0 + len(note)] += note
save("SFX_Toy_1.wav", room(xy, 0.6, 7, 0.25, 12), 0.4)
duck = np.concatenate([squeak(0.16, 900, 1300, 20), np.zeros(int(SR * 0.08)), squeak(0.18, 1000, 1400, 18)])
save("SFX_Toy_2.wav", room(duck, 0.4, 9, 0.2, 13), 0.35)

# ------------------------------------------------------------------ lollipop slurp, juice gulp
tt = t_(0.45)
sl = bp(rng.standard_normal(len(tt)), 900, 4500) * (0.5 + 0.5 * np.sin(2 * np.pi * 9 * tt)) * np.sin(np.pi * tt / 0.45)
save("SFX_Slurp.wav", sl, 0.4)
tt = t_(0.35)
gl = np.sin(2 * np.pi * (240 - 120 * tt / 0.35) * tt) * np.exp(-((tt - 0.12) / 0.06) ** 2) + bp(rng.standard_normal(len(tt)), 300, 1500) * np.exp(-((tt - 0.12) / 0.05) ** 2) * 0.4
save("SFX_Gulp.wav", gl, 0.5)

# ------------------------------------------------------------------ MUS_Playroom_Loop: an upbeat, bouncy original tune (112 BPM, C major)
# marimba lead, ukulele strums, plucked bass, soft kick / clap / shaker, a glockenspiel sparkle. 16 bars, seamless loop.
BPM = 112
SPB = 60.0 / BPM                       # seconds per beat
BARS = 16
LEN = int(SR * BARS * 4 * SPB)
song = np.zeros(LEN + SR * 3)
r2 = np.random.default_rng(31)


def put(x, t, gain=1.0):
    add(song, int(t * SR), x * gain)


def marimba(n, dur=0.6, vel=1.0):
    f = midi(n)
    tt = t_(dur + 0.4)
    s = np.sin(2 * np.pi * f * tt) * np.exp(-tt / 0.32) + 0.35 * np.sin(2 * np.pi * f * 4.0 * tt) * np.exp(-tt / 0.05)
    s += 0.12 * np.sin(2 * np.pi * f * 9.2 * tt) * np.exp(-tt / 0.015)
    return s * vel * np.minimum(1, tt / 0.002)


def glock(n, vel=1.0):
    f = midi(n)
    tt = t_(1.4)
    s = np.sin(2 * np.pi * f * tt) * np.exp(-tt / 0.6) + 0.4 * np.sin(2 * np.pi * f * 2.76 * tt) * np.exp(-tt / 0.2)
    return s * vel * np.minimum(1, tt / 0.001)


def pluck(n, dur, bright=0.5, seed=0):
    """Karplus-Strong string (ukulele / bass)."""
    f = midi(n)
    N = int(SR / f)
    r = np.random.default_rng(seed)
    buf = r.uniform(-1, 1, N)
    buf = lp(buf, 1500 + 6000 * bright)
    out = np.zeros(int(SR * dur))
    for i in range(len(out)):
        out[i] = buf[i % N]
        buf[i % N] = 0.5 * (buf[i % N] + buf[(i + 1) % N]) * 0.996
    return out * np.minimum(1, np.arange(len(out)) / 40)


def kick():
    tt = t_(0.35)
    return np.sin(2 * np.pi * np.cumsum(55 + 110 * np.exp(-tt / 0.04)) / SR) * np.exp(-tt / 0.12)


def clap():
    tt = t_(0.25)
    n = bp(r2.standard_normal(len(tt)), 900, 5000)
    env = np.exp(-tt / 0.06) + 0.6 * np.exp(-np.maximum(0, tt - 0.012) / 0.01) * (tt > 0.012)
    return n * env


def shaker():
    tt = t_(0.09)
    return hp(r2.standard_normal(len(tt)), 6000) * np.exp(-tt / 0.025)


# chords per bar (root midi, quality): I vi IV V | I vi IV V | IV V iii vi | IV V I I
prog = [(60, "M"), (57, "m"), (53, "M"), (55, "M")] * 2 + [(53, "M"), (55, "M"), (52, "m"), (57, "m"), (53, "M"), (55, "M"), (60, "M"), (60, "M")]
def chord(root, q):
    return [root, root + (4 if q == "M" else 3), root + 7, root + 12]

# melody: (beat offset within the bar, midi note, length in beats); 2-bar phrases, bouncy, call and answer
phrase_a = [(0, 72, .5), (.5, 74, .5), (1, 76, .5), (1.5, 79, .5), (2, 76, 1), (3, 74, .5), (3.5, 72, .5),
            (4, 69, .5), (4.5, 72, .5), (5, 76, .5), (6, 74, 1.5), (7.5, 72, .5)]
phrase_b = [(0, 77, .5), (.5, 76, .5), (1, 74, .5), (1.5, 72, .5), (2, 74, .75), (2.75, 76, .25), (3, 79, 1),
            (4, 79, .5), (4.5, 77, .5), (5, 76, .5), (5.5, 74, .5), (6, 72, 1), (7, 71, .5), (7.5, 74, .5)]
phrase_c = [(0, 77, .5), (.5, 79, .5), (1, 81, 1), (2, 79, .5), (2.5, 77, .5), (3, 76, 1),
            (4, 74, .5), (4.5, 76, .5), (5, 77, .5), (5.5, 79, .5), (6, 81, .75), (6.75, 79, .25), (7, 76, 1)]
phrase_d = [(0, 77, .5), (.5, 76, .5), (1, 74, .5), (1.5, 77, .5), (2, 76, .5), (2.5, 74, .5), (3, 72, 1),
            (4, 74, .5), (4.5, 76, .5), (5, 79, .5), (5.5, 76, .5), (6, 72, 1.5)]
melody = [(0, phrase_a), (2, phrase_b), (4, phrase_a), (6, phrase_d), (8, phrase_c), (10, phrase_b), (12, phrase_c), (14, phrase_d)]

swing = 0.06 * SPB                   # a little bounce on the off-beat eighths
def T(bar, beat):
    frac = beat - math.floor(beat)
    return (bar * 4 + beat) * SPB + (swing if abs(frac - 0.5) < 1e-6 else 0.0)

for bar0, ph in melody:
    for beat, n, ln in ph:
        put(marimba(n, ln * SPB, r2.uniform(0.85, 1.0)), T(bar0, beat), 0.55)

for bar, (root, q) in enumerate(prog):
    notes = chord(root, q)
    # bass: root on 1, fifth on 3, a walking note on 4.5
    put(pluck(root - 24, SPB * 1.6, 0.25, bar), T(bar, 0), 0.75)
    put(pluck(root - 17, SPB * 1.4, 0.25, bar + 50), T(bar, 2), 0.6)
    put(pluck(root - 22 if q == "M" else root - 21, SPB * 0.5, 0.2, bar + 90), T(bar, 3.5), 0.4)
    # ukulele: strums on the off-beats (down-up feel), each string a few ms apart
    for k, beat in enumerate((0.5, 1.5, 2.5, 3.0, 3.5)):
        up = k % 2 == 1
        for j, n in enumerate(reversed(notes) if up else notes):
            put(pluck(n, SPB * 0.9, 0.7, bar * 10 + k * 3 + j), T(bar, beat) + j * 0.009, 0.16)
    # drums
    for beat in (0, 2):
        put(kick(), T(bar, beat), 0.7)
    put(kick(), T(bar, 2.5), 0.35)
    for beat in (1, 3):
        put(clap(), T(bar, beat), 0.32)
    for e in range(8):
        put(shaker(), T(bar, e * 0.5), 0.12 if e % 2 else 0.07)
    # glockenspiel sparkle every 4 bars
    if bar % 4 == 3:
        for j, n in enumerate((84, 88, 91, 96)):
            put(glock(n, 0.6), T(bar, 3 + j * 0.25), 0.25)

tail = song[LEN:]
song = song[:LEN]
song[:len(tail)] += tail[:LEN]
song = room(song, 0.7, 7, 0.16, 77)
save("MUS_Playroom_Loop.wav", song, 0.72)
