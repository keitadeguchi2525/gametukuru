# -*- coding: utf-8 -*-
# 効果音づくり（48kHz）。「コッ（納刀）→ パーンッ（剣戟）」を核に、重さのある音を層で作る
import numpy as np, wave, os
from scipy import signal
SR = 48000
OUT = os.path.join(os.path.dirname(__file__), "out", "audio")
rng = np.random.default_rng(11)

def T(d): return np.arange(int(SR * d)) / SR
def noise(d): return rng.uniform(-1, 1, int(SR * d))
def bp(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype='band', fs=SR, output='sos'); return signal.sosfilt(sos, x)
def lp(x, f, order=2):
    sos = signal.butter(order, f, btype='low', fs=SR, output='sos'); return signal.sosfilt(sos, x)
def hp(x, f, order=2):
    sos = signal.butter(order, f, btype='high', fs=SR, output='sos'); return signal.sosfilt(sos, x)
def env(d, a=0.001, k=20.0):
    t = T(d); return np.minimum(1, t / max(a, 1e-5)) * np.exp(-np.maximum(0, t - a) * k)
def pad(x, d):
    n = int(SR * d); y = np.zeros(max(n, len(x))); y[:len(x)] = x; return y
def mix(*xs):
    n = max(len(x) for x in xs); y = np.zeros(n)
    for x in xs: y[:len(x)] += x
    return y
def at(x, t0):
    return np.concatenate([np.zeros(int(SR * t0)), x])
def modes(d, freqs, decs, amps, bend=0.0):
    t = T(d); y = np.zeros(len(t))
    for f, k, a in zip(freqs, decs, amps):
        ph = rng.uniform(0, 2 * np.pi)
        ff = f * (1 + bend * np.exp(-t * 60))
        y += a * np.sin(2 * np.pi * np.cumsum(ff) / SR + ph) * np.exp(-t * k)
    return y
def sweep_noise(d, f0, f1, q=3.0, n=24):
    """中心周波数が動く帯域ノイズ（短い区間ごとにフィルタして重ねる）"""
    x = noise(d); y = np.zeros(len(x)); seg = len(x) // n + 1; w = np.hanning(seg * 2)
    for i in range(n):
        fc = f0 * (f1 / f0) ** (i / (n - 1))
        lo, hi = fc / (1 + 0.5 / q), min(SR * 0.45, fc * (1 + 0.5 / q))
        yy = bp(x, lo, hi)
        a = max(0, i * seg - seg // 2); b = min(len(x), a + seg * 2)
        y[a:b] += yy[a:b] * w[:b - a]
    return y
def room(x, wet=0.12, d=0.35, bright=5000):
    ir = lp(noise(d), bright) * np.exp(-T(d) * 14); ir[:int(SR * 0.004)] = 0
    y = signal.fftconvolve(x, ir)[:len(x) + len(ir)]
    y = y / (np.max(np.abs(y)) + 1e-9) * np.max(np.abs(x))
    return mix(x, y * wet)
def sat(x, g=2.0): return np.tanh(x * g) / np.tanh(g)
def save(name, x, peak=0.89, fade=0.01):
    x = np.asarray(x, dtype=np.float64)
    n = int(SR * fade); x[-n:] *= np.linspace(1, 0, n)
    x = x / (np.max(np.abs(x)) + 1e-9) * peak
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((x * 32767).astype(np.int16).tobytes())
    print("%-14s %.2fs" % (name, len(x) / SR))

BAR = [1.0, 2.756, 5.404, 8.933, 13.34, 18.64]

# ---- 納刀：刃が鞘を滑る「シュ……」→ 鍔が鯉口に当たる「コッ」 ----
for i in range(3):
    d = 0.2
    s = sweep_noise(d, 5200 + 300 * i, 2400, q=1.6) * (np.linspace(0.25, 1, int(SR * d)) ** 1.5) * 0.5
    s += modes(d, [2950 + 90 * i, 4380], [9, 12], [0.035, 0.02]) * np.linspace(0, 1, int(SR * d))
    s = hp(s, 900)
    s[-int(SR * 0.012):] *= np.linspace(1, 0, int(SR * 0.012))
    save("noto_slide%d" % i, room(s, 0.06, 0.15), 0.5)
for i in range(4):
    f = [1, 1.06, 0.95, 1.1][i]
    d = 0.32
    knock = modes(d, [215 * f, 430 * f], [34, 46], [1.0, 0.45])                           # 木（鞘）の胴鳴り
    tick = modes(d, [1180 * f, 1905 * f, 2760 * f, 3890 * f, 5650 * f], [58, 70, 85, 110, 150], [0.55, 0.5, 0.4, 0.3, 0.2])   # 鍔金の硬い当たり
    click = hp(noise(0.004), 2500) * np.linspace(1, 0, int(SR * 0.004)) * 0.9
    ring = modes(d, [3320 * f, 4975 * f], [16, 22], [0.05, 0.035])                         # かすかな余韻
    x = sat(mix(knock * 0.9, tick * 0.7, click, ring), 1.6)
    save("noto_click%d" % i, room(x, 0.1, 0.22, 6000))

# ---- 抜刀：鞘走りの「シャッ」＋空気を裂く音 ----
for i in range(3):
    d = 0.3
    sh = sweep_noise(0.13, 1800, 8200 + 500 * i, q=1.4) * env(0.13, 0.03, 16) * 0.8
    air = sweep_noise(0.2, 500, 2600, q=1.0) * np.hanning(int(SR * 0.2)) * 0.55
    shing = modes(d, [3480 + 140 * i, 5210, 7350, 9600], [11, 14, 19, 26], [0.16, 0.12, 0.08, 0.05], bend=0.02)
    save("batto%d" % i, room(mix(sh, at(air, 0.01), at(shing, 0.035)), 0.1, 0.3, 9000), 0.8)

# ---- 剣戟：太い刃同士の「パーンッ」。破裂＋鈍い鋼の胴鳴り＋短い高域 ----
for i, (f0, f1) in enumerate([(318, 471), (286, 512), (352, 436), (301, 489), (334, 455)]):
    d = 0.75
    crack = bp(noise(0.03), 700, 6500) * env(0.03, 0.0004, 130) * 1.5
    snap = hp(noise(0.006), 3500) * np.linspace(1, 0, int(SR * 0.006)) * 1.0
    thump = modes(0.2, [92, 148], [26, 34], [1.0, 0.6], bend=0.5)
    a = modes(d, [f0 * r for r in BAR], [9, 12, 16, 22, 30, 40], [0.5, 0.42, 0.3, 0.2, 0.12, 0.07], bend=0.012)
    b = modes(d, [f1 * r * 1.007 for r in BAR], [10, 13, 18, 25, 34, 44], [0.4, 0.36, 0.26, 0.16, 0.1, 0.06], bend=0.012)
    beat = 1 - 0.25 * np.sin(2 * np.pi * 31 * T(d)) * np.exp(-T(d) * 8)                    # 刃同士が噛んで震える
    x = sat(mix(crack, snap, thump * 0.9, (a + b) * beat * 0.55), 2.2)
    save("clash%d" % i, room(x, 0.16, 0.5, 5200))

# ---- 冴え（完璧な受け）：澄んだ高い余韻を重ねる ----
d = 1.3
x = modes(d, [2210, 3318, 4983, 6640], [3.2, 4.2, 6, 9], [0.5, 0.3, 0.16, 0.08], bend=0.004) * (1 - np.exp(-T(d) * 400))
save("sae", room(x, 0.25, 0.7, 9000), 0.6)

# ---- 斬撃（崩し中に入る一太刀）：「ザシュッ」 ----
for i in range(3):
    d = 0.4
    tear = lp(sweep_noise(0.16, 6500, 900, q=0.9) * env(0.16, 0.002, 22), 7000) * 1.1
    bite = bp(noise(0.02), 1500, 5000) * env(0.02, 0.0005, 160) * 0.9
    thud = modes(0.22, [78 + 6 * i, 131], [22, 30], [1.0, 0.5], bend=0.6)
    plate = modes(d, [612 + 40 * i, 1687, 3301], [24, 32, 42], [0.22, 0.16, 0.1])
    save("cut%d" % i, room(sat(mix(tear, bite, thud, plate), 2.0), 0.12, 0.3, 4500))

# ---- 弾かれ（崩していない相手に斬り込んだ）：硬く短い「カッ」 ----
for i in range(2):
    x = mix(modes(0.25, [760 + 90 * i, 2090, 3410, 5280], [38, 46, 60, 80], [0.6, 0.45, 0.3, 0.2]), hp(noise(0.004), 3000) * 0.8)
    save("deflect%d" % i, room(sat(x, 1.5), 0.1, 0.25, 6000), 0.7)

# ---- ボス：予兆のうなり／大太刀の風切り／突きの警告 ----
d = 0.55
t = T(d)
g = (signal.sawtooth(2 * np.pi * 49 * t) * 0.5 + signal.sawtooth(2 * np.pi * 73.6 * t) * 0.35 + np.sin(2 * np.pi * 24.5 * t) * 0.8)
g = lp(g, 420) * (t / d) ** 1.6 * (1 - 0.3 * np.sin(2 * np.pi * 17 * t))
save("warn", room(mix(g, lp(noise(d), 300) * (t / d) ** 2 * 0.3), 0.15, 0.4, 2000), 0.7, fade=0.04)
d = 0.6
t = T(d)
g2 = lp(signal.sawtooth(2 * np.pi * 98 * t) * 0.4 + signal.sawtooth(2 * np.pi * 138.6 * t) * 0.4, 1400) * (t / d) ** 1.2
g2 += modes(d, [1396, 1975], [5, 6], [0.12, 0.1]) * (t / d)
save("warn_red", room(g2, 0.15, 0.4, 3000), 0.7, fade=0.04)
for i in range(2):
    d = 0.42
    w = sweep_noise(d, 180, 1500 + 300 * i, q=0.8) * np.hanning(int(SR * d)) ** 1.5
    w += lp(noise(d), 160) * np.hanning(int(SR * d)) * 0.8
    save("whoosh%d" % i, w, 0.8)

# ---- 被弾：体に入る重い衝撃 ----
d = 0.5
x = mix(modes(0.35, [58, 97, 151], [14, 20, 28], [1.0, 0.7, 0.4], bend=0.8), lp(noise(0.08), 1800) * env(0.08, 0.001, 45) * 0.9,
        bp(noise(0.25), 300, 1200) * env(0.25, 0.01, 14) * 0.3)
save("hurt", room(sat(x, 2.5), 0.15, 0.4, 2500))

# ---- 崩し：体勢が砕ける。低い落ち込み＋割れる高域 ----
d = 1.1
t = T(d)
sub = np.sin(2 * np.pi * np.cumsum(40 + 110 * np.exp(-t * 9)) / SR) * np.exp(-t * 4.5)
crk = bp(noise(0.05), 500, 7000) * env(0.05, 0.0005, 80) * 1.2
sh = np.zeros(len(t))
for k in range(26):
    t0 = rng.uniform(0.02, 0.5); f = rng.uniform(2400, 9000)
    seg = modes(0.25, [f, f * 1.48], [30, 44], [0.12, 0.07]); a = int(SR * t0); sh[a:a + len(seg)] += seg[:len(sh) - a] * np.exp(-t0 * 4)
metal = modes(d, [233 * r for r in BAR], [6, 8, 11, 15, 20, 26], [0.4, 0.32, 0.22, 0.14, 0.08, 0.05])
save("break", room(sat(mix(sub * 1.1, crk, sh, metal * 0.5), 2.0), 0.2, 0.6, 5000), fade=0.05)

# ---- とどめ：長い一閃 ----
d = 1.8
t = T(d)
sl = sweep_noise(0.35, 900, 9000, q=1.0) * env(0.35, 0.05, 9)
boom = np.sin(2 * np.pi * np.cumsum(34 + 90 * np.exp(-t * 7)) / SR) * np.exp(-t * 2.6)
bell = modes(d, [1108, 1661, 2217, 3322, 4435], [2.2, 2.8, 3.6, 5, 7], [0.3, 0.22, 0.2, 0.1, 0.06]) * (1 - np.exp(-t * 200))
save("finish", room(sat(mix(sl, at(boom, 0.08), at(bell * 0.7, 0.06), at(bp(noise(0.04), 600, 6000) * env(0.04, 0.0005, 90), 0.08)), 1.8), 0.25, 0.8, 7000), fade=0.1)

# ---- かわす：衣擦れ ----
d = 0.26
save("dodge", sweep_noise(d, 700, 2600, q=0.7) * np.hanning(int(SR * d)) ** 2, 0.5)

# ---- 環境：遠い風と低い唸り（つなぎ目のないループ） ----
d = 12.0; t = T(d); n = len(t)
wind = lp(hp(noise(d), 120), 700) * (0.55 + 0.25 * np.sin(2 * np.pi * t / 6.0) + 0.2 * np.sin(2 * np.pi * t / 4.0 + 1))
drone = np.sin(2 * np.pi * 55 * t) * 0.5 + np.sin(2 * np.pi * 82.5 * t) * 0.3 * (0.6 + 0.4 * np.sin(2 * np.pi * t / 12.0)) + np.sin(2 * np.pi * 110.25 * t) * 0.12
x = wind * 0.6 + drone * 0.5
f = int(SR * 1.5); x[:f] = x[:f] * np.linspace(0, 1, f) + x[-f:] * np.linspace(1, 0, f); x = x[:-f]
save("ambience", x, 0.35, fade=0.0001)
