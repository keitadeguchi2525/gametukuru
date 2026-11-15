# -*- coding: utf-8 -*-
# ボス「門守（かどもり）」：主人公よりひとまわり大きい逆三角形。肩の上に「乗る」大きな肩甲、
# 背の欠けた光輪、身の丈ほどの大太刀。黒曜の鎧に深紅の布とマゼンタの光
import sys, os, math
sys.path.insert(0, os.path.dirname(__file__))
from rigcore import *
from humanoid import Humanoid, xf

OUT = os.path.join(os.path.dirname(__file__), "out")
BL = 1.95
D = dict(pelvis=1.30, waist=1.47, neck=2.06, shX=0.46, shZ=1.99, uarm=0.47, larm=0.44, palm=0.11,
         hipX=0.17, hipZ=1.26, uleg=0.60, lleg=0.56)


class Boss(Humanoid):
    def solve_weapons(self, P, sp):
        sw = sp["sword"]
        dirv = V(sw["dir"]).normalized()
        P["Odachi"] = M4(frame(dirv, V(sw["edge"])), V(sw["grip"]) + dirv * 0.12)

    def hand_target(self, P, sp, s, h):
        if isinstance(h, str):
            k = P["Odachi"]; ky = k.col[1].xyz.normalized(); ko = k.translation
            if h == "sword": return ko - ky * 0.12, ky
            if h == "sword2": return ko - ky * 0.40, ky
        return super().hand_target(P, sp, s, h)


def build():
    reset_scene()
    OBS = mat("B_黒曜", (0.05, 0.042, 0.075), shade=(0.012, 0.008, 0.028), rim=0.55, spec=0.16)
    GUN = mat("B_鉄紫", (0.2, 0.16, 0.3), shade=(0.06, 0.04, 0.13), rim=0.4, spec=0.1)
    CRIM = mat("B_深紅", (0.5, 0.03, 0.11), shade=(0.17, 0.006, 0.07), rim=0.25)
    GOLD = mat("B_金", (0.86, 0.6, 0.24), shade=(0.36, 0.17, 0.1), rim=0.2, spec=0.25)
    NEON = mat("B_ネオン", (1.0, 0.16, 0.5), kind="neon", intensity=2.0)
    LINE = mat("線", (0.006, 0.005, 0.012), kind="line")
    for m in (OBS, GUN, CRIM, GOLD, NEON): MATS[m.name]["rimColor"] = [1.0, 0.25, 0.6]

    r = Boss("Boss", LINE, D, line_w=0.009)
    grip0 = V((-0.6, -0.2, 1.0)); dir0 = V((0, -1, 0))
    r.weapon_joint("Odachi", M4(frame(dir0, V((0, 0, -1))), grip0 + dir0 * 0.12))
    r.free_joint("TabF", "Hips", (0, -0.2, 1.4))
    r.free_joint("TabB", "Hips", (0, 0.2, 1.4))
    r.free_joint("PauldronL", "Chest", (0.44, 0, 2.03))
    r.free_joint("PauldronR", "Chest", (-0.44, 0, 2.03))
    X, Y, Z = V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))

    # ---------- 腰 ----------
    pel = loft("Pelvis", [ring((0, 0, z), X, Y, rx, ry, 14, 3.0) for z, rx, ry in
                          [(1.12, 0.17, 0.14), (1.22, 0.26, 0.19), (1.36, 0.25, 0.185), (1.5, 0.215, 0.165)]], OBS)
    belt = loft("Belt", [ring((0, 0, z), X, Y, rx, ry, 14, 3.0) for z, rx, ry in [(1.37, 0.268, 0.2), (1.46, 0.245, 0.185)]], GOLD, cap0=False, cap1=False)
    hipsParts = [pel, belt]
    for sg in (1, -1):   # 腰の横の草摺（くさずり）
        g = []
        for a in (-0.9, -0.3, 0.3, 0.9):
            col = []
            for z, rr in [(1.4, 0.275), (1.2, 0.36), (0.98, 0.4)]:
                col.append(V((sg * math.cos(a) * rr, math.sin(a) * rr * 0.72, z)))
            g.append(col)
        hipsParts.append(sheet("Fauld", g, [GUN, GOLD], thick=0.03, mat_fn=lambda u, v: 1 if v == 1 else 0))
    r.attach(join("Pelvis", hipsParts), "Hips")

    def tab(name, y, zs, bulge):
        g = []
        for z, w in zs:
            t = (1.4 - z)
            yy = y + bulge * t
            g.append([V((-w, yy, z)), V((0, yy + (0.02 if bulge < 0 else -0.02), z)), V((w, yy, z))])
        return sheet(name, [list(c) for c in zip(*g)], [CRIM, GOLD], thick=0.022, smooth_angle=50, mat_fn=lambda u, v: 1 if v == len(zs) - 2 else 0)
    r.attach(tab("TabF", -0.205, [(1.42, 0.14), (1.1, 0.135), (0.72, 0.12), (0.46, 0.11), (0.3, 0.004)], -0.05), "TabF")
    r.attach(tab("TabB", 0.2, [(1.42, 0.2), (1.0, 0.21), (0.55, 0.2), (0.24, 0.17), (0.06, 0.004)], 0.09), "TabB")

    # ---------- 脚 ----------
    for s, sg in (("L", 1), ("R", -1)):
        hx = sg * D["hipX"]
        th = tube("Thigh" + s, [(hx, 0, 1.3), (hx + sg * 0.01, 0, 0.98), (hx + sg * 0.01, 0, 0.64)], [(0.15, 0.165), (0.14, 0.15), (0.115, 0.125)], OBS, n=12, power=2.6, zhint=(0, -1, 0))
        r.attach(th, "UpperLeg" + s)
        gr = tube("Greave" + s, [(hx + sg * 0.01, 0, 0.7), (hx + sg * 0.01, -0.01, 0.42), (hx + sg * 0.01, 0, 0.13)], [(0.125, 0.135), (0.118, 0.14), (0.14, 0.16)], [GUN, GOLD], n=12, power=2.6, zhint=(0, -1, 0))
        knee = lathe("Knee" + s, [(0.001, 0.2), (0.05, 0.1), (0.1, 0.02), (0.11, -0.06), (0.001, -0.08)], [OBS], n=8, center=(hx + sg * 0.01, -0.12, 0.66), axis_rot=rot_ypr(-28, 0, 0), sy=0.8)
        cuff = tube("Cuff" + s, [(hx + sg * 0.01, 0, 0.17), (hx + sg * 0.01, 0, 0.12)], [(0.146, 0.166), (0.15, 0.17)], GOLD, n=12, power=2.6, zhint=(0, -1, 0), cap0=False, cap1=False)
        r.attach(join("Greave" + s, [gr, knee, cuff]), "LowerLeg" + s)
        ft = loft("Foot" + s, [[V((hx + sx * w, y, z)) for sx, z in ((-1, 0), (1, 0), (1, h), (0, h * 1.25), (-1, h))] for y, w, h in
                                [(0.14, 0.085, 0.13), (-0.1, 0.1, 0.13), (-0.26, 0.085, 0.09), (-0.4, 0.012, 0.03)]], OBS, smooth_angle=30)
        r.attach(ft, "Foot" + s)

    # ---------- 胴（腰は細く、胸の上端をいちばん広く） ----------
    torso = loft("Torso", [ring((0, cy, z), X, Y, rx, ry, 16, 3.0) for z, rx, ry, cy in
                           [(1.44, 0.2, 0.155, 0), (1.6, 0.25, 0.18, 0), (1.8, 0.385, 0.235, -0.01), (1.98, 0.47, 0.245, -0.01), (2.07, 0.32, 0.19, 0)]], OBS)
    abs_ = [loft("Ab", [ring((0, 0, z), X, Y, rx, ry, 16, 3.0) for z, rx, ry in [(z0, rx0, ry0), (z0 + 0.07, rx0 + 0.022, ry0 + 0.012)]], GUN, cap0=False, cap1=False)
            for z0, rx0, ry0 in [(1.47, 0.215, 0.165), (1.57, 0.245, 0.18)]]
    # 胸甲：前に張り出す一枚板と、縦の光の芯
    def cw(z): return 0.13 + (z - 1.6) / 0.43 * 0.3
    plate = sheet("ChestPlate", [[V((u * cw(z), -0.2 - 0.075 * (1 - abs(u)) ** 0.7 - (0.03 if 1.7 < z < 2.0 else 0) - (z - 1.6) * 0.06, z)) for z in (1.6, 1.76, 1.94, 2.03)] for u in (-1, -0.55, 0, 0.55, 1)],
                  [GUN, GOLD], thick=0.035, mat_fn=lambda u, v: 1 if v == 2 else 0)
    core = box("Core", (0, -0.33, 1.84), (0.05, 0.02, 0.2), NEON, bevel=0.0)
    gorget = tube("Gorget", [(0, 0.01, 2.02), (0, 0.02, 2.12), (0, 0.035, 2.2)], [(0.2, 0.19), (0.215, 0.2), (0.25, 0.235)], [OBS, GOLD], n=14, cap0=False, cap1=False,
                  mat_fn=lambda i, j: 1 if i == 1 else 0)
    r.attach(join("Torso", [torso] + abs_ + [plate, gorget]), "Chest")
    r.attach(core, "Chest", line=False)

    # 背の光輪（欠けた輪）
    hc = V((0, 0.36, 2.2)); htilt = rot_ypr(-14, 0, 0)
    for k, (a0, a1) in enumerate([(-0.35, 1.2), (1.45, 2.5), (2.75, 3.6), (3.95, 5.6)]):
        pts = [hc + htilt @ V((math.cos(a) * 0.7, 0, math.sin(a) * 0.7)) for a in [a0 + (a1 - a0) * i / 14 for i in range(15)]]
        r.attach(tube("Halo%d" % k, pts, [0.016] * 15, NEON, n=5, zhint=(0, 1, 0)), "Chest", line=False)
    strut = tube("HaloStrut", [(0, 0.2, 1.9), hc], [0.03, 0.02], OBS, n=6)
    r.attach(strut, "Chest")

    # ---------- 肩甲：肩の「上と外」に重ねて乗せる（体に埋めない） ----------
    for s, sg in (("L", 1), ("R", -1)):
        ps = []
        for k, (cx, cz, rad, h, tilt) in enumerate([(0.52, 2.04, 0.33, 0.2, 22), (0.67, 1.93, 0.29, 0.15, 48), (0.75, 1.78, 0.24, 0.12, 68)]):
            ps.append(lathe("Lame", [(0.001, h), (rad * 0.5, h * 0.86), (rad * 0.86, h * 0.5), (rad, 0.02), (rad * 1.03, -0.02), (rad * 0.9, -0.035), (0.001, -0.03)],
                            [OBS, GOLD], n=14, center=(sg * cx, 0, cz), axis_rot=rot_ypr(0, 0, sg * tilt), sy=0.92, mat_fn=lambda i, j: 1 if i in (3, 4) else 0))
        horn = lathe("Horn", [(0.001, 0.46), (0.03, 0.26), (0.075, 0.0), (0.001, -0.02)], [GUN], n=6, center=(sg * 0.6, 0, 2.16), axis_rot=rot_ypr(0, 0, sg * 38))
        r.attach(join("Pauldron" + s, ps + [horn]), "Pauldron" + s)

    # ---------- 腕（前腕が大きい籠手） ----------
    for s, sg in (("L", 1), ("R", -1)):
        sh = V((sg * D["shX"], 0, D["shZ"])); el = sh + V((sg * 0.02, 0, -D["uarm"])); wr = el + V((sg * 0.01, 0, -D["larm"]))
        ua = tube("UpperArm" + s, [sh + V((0, 0, 0.05)), (sh + el) / 2, el + V((0, 0, -0.03))], [0.12, 0.105, 0.09], GUN, n=10, power=2.4)
        r.attach(ua, "UpperArm" + s)
        ga = tube("Gauntlet" + s, [el + V((0, 0, 0.06)), el + V((0, 0, -0.1)), wr + V((0, 0, 0.1)), wr + V((0, 0, 0.0))], [0.105, 0.115, 0.15, 0.135], [OBS, GOLD], n=10, power=3.0,
                  mat_fn=lambda i, j: 1 if i == 2 else 0)
        elb = lathe("Elbow" + s, [(0.001, 0.17), (0.035, 0.08), (0.085, 0.0), (0.001, -0.03)], [OBS], n=6, center=el + V((0, 0.08, 0.0)), axis_rot=rot_ypr(-110, 0, 0))
        r.attach(join("LowerArm" + s, [ga, elb]), "LowerArm" + s)
        hd = box("Hand" + s, wr + V((0, 0, -0.105)), (0.15, 0.17, 0.19), OBS, bevel=0.03)
        r.attach(hd, "Hand" + s)

    # ---------- 頭：小さく、肩の間に沈める。高い前立てと後ろへ流れる角 ----------
    helm = loft("Helm", [ring((0, cy, z), X, Y, rx, ry, 12, 2.6) for z, rx, ry, cy in
                         [(2.08, 0.095, 0.11, 0), (2.18, 0.118, 0.14, -0.005), (2.28, 0.112, 0.135, 0), (2.36, 0.07, 0.09, 0.01)]], OBS)
    jaw = loft("Jaw", [[V((-w, y, z0)), V((w, y, z0)), V((w * 0.7, y, z1)), V((-w * 0.7, y, z1))] for y, w, z0, z1 in
                       [(-0.06, 0.1, 2.2, 2.08), (-0.15, 0.07, 2.17, 2.1), (-0.2, 0.01, 2.13, 2.11)]], GUN, smooth_angle=25)
    crest = loft("Crest", [[V((-t, y, z0)), V((t, y, z0)), V((0, y, z1))] for y, t, z0, z1 in
                           [(-0.15, 0.018, 2.3, 2.34), (-0.08, 0.022, 2.32, 2.78), (0.06, 0.02, 2.32, 2.66), (0.17, 0.012, 2.28, 2.4)]], GOLD, smooth_angle=20)
    hparts = [helm, jaw, crest]
    for sg in (1, -1):
        hparts.append(tube("HelmHorn", [(sg * 0.1, 0.0, 2.26), (sg * 0.2, 0.08, 2.36), (sg * 0.25, 0.22, 2.5), (sg * 0.22, 0.4, 2.6)], [0.04, 0.034, 0.022, 0.004], GUN, n=6))
    r.attach(join("Head", hparts), "Head")
    eye = box("Eye", (0, -0.142, 2.215), (0.16, 0.02, 0.016), NEON)
    r.attach(eye, "Head", line=False)

    # ---------- 大太刀 ----------
    wm = r.rest_of("Odachi")
    def bw(t): return 0.15 + 0.03 * math.sin(min(1, t / 0.8) * math.pi * 0.5) if t < 0.86 else 0.18 * (1 - (t - 0.86) / 0.14) ** 0.8 + 0.004
    ts = [i / 14 for i in range(15)]
    blade = loft("OBlade", [[V((0.016, y, z0 - 0.42 * w)), V((0.012, y, z0 + 0.3 * w)), V((0, y, z0 + 0.5 * w)), V((-0.012, y, z0 + 0.3 * w)), V((-0.016, y, z0 - 0.42 * w)), V((0, y, z0 - 0.5 * w))]
                            for y, w, z0 in [(0.03 + t * BL, bw(t), -0.1 * t * t) for t in ts]], GUN, smooth_angle=25)
    edge = tube("OEdge", [V((0, 0.03 + t * BL, -0.1 * t * t + 0.5 * bw(t))) for t in ts], [0.012] * 14 + [0.003], NEON, n=4, zhint=(0, 0, 1))
    guard = box("OGuard", (0, 0.0, 0.0), (0.06, 0.06, 0.34), GOLD, bevel=0.012)
    hilt = tube("OHilt", [(0, -0.02, 0), (0, -0.33, 0), (0, -0.64, 0)], [(0.03, 0.036), (0.027, 0.032), (0.03, 0.036)], CRIM, n=8, zhint=(0, 0, 1))
    pom = lathe("OPommel", [(0.001, 0.05), (0.05, 0.0), (0.001, -0.06)], [GOLD], n=6, center=(0, -0.66, 0), axis_rot=Matrix.Rotation(math.pi / 2, 3, 'X'))
    od = join("Odachi", [blade, guard, hilt, pom]); xf(od, wm); r.attach(od, "Odachi")
    xf(edge, wm); r.attach(edge, "Odachi", line=False)
    return r


def mirror(sp):
    """左右を入れ替えた姿勢（両手の握りはそのまま）"""
    def fx(v): return (-v[0], v[1], v[2])
    def fr(v): return (v[0], -v[1], -v[2])
    o = {}
    for k, v in sp.items():
        if k in ("hips",): o[k] = fx(v)
        elif k in ("hips_rot", "chest", "head"): o[k] = fr(v)
        elif k in ("footL", "footR"):
            f = dict(v); f["pos"] = fx(v["pos"]); f["yaw"] = -v.get("yaw", 0)
            if "knee" in v: f["knee"] = fx(v["knee"])
            o["footR" if k == "footL" else "footL"] = f
        elif k in ("elbowL", "elbowR"): o["elbowR" if k == "elbowL" else "elbowL"] = fx(v)
        elif k == "sword": o[k] = dict(grip=fx(v["grip"]), dir=fx(v["dir"]), edge=fx(v["edge"]))
        elif k == "cloth":
            c = {}
            for n, e in v.items():
                n2 = n[:-1] + ("R" if n.endswith("L") else "L") if n[-1] in "LR" and n.startswith("Pauldron") else n
                c[n2] = fr(e)
            o[k] = c
        else: o[k] = v
    return o


# 姿勢（ボス自身は -Y を向く。相手は -Y の先 約2.9m）。名前の L/R は「プレイヤーから見て左／右から来る」
def poses(r):
    wide = dict(footR=dict(pos=(-0.42, -0.1, 0), yaw=-22), footL=dict(pos=(0.42, 0.12, 0), yaw=30))
    idle = dict(wide, hips=(0, 0, -0.1), hips_rot=(4, 8, 0), chest=(8, 4, 0), head=(16, -10, 0),
                handR="sword", handL="sword2", elbowR=(-1, 0.5, 0), elbowL=(1, 0.2, 0.3),
                sword=dict(grip=(-0.34, -0.42, 1.14), dir=(0.66, -0.66, -0.36), edge=(0.2, -0.5, 0.8)),
                cloth=dict(TabF=(4, 0, 0), TabB=(-4, 0, 0)))
    intro = dict(footR=dict(pos=(-0.3, 0.0, 0), yaw=-16), footL=dict(pos=(0.3, 0.0, 0), yaw=16),
                 hips=(0, 0, -0.02), chest=(-2, 0, 0), head=(12, 0, 0),
                 handR="sword", elbowR=(-1, 0.5, 0.2),
                 sword=dict(grip=(-0.78, -0.3, 1.98), dir=(0.06, -0.02, -1), edge=(0, -1, 0)))
    # プレイヤーの左から来る横薙ぎ＝ボスの右肩に担ぐ
    windL = dict(footR=dict(pos=(-0.5, 0.22, 0), yaw=-50), footL=dict(pos=(0.36, -0.3, 0), yaw=14),
                 hips=(-0.1, 0.1, -0.16), hips_rot=(2, -24, 0), chest=(-6, -38, -6), head=(8, 44, 0),
                 handR="sword", handL="sword2", elbowR=(-1, 0.6, 0.2), elbowL=(-0.2, -1, -0.3),
                 sword=dict(grip=(-0.62, 0.16, 2.02), dir=(-0.55, 0.78, 0.3), edge=(-0.6, -0.6, 0.2)),
                 cloth=dict(TabF=(-8, 0, -10), TabB=(-12, 0, 14), PauldronR=(0, 0, 14)))
    hitL = dict(footR=dict(pos=(-0.46, 0.3, 0), yaw=-30, pitch=18, lift=0.04), footL=dict(pos=(0.3, -0.72, 0), yaw=18, knee=(0.3, -1, 0)),
                hips=(0.1, -0.42, -0.34), hips_rot=(14, 30, 0), chest=(16, 34, 6), head=(-10, -40, 0),
                handR="sword", handL="sword2", elbowR=(-0.4, -0.3, -1), elbowL=(1, 0.6, 0),
                sword=dict(grip=(0.5, -1.0, 1.02), dir=(0.86, -0.36, -0.35), edge=(0.4, 0.9, 0)),
                cloth=dict(TabF=(-20, 0, 26), TabB=(-30, 0, -30)))
    recoilL = dict(footR=dict(pos=(-0.5, 0.4, 0), yaw=-36), footL=dict(pos=(0.36, -0.2, 0), yaw=16, pitch=-10),
                   hips=(-0.08, 0.2, -0.2), hips_rot=(-10, -14, 0), chest=(-20, -22, -8), head=(-18, 10, 0),
                   handR="sword", handL=dict(pos=(0.7, -0.3, 1.5)), elbowR=(-1, 0.4, -0.3), elbowL=(1, 0.5, -0.4),
                   sword=dict(grip=(-0.98, 0.1, 2.12), dir=(-0.5, 0.42, 0.76), edge=(-0.7, -0.5, -0.2)),
                   cloth=dict(TabF=(28, 0, 0), TabB=(12, 0, 0), PauldronR=(0, 0, 18)))
    # 真上からの唐竹割り
    windD = dict(footR=dict(pos=(-0.36, 0.14, 0), yaw=-24), footL=dict(pos=(0.36, -0.16, 0), yaw=20),
                 hips=(0, 0.06, 0.0), hips_rot=(-6, 0, 0), chest=(-16, 0, 0), head=(26, 0, 0),
                 handR="sword", handL="sword2", elbowR=(-1, -0.4, 0.2), elbowL=(1, -0.4, 0.2),
                 sword=dict(grip=(0, 0.1, 2.72), dir=(0, 0.6, 0.8), edge=(0, -0.8, 0.6)),
                 cloth=dict(TabF=(10, 0, 0), TabB=(-6, 0, 0), PauldronL=(0, 0, -16), PauldronR=(0, 0, 16)))
    hitD = dict(footR=dict(pos=(-0.42, 0.34, 0), yaw=-24, pitch=20, lift=0.05), footL=dict(pos=(0.34, -0.7, 0), yaw=10, knee=(0.2, -1, 0)),
                hips=(0, -0.4, -0.42), hips_rot=(26, 0, 0), chest=(26, 0, 0), head=(-34, 0, 0),
                handR="sword", handL="sword2", elbowR=(-1, 0.2, 0.3), elbowL=(1, 0.2, 0.3),
                sword=dict(grip=(0, -1.02, 0.78), dir=(0, -0.92, -0.38), edge=(0, -0.38, -0.92)),
                cloth=dict(TabF=(-34, 0, 0), TabB=(-40, 0, 0)))
    # 突き（弾けない。横へかわす）
    windT = dict(footR=dict(pos=(-0.44, 0.4, 0), yaw=-60), footL=dict(pos=(0.3, -0.34, 0), yaw=8),
                 hips=(-0.06, 0.16, -0.3), hips_rot=(8, -40, 0), chest=(10, -30, 0), head=(4, 66, 0),
                 handR="sword", handL=dict(pos=(0.3, -0.75, 1.5)), elbowR=(-1, 0.8, 0.1), elbowL=(1, 0, -0.3),
                 sword=dict(grip=(-0.62, 0.42, 1.42), dir=(0.24, -0.97, -0.06), edge=(0, 0, 1)),
                 cloth=dict(TabF=(-6, 0, 0), TabB=(-14, 0, 0)))
    hitT = dict(footR=dict(pos=(-0.4, 0.5, 0), yaw=-40, pitch=24, lift=0.06), footL=dict(pos=(0.22, -0.9, 0), yaw=4, knee=(0.2, -1, 0)),
                hips=(0, -0.5, -0.4), hips_rot=(20, 34, 0), chest=(14, 30, 0), head=(-24, -60, 0),
                handR="sword", handL=dict(pos=(0.7, 0.2, 1.0)), elbowR=(-0.6, 0.2, -0.8), elbowL=(1, 0.8, 0),
                sword=dict(grip=(-0.1, -1.5, 1.22), dir=(0.03, -1, -0.06), edge=(0, 0, 1)),
                cloth=dict(TabF=(-30, 0, 0), TabB=(-46, 0, 0)))
    guard = dict(wide, hips=(0, 0.04, -0.14), hips_rot=(4, -10, 0), chest=(4, -8, 0), head=(12, 14, 0),
                 handR="sword", handL="sword2", elbowR=(-1, 0.2, -0.3), elbowL=(1, 0.2, -0.4),
                 sword=dict(grip=(-0.12, -0.56, 1.2), dir=(0.3, -0.1, 0.95), edge=(0.2, -1, 0)))
    # 崩し：片膝をつき、大太刀を杖にして胸が開く
    broken = dict(footR=dict(pos=(-0.4, 0.4, 0), yaw=-20, pitch=70, lift=0.1, knee=(-0.3, -1, -0.8)), footL=dict(pos=(0.36, -0.36, 0), yaw=14, knee=(0.2, -1, 0.2)),
                  hips=(0, 0.02, -0.62), hips_rot=(10, 6, 0), chest=(20, 8, 4), head=(38, 0, 0),
                  handR="sword", handL=dict(pos=(0.62, -0.3, 0.42)), elbowR=(-1, 0.3, 0.2), elbowL=(1, 0.6, 0.2),
                  sword=dict(grip=(-0.62, -0.42, 1.0), dir=(0.3, -0.78, -0.5), edge=(0, -0.5, 0.8)),
                  cloth=dict(TabF=(-50, 0, 0), TabB=(30, 0, 0)))
    dead = dict(footR=dict(pos=(-0.4, 0.44, 0), yaw=-20, pitch=75, lift=0.1, knee=(-0.3, -1, -0.8)), footL=dict(pos=(0.4, 0.4, 0), yaw=20, pitch=75, lift=0.1, knee=(0.3, -1, -0.8)),
                hips=(0, 0.1, -0.78), hips_rot=(18, 0, 0), chest=(38, 0, 0), head=(46, 0, 0),
                handR=dict(pos=(-0.6, -0.5, 0.2)), handL=dict(pos=(0.6, -0.5, 0.2)), elbowR=(-1, 0.3, 0.3), elbowL=(1, 0.3, 0.3),
                sword=dict(grip=(-0.9, -0.9, 0.06), dir=(0.9, -0.42, 0.0), edge=(0, 0, -1)),
                cloth=dict(TabF=(-70, 0, 0), TabB=(40, 0, 0)))
    out = [("idle", idle), ("intro", intro), ("windL", windL), ("hitL", hitL), ("recoilL", recoilL),
           ("windR", mirror(windL)), ("hitR", mirror(hitL)), ("recoilR", mirror(recoilL)),
           ("windD", windD), ("hitD", hitD), ("windT", windT), ("hitT", hitT), ("guard", guard), ("broken", broken), ("dead", dead)]
    return out


if __name__ == "__main__":
    r = build()
    ps = poses(r)
    for n, sp in ps: r.add_pose(n, r.solve(sp))
    cam = setup_render(640, 800)
    bpy.context.scene.display.shading.light = 'STUDIO'
    which = sys.argv[-1]
    if which == "turn":
        r.apply(r.poses["idle"]); bpy.context.view_layer.update()
        for nm, eye in [("front", (1.6, -6.4, 1.3)), ("back", (-2.4, 6.0, 2.4)), ("side", (-6.6, 0, 1.6))]:
            shoot(cam, os.path.join(OUT, "boss_%s.png" % nm), eye, (0, 0, 1.3), lens=50)
    elif which == "sheet":
        for n, _ in ps:
            r.apply(r.poses[n]); bpy.context.view_layer.update()
            # プレイヤーの目線（少し右下から見上げる）
            shoot(cam, os.path.join(OUT, "boss_p_%s.png" % n), (-0.7, -5.6, 1.5), (0, -0.3, 1.45), lens=32, w=520, h=560)
    elif which == "export":
        r.export(os.path.join(OUT, "Boss.json"))
