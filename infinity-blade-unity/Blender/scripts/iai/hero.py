# -*- coding: utf-8 -*-
# 新主人公「旅の抜刀者」：笠・白い外套・朱の尾・左腰の鞘。背中から見て一目で分かる形にする
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
from rigcore import *
from humanoid import Humanoid, xf

OUT = os.path.join(os.path.dirname(__file__), "out")
BL = 0.80      # 刃の長さ
TSUKA = 0.27

D = dict(pelvis=0.99, waist=1.12, neck=1.54, shX=0.19, shZ=1.47, uarm=0.29, larm=0.27, palm=0.07,
         hipX=0.09, hipZ=0.96, uleg=0.46, lleg=0.42)

SAYA_POS = V((0.135, -0.15, 0.035))                 # 腰（Hips）から見た鯉口の位置
SAYA_DIR = V((0.22, 0.86, -0.40)).normalized()      # 鯉口→鐺（こじり）


def sori(t):   # 反り（刃と反対側へ）
    return -0.035 * t * t


class Hero(Humanoid):
    def saya_m(self, P, sp):
        dirv = V(sp.get("saya_dir", SAYA_DIR)).normalized()
        pos = SAYA_POS + V(sp.get("saya_off", (0, 0, 0)))
        return P["Hips"] @ M4(frame(dirv, V((0, 0, 1))), pos)

    def solve_weapons(self, P, sp):
        P["Saya"] = self.saya_m(P, sp)
        sw = sp.get("sword", {"sheath": 0.0})
        if "sheath" in sw:
            P["Katana"] = P["Saya"] @ Matrix.Translation((0, -sw["sheath"] * BL, 0))
        else:
            dirv = V(sw["dir"]).normalized()
            P["Katana"] = M4(frame(dirv, V(sw["edge"])), V(sw["grip"]) + dirv * 0.06)
        if "kasa" in sp:
            f = P["Head"].to_3x3() @ V((0, -1, 0)); yaw = math.degrees(math.atan2(f.x, -f.y))
            P["Kasa"] = M4(rot_ypr(sp["kasa"][0], yaw, sp["kasa"][1]), P["Kasa"].translation)

    def hand_target(self, P, sp, s, h):
        if isinstance(h, str):
            k = P["Katana"]; ky = k.col[1].xyz.normalized(); ko = k.translation
            sm = P["Saya"]; sy = sm.col[1].xyz.normalized(); so = sm.translation
            if h == "sword": return ko - ky * 0.06, ky
            if h == "sword2": return ko - ky * 0.19, ky
            if h == "hover": return ko - ky * 0.10 + V((-0.03, -0.02, 0.05)), ky
            if h == "saya": return so + sy * 0.075, -sy
        return super().hand_target(P, sp, s, h)


def build():
    reset_scene()
    WHITE = mat("H_白", (0.87, 0.85, 0.79), shade=(0.43, 0.44, 0.62), rim=0.12, spec=0.06)
    INK = mat("H_黒", (0.045, 0.045, 0.07), shade=(0.012, 0.012, 0.03), rim=0.4, spec=0.1)
    INDIGO = mat("H_藍", (0.075, 0.1, 0.23), shade=(0.02, 0.028, 0.09), rim=0.35)
    RED = mat("H_朱", (0.78, 0.09, 0.045), shade=(0.3, 0.015, 0.06), rim=0.2)
    NEON = mat("H_ネオン", (0.15, 0.95, 1.0), kind="neon", intensity=1.8)
    BLADE = mat("H_刃", (0.72, 1.0, 1.0), kind="neon", intensity=2.4)
    LINE = mat("線", (0.006, 0.005, 0.012), kind="line")

    r = Hero("Hero", LINE, D, line_w=0.0055)
    hipsRest = r.rest_of("Hips")
    sayaRest = hipsRest @ M4(frame(SAYA_DIR, V((0, 0, 1))), SAYA_POS)
    r.joint("Saya", "Hips", sayaRest.translation, sayaRest.to_3x3())
    r.weapon_joint("Katana", sayaRest)
    r.free_joint("TailL", "Chest", (0.075, 0.115, 1.43))
    r.free_joint("TailR", "Chest", (-0.075, 0.115, 1.43))
    r.free_joint("Kasa", "Head", (0, 0, 1.70))

    X, Y, Z = V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))

    # ---------- 腰 ----------
    pel = loft("Pelvis", [ring((0, 0, z), X, Y, rx, ry, 12, 2.6) for z, rx, ry in
                          [(0.86, 0.095, 0.08), (0.93, 0.135, 0.102), (1.03, 0.132, 0.098), (1.13, 0.12, 0.09)]], INDIGO)
    obi = loft("Obi", [ring((0, 0, z), X, Y, rx, ry, 12, 2.6) for z, rx, ry in
                       [(1.035, 0.14, 0.106), (1.105, 0.13, 0.099)]], RED, cap0=False, cap1=False)
    knot = box("ObiKnot", (0.02, 0.108, 1.07), (0.10, 0.035, 0.07), RED, rot=rot_ypr(0, 0, 12), bevel=0.008)
    r.attach(join("Pelvis", [pel, obi, knot]), "Hips")

    # ---------- 脚（袴：膝下で鐘のように広がる） ----------
    for s, sg in (("L", 1), ("R", -1)):
        hx = sg * D["hipX"]
        th = tube("Thigh" + s, [(hx, 0, 0.99), (hx + sg * 0.012, 0, 0.74), (hx + sg * 0.018, 0, 0.47)],
                  [(0.088, 0.1), (0.092, 0.102), (0.094, 0.102)], INDIGO, n=14, power=2.2, zhint=(0, -1, 0))
        r.attach(th, "UpperLeg" + s)
        sh = tube("Shin" + s, [(hx + sg * 0.018, 0, 0.535), (hx + sg * 0.03, 0.005, 0.30), (hx + sg * 0.045, 0.012, 0.065)],
                  [(0.09, 0.098), (0.102, 0.112), (0.128, 0.146)], [INDIGO, RED], n=14, power=2.2, zhint=(0, -1, 0))
        hem = tube("Hem" + s, [(hx + sg * 0.045, 0.012, 0.075), (hx + sg * 0.046, 0.012, 0.05)],
                   [(0.131, 0.149), (0.133, 0.151)], RED, n=14, power=2.2, zhint=(0, -1, 0), cap0=False, cap1=False)
        r.attach(join("Shin" + s, [sh, hem]), "LowerLeg" + s)
        ft = box("Foot" + s, (hx, -0.055, 0.04), (0.085, 0.235, 0.075), INK, bevel=0.015, taper=(0.8, 0.85))
        r.attach(ft, "Foot" + s)

    # ---------- 胴 ----------
    torso = loft("Torso", [ring((0, cy, z), X, Y, rx, ry, 12, 2.5) for z, rx, ry, cy in
                           [(1.09, 0.125, 0.092, 0), (1.22, 0.135, 0.098, 0), (1.36, 0.175, 0.112, 0.004), (1.47, 0.195, 0.108, 0.006), (1.535, 0.085, 0.075, 0.008)]], INK)
    collar = tube("Collar", [(0, 0.01, 1.50), (0, 0.012, 1.575), (0, 0.016, 1.615)], [(0.078, 0.082), (0.082, 0.088), (0.092, 0.098)], INK, n=12, cap0=False, cap1=False)

    # 外套：左（鞘の側）へ長く垂れる、左右非対称の白い布
    NA = 28
    def hemz(a): return 1.235 - 0.14 * math.cos(a) - 0.035 * math.sin(a) - 0.13 * max(0.0, math.cos(a - 0.25)) ** 5
    rows = [(0.0, 0.082, 0.078, None), (0.2, 0.16, 0.125, 1.525), (0.45, 0.275, 0.172, 1.485), (0.75, 0.3, 0.2, None), (0.93, 0.312, 0.212, None), (1.0, 0.316, 0.216, None)]
    grid = []
    for ia in range(NA + 1):
        a = 2 * math.pi * (ia % NA) / NA
        col = []
        hz = hemz(a)
        for t, rx, ry, z in rows:
            if t == 0.0: z = 1.545
            elif z is None: z = 1.485 + (hz - 1.485) * ((t - 0.45) / 0.55)
            col.append(V((math.cos(a) * rx, math.sin(a) * ry + 0.008, z)))
        grid.append(col)
    def mantle_mat(u, v):
        return 1 if v == 4 else 0
    mantle = sheet("Mantle", grid, [WHITE, RED], thick=0.014, smooth_angle=50, mat_fn=mantle_mat)
    crest = box("Crest", (0, 0.196, 1.36), (0.035, 0.012, 0.15), RED, rot=rot_ypr(-10, 0, 0), bevel=0.004)
    r.attach(join("Torso", [torso, collar, mantle, crest]), "Chest")

    # 朱の尾（背中から膝まで。Unity 側で揺らす）
    for s, sg in (("L", 1), ("R", -1)):
        g = []
        for iv, (z, w, yb) in enumerate([(1.43, 0.06, 0.125), (1.2, 0.07, 0.2), (0.95, 0.065, 0.235), (0.72, 0.045, 0.25), (0.5, 0.004, 0.275)]):
            cx = sg * (0.075 + (1.43 - z) * 0.09)
            g.append([V((cx - w, yb + 0.004 * iv, z)), V((cx, yb + 0.012, z)), V((cx + w, yb + 0.004 * iv, z))])
        tail = sheet("Tail" + s, [list(c) for c in zip(*g)], RED, thick=0.012, smooth_angle=50)
        r.attach(tail, "Tail" + s)

    # ---------- 腕 ----------
    for s, sg in (("L", 1), ("R", -1)):
        sh = V((sg * D["shX"], 0, D["shZ"])); el = sh + V((sg * 0.02, 0, -D["uarm"])); wr = el + V((sg * 0.01, 0, -D["larm"]))
        ua = tube("UpperArm" + s, [sh + V((0, 0, 0.03)), (sh + el) / 2, el + V((0, 0, -0.02))], [0.052, 0.046, 0.041], WHITE, n=10)
        r.attach(ua, "UpperArm" + s)
        la = tube("Bracer" + s, [el + V((0, 0, 0.035)), el + V((0, 0, -0.06)), wr + V((0, 0, 0.015))], [0.054, 0.048, 0.04], [INK, RED], n=10, power=2.3,
                  mat_fn=lambda i, j: 0)
        band = tube("Band" + s, [el + V((0, 0, 0.04)), el + V((0, 0, 0.015))], [0.057, 0.056], RED, n=10, power=2.3, cap0=False, cap1=False)
        r.attach(join("LowerArm" + s, [la, band]), "LowerArm" + s)
        hd = box("Hand" + s, wr + V((0, 0, -0.065)), (0.055, 0.075, 0.092), INK, bevel=0.016)
        r.attach(hd, "Hand" + s)

    # ---------- 頭と笠 ----------
    hc = V((0, 0, 1.655))
    prof = [(math.sin(a) * 0.097, -math.cos(a) * 0.105) for a in [i * math.pi / 8 for i in range(9)]]
    head = lathe("HeadBall", [(max(rr, 0.001), hc.z + z) for rr, z in prof], INK, n=14, sy=1.06)
    eye = box("Eye", (0.012, -0.099, 1.66), (0.105, 0.012, 0.011), NEON, rot=rot_ypr(0, 0, -7))
    r.attach(join("Head", [head]), "Head")
    r.attach(eye, "Head", line=False)
    tilt = rot_ypr(9, 0, 0)
    kc = V((0, 0, 1.70))
    kasa = lathe("KasaTop", [(0.001, 0.125), (0.09, 0.088), (0.22, 0.032), (0.345, -0.03), (0.35, -0.04), (0.335, -0.042), (0.2, 0.01), (0.001, 0.05)],
                 [WHITE, INK, RED], n=28, center=kc, axis_rot=tilt, mat_fn=lambda i, j: (2 if i == 0 else (0 if i < 4 else 1)))
    rimpts = [kc + tilt @ V((math.cos(a) * 0.349, math.sin(a) * 0.349, -0.035)) for a in [2 * math.pi * i / 36 for i in range(37)]]
    rim = tube("KasaRim", rimpts, [0.006] * 37, NEON, n=5, zhint=(0, 0, 1), cap0=False, cap1=False)
    r.attach(kasa, "Kasa")
    r.attach(rim, "Kasa", line=False)

    # ---------- 鞘と刀（鞘の基準姿勢の中で形を作ってから置く） ----------
    def along(n, f):
        return [f(i / (n - 1)) for i in range(n)]
    saya = tube("SayaBody", along(9, lambda t: V((0, t * (BL + 0.04), sori(t)))), [(0.02, 0.031)] * 8 + [(0.012, 0.02)], INK, n=8, zhint=(0, 0, 1))
    sline = tube("SayaLine", along(9, lambda t: V((0.0215, 0.05 + t * (BL - 0.12), sori((0.05 + t * (BL - 0.12)) / BL)))), [0.0035] * 9, NEON, n=4, zhint=(0, 0, 1))
    kojiri = tube("Kojiri", along(3, lambda t: V((0, BL - 0.06 + t * 0.105, sori(1)))), [(0.023, 0.034), (0.023, 0.034), (0.013, 0.02)], RED, n=8, zhint=(0, 0, 1))
    kuri = box("Kurigata", (0.022, 0.16, 0.0), (0.02, 0.045, 0.03), RED, bevel=0.005)
    so = join("Saya", [saya, kojiri, kuri]); xf(so, sayaRest); r.attach(so, "Saya")
    xf(sline, sayaRest); r.attach(sline, "Saya", line=False)

    blade = loft("Blade", [[V((0.006, y, z0 - 0.5 * w)), V((0, y, z0 + 0.5 * w)), V((-0.006, y, z0 - 0.5 * w))] for y, w, z0 in
                           [(t * BL, 0.044 - 0.01 * t - (0.03 * max(0, t - 0.93) / 0.07), sori(t)) for t in [i / 16 for i in range(17)]]], BLADE, smooth_angle=20)
    tsuba = lathe("Tsuba", [(0.001, -0.005), (0.04, -0.005), (0.04, 0.005), (0.001, 0.005)], INK, n=10, axis_rot=Matrix.Rotation(math.pi / 2, 3, 'X'), sy=1.2)
    tsuka = tube("Tsuka", [(0, -0.005, 0), (0, -TSUKA * 0.5, -0.004), (0, -TSUKA, -0.012)], [(0.015, 0.02), (0.014, 0.019), (0.016, 0.021)], WHITE, n=8, zhint=(0, 0, 1))
    kashira = tube("Kashira", [(0, -TSUKA + 0.004, -0.012), (0, -TSUKA - 0.022, -0.014)], [(0.018, 0.023), (0.015, 0.019)], RED, n=8, zhint=(0, 0, 1))
    fuchi = tube("Fuchi", [(0, -0.006, 0), (0, -0.04, -0.001)], [(0.0175, 0.0225), (0.017, 0.022)], RED, n=8, zhint=(0, 0, 1), cap0=False, cap1=False)
    hilt = join("KatanaHilt", [tsuba, tsuka, kashira, fuchi]); xf(hilt, sayaRest); r.attach(hilt, "Katana", line_w=0.004)
    xf(blade, sayaRest); r.attach(blade, "Katana", line=False)
    return r


# ---------------------------------------------------------------- 姿勢（敵は -Y の先にいる）
def poses(r):
    # 立ち姿：半身で、左手は鞘に。笠で顔を隠す
    stand = dict(kasa=(6, 0), 
        hips=(0.0, 0.01, -0.025), hips_rot=(0, 24, -2), chest=(1, 8, 2), head=(11, -30, 0),
        footR=dict(pos=(-0.12, -0.10, 0), yaw=2), footL=dict(pos=(0.16, 0.13, 0), yaw=48),
        handL="saya", elbowL=(1, 0.5, 0.1), sword=dict(sheath=0.0),
        cloth=dict(TailL=(-4, 0, 3), TailR=(-4, 0, -3)))
    # 居合腰：低く沈み、柄に手を掛ける寸前。鞘を前へ送る
    kamae = dict(kasa=(10, 0), 
        hips=(0.02, -0.02, -0.26), hips_rot=(20, 34, 0), chest=(16, 14, 3), head=(-26, -46, 0),
        footR=dict(pos=(-0.13, -0.46, 0), yaw=4, knee=(-0.35, -1, 0)), footL=dict(pos=(0.20, 0.42, 0), yaw=62, pitch=24, lift=0.05, knee=(1, -0.55, 0)),
        saya_dir=(0.12, 0.93, -0.18), saya_off=(-0.02, -0.07, 0.03),
        handL="saya", handR="sword", elbowL=(1, 0.35, 0.25), elbowR=(-0.9, -0.1, 0.45), sword=dict(sheath=0.0),
        cloth=dict(TailL=(-22, 0, 8), TailR=(-20, 0, -4)))
    # 納刀の途中：切っ先を鯉口に乗せ、右手を大きく前へ
    noto = dict(kasa=(7, 0), 
        hips=(0.01, 0.0, -0.13), hips_rot=(9, 26, 0), chest=(6, 4, 2), head=(4, -24, 0),
        footR=dict(pos=(-0.12, -0.30, 0), yaw=2), footL=dict(pos=(0.18, 0.30, 0), yaw=55, pitch=10, lift=0.02),
        saya_dir=(0.42, 0.86, -0.22), saya_off=(-0.01, -0.05, 0.02),
        handL="saya", handR="sword", elbowL=(1, 0.3, 0.2), elbowR=(-0.8, 0.1, 0.5), sword=dict(sheath=0.9),
        cloth=dict(TailL=(-10, 0, 5), TailR=(-10, 0, -3)))
    lunge = dict(footR=dict(pos=(-0.17, -0.68, 0), yaw=-6, knee=(-0.25, -1, 0)), footL=dict(pos=(0.22, 0.52, 0), yaw=66, pitch=30, lift=0.07, knee=(1, -0.5, 0)))
    # 右へ抜き付け（左→右の横一文字）。鞘を後ろへ引き切る
    cutR = dict(lunge, kasa=(8, -7),
        hips=(-0.04, -0.20, -0.30), hips_rot=(14, 42, 4), chest=(6, -34, -6), head=(-12, -10, 0),
        saya_dir=(0.34, 0.9, 0.12), saya_off=(0.03, 0.10, 0.0),
        handL="saya", handR="sword", elbowL=(1, 0.9, 0.3), elbowR=(-0.6, 0.6, -0.5),
        sword=dict(grip=(-0.66, -0.34, 1.0), dir=(-0.88, 0.42, 0.16), edge=(-0.3, 1, 0.05)),
        cloth=dict(TailL=(-62, 0, 34), TailR=(-48, 0, 26)))
    # 左へ斬り返し（右→左）。腕が体の前で交差して終わる
    cutL = dict(lunge, kasa=(8, 7),
        hips=(0.05, -0.18, -0.28), hips_rot=(16, 48, -4), chest=(10, 26, 6), head=(-14, -68, 0),
        saya_dir=(0.1, 0.95, -0.05), saya_off=(-0.03, 0.04, 0.0),
        handL="saya", handR="sword", elbowL=(1, 0.8, 0.2), elbowR=(-0.3, -0.6, -0.7),
        sword=dict(grip=(0.38, -0.5, 1.04), dir=(0.9, 0.28, 0.3), edge=(0.2, 1, -0.3)),
        cloth=dict(TailL=(-46, 0, -24), TailR=(-60, 0, -36)))
    # 斬り上げ（左腰から右上へ）。体が伸び上がる
    cutU = dict(kasa=(0, -5), 
        hips=(-0.03, -0.16, -0.10), hips_rot=(-4, 30, 3), chest=(-14, -30, -8), head=(-8, 0, 0),
        footR=dict(pos=(-0.16, -0.56, 0), yaw=-6, knee=(-0.25, -1, 0)), footL=dict(pos=(0.20, 0.46, 0), yaw=64, pitch=38, lift=0.09, knee=(1, -0.5, 0)),
        saya_dir=(0.26, 0.82, -0.5), saya_off=(0.03, 0.08, 0.0),
        handL="saya", handR="sword", elbowL=(1, 0.9, 0.2), elbowR=(-0.8, 0.5, -0.3),
        sword=dict(grip=(-0.44, -0.30, 1.78), dir=(-0.5, 0.34, 0.8), edge=(-0.5, 0.75, -0.6)),
        cloth=dict(TailL=(-30, 0, 20), TailR=(-26, 0, 12)))
    # 斬り下ろし（真っ向）。両手で深く斬り下げて沈む
    cutD = dict(kasa=(16, 0), 
        hips=(0.0, -0.22, -0.42), hips_rot=(30, 14, 0), chest=(22, -8, 0), head=(-34, -6, 0),
        footR=dict(pos=(-0.16, -0.66, 0), yaw=-4, knee=(-0.3, -1, 0)), footL=dict(pos=(0.24, 0.50, 0), yaw=64, pitch=34, lift=0.08, knee=(1, -0.4, 0)),
        handL="sword2", handR="sword", elbowL=(1, 0.2, 0.2), elbowR=(-1, 0.2, 0.2),
        sword=dict(grip=(-0.06, -0.58, 0.60), dir=(-0.1, -0.84, -0.54), edge=(0, -0.5, -1)),
        cloth=dict(TailL=(-70, 0, 10), TailR=(-70, 0, -10)))
    # 抜き身で立つ（残心のあと力を抜いた形）
    drawn = dict(kasa=(6, 0), 
        hips=(0.0, -0.02, -0.06), hips_rot=(3, 26, 0), chest=(2, 4, 0), head=(6, -28, 0),
        footR=dict(pos=(-0.13, -0.22, 0), yaw=2), footL=dict(pos=(0.17, 0.22, 0), yaw=52),
        handL="saya", handR="sword", elbowL=(1, 0.5, 0.1), elbowR=(-0.7, 0.7, 0),
        sword=dict(grip=(-0.40, -0.20, 0.84), dir=(-0.22, -0.62, -0.75), edge=(0.2, -0.75, 0.6)),
        cloth=dict(TailL=(-6, 0, 3), TailR=(-6, 0, -3)))
    # 倒れる：膝をつき、刀を地に突いてうなだれる
    dead = dict(
        hips=(0.02, -0.05, -0.56), hips_rot=(26, 20, 4), chest=(30, 0, 4), head=(40, 0, 0),
        footR=dict(pos=(-0.16, -0.42, 0), yaw=0, knee=(-0.2, -1, 0.2)), footL=dict(pos=(0.16, 0.34, 0.0), yaw=20, pitch=70, lift=0.06, knee=(0.4, -1, -0.6)),
        handL=dict(pos=(0.3, -0.2, 0.3)), handR="sword", elbowR=(-1, 0.3, 0.2),
        sword=dict(grip=(-0.3, -0.5, 0.74), dir=(0.05, -0.12, -1), edge=(0, -1, 0)),
        cloth=dict(TailL=(-4, 0, 6), TailR=(-4, 0, -6)))
    return [("stand", stand), ("kamae", kamae), ("noto", noto), ("cutR", cutR), ("cutL", cutL), ("cutU", cutU), ("cutD", cutD), ("drawn", drawn), ("dead", dead)]


if __name__ == "__main__":
    r = build()
    ps = poses(r)
    for n, sp in ps: r.add_pose(n, r.solve(sp))
    cam = setup_render(640, 800)
    bpy.context.scene.display.shading.light = 'STUDIO'
    which = sys.argv[-1] if sys.argv[-1] in ("sheet", "turn", "export") else "sheet"
    if which == "turn":
        r.apply(r.poses["stand"]); bpy.context.view_layer.update()
        for nm, eye in [("front", (1.6, -4.2, 1.5)), ("back", (-1.9, 4.0, 2.0)), ("side", (-4.5, 0, 1.3))]:
            shoot(cam, os.path.join(OUT, "hero_%s.png" % nm), eye, (0, 0, 0.95), lens=60)
    elif which == "sheet":
        for n, _ in ps:
            r.apply(r.poses[n]); bpy.context.view_layer.update()
            shoot(cam, os.path.join(OUT, "hero_p_%s_back.png" % n), (-1.5, 3.6, 2.1), (0, -0.2, 0.9), lens=50, w=480, h=560)
            shoot(cam, os.path.join(OUT, "hero_p_%s_front.png" % n), (1.8, -3.8, 1.3), (0, 0, 0.9), lens=50, w=480, h=560)
    if which == "export":
        r.export(os.path.join(OUT, "Hero.json"))
