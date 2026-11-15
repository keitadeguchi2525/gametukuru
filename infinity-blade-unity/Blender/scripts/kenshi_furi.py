# -*- coding: utf-8 -*-
# Furi の考え方で作る男剣士（オリジナル）
#  ・剣道の面・胴・垂・小手をモチーフに、太いシルエットと左右非対称
#  ・色は 黒・白・朱 ＋ ネオンの青緑だけ。アニメ調の影2段と輪郭線
#  ・体格と骨は武士と同じ（Unity の人型にそのまま使える）
#  ・出力：Blender/Kenshi_Furi.blend と Blender/Renders/kenshi_*.png
import bpy, bmesh, os, math, json, traceback, random
from mathutils import Vector, Matrix, Quaternion

ROOT = os.path.join(os.path.expanduser("~"), "インフィニティブレード", "Blender")
FBX = os.path.join(ROOT, "Export", "SamuraiCharacter.fbx")
OUT = os.path.join(ROOT, "Renders"); os.makedirs(OUT, exist_ok=True)
LOG = os.path.join(OUT, "kenshi_log.txt"); open(LOG, "w", encoding="utf-8").close()
def log(*a):
    with open(LOG, "a", encoding="utf-8") as f: f.write(" ".join(str(x) for x in a) + "\n")
log("start")
random.seed(7)
D2R = math.pi / 180

# ================= 読み込み（素体と骨だけ残す） =================
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.curves, bpy.data.lights, bpy.data.cameras, bpy.data.images, bpy.data.armatures):
    for d in list(coll):
        if d.users == 0: coll.remove(d)
bpy.ops.import_scene.fbx(filepath=FBX, use_anim=False, ignore_leaf_bones=True, automatic_bone_orientation=True)
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
body = None
for o in list(bpy.context.scene.objects):
    if o.type != 'MESH': continue
    if o.vertex_groups and body is None: body = o
    else: bpy.data.objects.remove(o, do_unlink=True)
for d in list(bpy.data.meshes):
    if d.users == 0: bpy.data.meshes.remove(d)
bpy.context.view_layer.update()
log("kept", body.name, arm.name)

AW = arm.matrix_world
def bh(n): return AW @ arm.data.bones[n].head_local
def bt(n): return AW @ arm.data.bones[n].tail_local

# 素体の頂点を「一番強く効いている骨」ごとに分ける（腕と胴を混ぜずに測るため）
GN = {g.index: g.name for g in body.vertex_groups}
BV = []; BYG = {}
for v in body.data.vertices:
    p = body.matrix_world @ v.co; BV.append(p)
    if v.groups:
        g = max(v.groups, key=lambda x: x.weight)
        BYG.setdefault(GN.get(g.group, "?"), []).append(p)
log("groups", {k: len(v) for k, v in BYG.items()})
TORSO_G = ["Hips", "Spine", "Chest", "UpperChest"]

def pts_of(groups):
    out = []
    for g in groups: out += BYG.get(g, [])
    return out or BV

def extent(zc, dz, groups):
    pts = [p for p in pts_of(groups) if abs(p.z - zc) < dz]
    if not pts: return 0.15, 0.11, 0.0
    xs = [p.x for p in pts]; ys = [p.y for p in pts]
    return (max(xs) - min(xs)) / 2, (max(ys) - min(ys)) / 2, (max(ys) + min(ys)) / 2

def seg_radius(a, b, group, lo=0.2, hi=0.8):
    d = b - a; Ln = d.length; d = d / Ln; ds = []
    for p in pts_of([group]):
        t = (p - a).dot(d) / Ln
        if lo < t < hi: ds.append(((p - a) - d * (t * Ln)).length)
    ds.sort()
    return min(0.12, ds[int(len(ds) * 0.8)]) if ds else 0.06

# 体の目印
SHL, SHR = bh("Left_UpperArm"), bh("Right_UpperArm")
ELL, ELR = bh("Left_LowerArm"), bh("Right_LowerArm")
WRL, WRR = bh("Left_Hand"), bh("Right_Hand")
HPL, HPR = bh("Left_UpperLeg"), bh("Right_UpperLeg")
KNL, KNR = bh("Left_LowerLeg"), bh("Right_LowerLeg")
ANL, ANR = bh("Left_Foot"), bh("Right_Foot")
NECK, HEAD = bh("Neck"), bh("Head")
pelvisZ = (HPL.z + HPR.z) / 2
waistZ = pelvisZ + 0.13
chestTop = NECK.z - 0.035
shX = abs(SHL.x)
SIDE = 1 if SHL.x > 0 else -1   # 左（Left_）が +X なら 1
log("landmarks", round(pelvisZ, 3), round(waistZ, 3), round(chestTop, 3), round(shX, 3), SIDE)

# 胴の断面（高さ t=0 腰〜1 胸上）
T_SAMPLES = []
for i in range(7):
    t = i / 6; z = waistZ + (chestTop - waistZ) * t
    rx, ry, cy = extent(z, 0.03, TORSO_G)
    T_SAMPLES.append((rx, ry, cy))
def torso(t):
    t = max(0, min(1, t)) * 6; i = min(5, int(t)); f = t - i
    a, b = T_SAMPLES[i], T_SAMPLES[i + 1]
    return tuple(a[k] + (b[k] - a[k]) * f for k in range(3))
HIP = extent(pelvisZ + 0.02, 0.04, TORSO_G + ["Left_UpperLeg", "Right_UpperLeg"])
HEADC = HEAD + Vector((0, 0, 0.1))
hrx, hry, hcy = extent(HEADC.z, 0.03, ["Head", "Jaw", "Left_Eye", "Right_Eye"])
HEADC.y = hcy
HR = max(hrx, hry) + 0.022
log("torso", [tuple(round(v, 3) for v in s) for s in T_SAMPLES], "hip", HIP, "headR", round(HR, 3))


# ================= アニメ調マテリアル（2段の影＋縁の光） =================
def toon(name, lit, shade=None, rim=(0.35, 0.85, 1.0), rim_k=0.35, spec=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial'); out.location = (900, 0)
    if shade is None: shade = (lit[0] * 0.32, lit[1] * 0.32, lit[2] * 0.42 + 0.008)
    dif = nt.nodes.new('ShaderNodeBsdfDiffuse'); dif.inputs['Color'].default_value = (1, 1, 1, 1)
    s2r = nt.nodes.new('ShaderNodeShaderToRGB'); nt.links.new(dif.outputs[0], s2r.inputs[0])
    bw = nt.nodes.new('ShaderNodeRGBToBW'); nt.links.new(s2r.outputs['Color'], bw.inputs[0])
    ramp = nt.nodes.new('ShaderNodeValToRGB'); ramp.color_ramp.interpolation = 'CONSTANT'
    e = ramp.color_ramp.elements
    e[0].position = 0.0; e[0].color = (*shade, 1)
    e[1].position = 0.22; e[1].color = (*lit, 1)
    if spec > 0:
        hi = tuple(min(1.0, c + spec) for c in lit)
        el = e.new(0.82); el.color = (*hi, 1)
    nt.links.new(bw.outputs[0], ramp.inputs['Fac'])
    col = ramp.outputs['Color']
    if rim_k > 0:
        lw = nt.nodes.new('ShaderNodeLayerWeight'); lw.inputs['Blend'].default_value = 0.35
        rr = nt.nodes.new('ShaderNodeValToRGB'); rr.color_ramp.interpolation = 'CONSTANT'
        rr.color_ramp.elements[0].color = (0, 0, 0, 1); rr.color_ramp.elements[1].position = 0.62; rr.color_ramp.elements[1].color = (1, 1, 1, 1)
        nt.links.new(lw.outputs['Facing'], rr.inputs['Fac'])
        k = nt.nodes.new('ShaderNodeMath'); k.operation = 'MULTIPLY'; k.inputs[1].default_value = rim_k
        nt.links.new(rr.outputs['Color'], k.inputs[0])
        mx = nt.nodes.new('ShaderNodeMix'); mx.data_type = 'RGBA'; mx.blend_type = 'ADD'
        nt.links.new(k.outputs[0], mx.inputs['Factor'])
        nt.links.new(col, mx.inputs[6]); mx.inputs[7].default_value = (*rim, 1)
        col = mx.outputs[2]
    em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(col, em.inputs['Color']); em.inputs['Strength'].default_value = 1.0
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    return m

def neon(name, color, strength=6.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission'); em.inputs['Color'].default_value = (*color, 1); em.inputs['Strength'].default_value = strength
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    return m

def line_mat():
    m = bpy.data.materials.new("輪郭線"); m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission'); em.inputs['Color'].default_value = (0.006, 0.005, 0.012, 1)
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    m.use_backface_culling = True
    return m

# 配色：黒・白・朱＋ネオンの青緑だけ
NEON = (0.15, 0.95, 1.0)
M = {}
M['kuro'] = toon("黒", (0.035, 0.034, 0.045), (0.006, 0.006, 0.012), spec=0.12)
M['sumi'] = toon("墨（下着）", (0.05, 0.05, 0.065), (0.01, 0.01, 0.02))
M['kon'] = toon("濃紺（袴）", (0.035, 0.045, 0.1), (0.008, 0.01, 0.03))
M['shiro'] = toon("白（胴）", (0.86, 0.85, 0.82), (0.32, 0.34, 0.48), rim_k=0.15, spec=0.1)
M['aka'] = toon("朱", (0.62, 0.03, 0.025), (0.17, 0.006, 0.03), rim_k=0.25)
M['kin'] = toon("金", (0.75, 0.52, 0.16), (0.24, 0.12, 0.05), rim_k=0.2, spec=0.2)
M['ha'] = toon("刃", (0.8, 0.84, 0.9), (0.3, 0.34, 0.45), rim_k=0.3, spec=0.15)
M['neon'] = neon("ネオン", NEON, 2.2)
M['line'] = line_mat()
for s in body.material_slots: s.material = M['sumi']

def outline(o, w=0.0055):
    try:
        o.data.materials.append(M['line'])
        sc_ = o.matrix_world.to_scale()
        sz = max(1e-6, (abs(sc_.x) + abs(sc_.y) + abs(sc_.z)) / 3)
        so = o.modifiers.new("輪郭", 'SOLIDIFY')
        so.thickness = w / sz; so.offset = 1.0; so.use_flip_normals = True; so.use_rim = False
        so.material_offset = len(o.data.materials) - 1
    except Exception as e: log("ERR outline", o.name, e)

# ================= 形を作る道具 =================
COL = bpy.data.collections.new("剣士"); bpy.context.scene.collection.children.link(COL)
TRIS = [0]
def make_obj(name, bm, mat, parent_bone=None, thick=0.0, sub=2, line=True, smooth=True):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = smooth
    o = bpy.data.objects.new(name, me); COL.objects.link(o)
    me.materials.append(mat)
    if thick > 0:
        so = o.modifiers.new("厚み", 'SOLIDIFY'); so.thickness = thick; so.offset = 0.0; so.use_even_offset = True; so.use_rim = True
    if sub > 0:
        sd = o.modifiers.new("なめらか", 'SUBSURF'); sd.levels = 1; sd.render_levels = sub
    if parent_bone:
        mw = o.matrix_world.copy()
        o.parent = arm; o.parent_type = 'BONE'; o.parent_bone = parent_bone
        bpy.context.view_layer.update(); o.matrix_world = mw
    if line and mat is not M['neon']: outline(o)
    TRIS[0] += len(me.polygons)
    return o

def grid(nu, nv, P, closed=False):
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new()
    cols = nu if closed else nu + 1
    V = [[bm.verts.new(P(i / nu, j / nv)) for j in range(nv + 1)] for i in range(cols)]
    for i in range(nu):
        i2 = (i + 1) % cols
        for j in range(nv):
            f = bm.faces.new((V[i][j], V[i2][j], V[i2][j + 1], V[i][j + 1]))
            uvs = ((i / nu, j / nv), ((i + 1) / nu, j / nv), ((i + 1) / nu, (j + 1) / nv), (i / nu, (j + 1) / nv))
            for lp, uv in zip(f.loops, uvs): lp[uvl].uv = uv
    bm.normal_update()
    return bm

def surf(name, nu, nv, P, mat, bone, thick=0.008, closed=False, sub=2, line=True):
    try: return make_obj(name, grid(nu, nv, P, closed), mat, bone, thick, sub, line)
    except Exception as e: log("ERR", name, e, traceback.format_exc()[-400:])

def tube(name, n_around, n_along, C, R, mat, bone, sub=1, line=True):
    def P(u, v):
        t = v; c = C(t); e = 0.002
        d = (C(min(1, t + e)) - C(max(0, t - e))).normalized()
        s = d.cross(Vector((0, 0, 1)))
        if s.length < 0.1: s = d.cross(Vector((0, 1, 0)))
        s.normalize(); up = s.cross(d).normalized()
        r = R(t); a = u * 2 * math.pi
        return c + s * math.cos(a) * r[0] + up * math.sin(a) * r[1]
    try:
        bm = grid(n_around, n_along, P, closed=True)
        bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
        return make_obj(name, bm, mat, bone, 0, sub, line)
    except Exception as e: log("ERR", name, e, traceback.format_exc()[-400:])

def L(a, b, t): return a + (b - a) * t
def smooth01(x): x = max(0.0, min(1.0, x)); return x * x * (3 - 2 * x)

# ================= 胴（剣道の胴を、白い漆の一枚板に） =================
log("do")
def do_pt(ang, t, grow=0.0):
    rx, ry, cy = torso(0.06 + 0.9 * t)
    bulge = 0.055 * math.cos(ang) * math.sin(math.pi * min(1, t * 1.15))   # 前に丸く張り出す
    rx += 0.045 + grow; ry += 0.04 + grow + bulge
    z = waistZ - 0.02 + (chestTop - 0.02 - (waistZ - 0.02)) * t
    return Vector((math.sin(ang) * rx, cy - math.cos(ang) * ry, z))
surf("胴", 28, 14, lambda u, v: do_pt(math.radians(-82 + 164 * u), v), M['shiro'], "Spine", thick=0.012)
# 胴の縁（朱）と、胸の斜めのネオン線
tube("胴_縁上", 8, 40, lambda t: do_pt(math.radians(-82 + 164 * t), 1.0, 0.004), lambda t: (0.009, 0.009), M['aka'], "Spine")
tube("胴_縁下", 8, 40, lambda t: do_pt(math.radians(-82 + 164 * t), 0.0, 0.004), lambda t: (0.009, 0.009), M['aka'], "Spine")
for k, (a0, a1, t0, t1) in enumerate(((-38, 10, 0.78, 0.30), (-16, 32, 0.84, 0.36))):
    tube("胴_ネオン%d" % k, 6, 24, lambda t, a0=a0, a1=a1, t0=t0, t1=t1: do_pt(math.radians(L(a0, a1, t)), L(t0, t1, t), 0.012), lambda t: (0.0045, 0.0045), M['neon'], "Spine", line=False)

# ================= 垂（腰の大きな板） =================
log("tare")
rx0, ry0, cy0 = torso(0.0)
def waist_pt(ang, z, grow=0.0):
    return Vector((math.sin(ang) * (rx0 + 0.07 + grow), cy0 - math.cos(ang) * (ry0 + 0.065 + grow), z))
surf("垂_帯", 36, 2, lambda u, v: waist_pt(u * 2 * math.pi, waistZ - 0.04 + 0.075 * v), M['kuro'], "Spine", thick=0.01, closed=True)
for k, (ang, wdt, ln, mat) in enumerate(((-34, 0.16, 0.30, M['kon']), (0, 0.17, 0.36, M['kon']), (34, 0.16, 0.30, M['kon']), (-82, 0.15, 0.28, M['kon']), (82, 0.15, 0.28, M['kon']))):
    a = math.radians(ang)
    def Pt(u, v, a=a, wdt=wdt, ln=ln):
        top = waist_pt(a, waistZ - 0.03, 0.012)
        side = Vector((math.cos(a), math.sin(a), 0))
        out = Vector((math.sin(a), -math.cos(a), 0))
        return top + side * ((u - 0.5) * wdt) + out * (0.02 + 0.06 * v * v) + Vector((0, 0, -ln * v))
    surf("垂_%d" % k, 6, 6, Pt, mat, "Spine", thick=0.012)
# 真ん中の垂に朱の名札
def tag(u, v):
    top = waist_pt(0, waistZ - 0.08, 0.03)
    return top + Vector((((u - 0.5) * 0.07), -0.018 - 0.04 * v * v, -0.2 * v))
surf("垂_名札", 2, 4, tag, M['aka'], "Spine", thick=0.006)

# ================= 袴（膝から下で大きく広がる） =================
log("hakama")
for pre, HP, KN, AN in (("Left_", HPL, KNL, ANL), ("Right_", HPR, KNR, ANR)):
    out_x = 1 if HP.x > 0 else -1
    def Cl(t, HP=HP, KN=KN, AN=AN, out_x=out_x):
        if t < 0.5: c = L(HP + Vector((0, 0, 0.06)), KN, t * 2)
        else: c = L(KN, AN + Vector((0, 0, 0.02)), (t - 0.5) * 2)
        return c + Vector((out_x * (0.012 + 0.035 * t * t), 0, 0))
    def Rl(t):
        r = 0.105 + 0.03 * t + 0.04 * smooth01((t - 0.55) / 0.45)
        return (r, r * 1.15)
    tube("袴_" + pre[:-1], 20, 16, Cl, Rl, M['kon'], pre + "UpperLeg", sub=1)
    # 裾の朱の線
    tube("袴裾_" + pre[:-1], 20, 1, lambda t, Cl=Cl: Cl(0.985 + 0.015 * t), lambda t: (Rl(1)[0] + 0.004, Rl(1)[1] + 0.004), M['aka'], pre + "UpperLeg", sub=1)

# ================= 面（剣道の面を、つるりとした黒い兜に。目にネオンの一文字） =================
log("men")
def head_pt(lat, lon, grow=0.0):
    r = HR + 0.02 + grow
    return HEADC + Vector((math.sin(lat) * math.sin(lon) * r * 1.0, -math.sin(lat) * math.cos(lon) * r * 1.1, math.cos(lat) * r * 1.12))
surf("面_兜", 28, 12, lambda u, v: head_pt(math.radians(2 + 108 * v), u * 2 * math.pi), M['kuro'], "Head", thick=0.01, closed=True)
# 顔を覆う面金の代わりの板（前に少し尖る）
def visor(u, v):
    lon = math.radians(-70 + 140 * u); lat = math.radians(55 + 70 * v)
    p = head_pt(lat, lon, 0.012)
    return p + Vector((0, -0.03 * math.cos(lon * 1.2) * math.sin(math.pi * v), 0))
surf("面_顔板", 18, 8, visor, M['kuro'], "Head", thick=0.008)
tube("面_一文字", 8, 24, lambda t: head_pt(math.radians(78), math.radians(-48 + 96 * t), 0.03) + Vector((0, -0.028 * math.cos(math.radians(-48 + 96 * t) * 1.2), 0)), lambda t: (0.006, 0.0035), M['neon'], "Head", line=False)
# 錣（しころ）：あごの下まで、首まわりを短く覆う
def shikoro(u, v):
    lon = math.radians(70 + 220 * u)
    top = head_pt(math.radians(100), lon, 0.0)
    bot = head_pt(math.radians(118), lon, 0.05) + Vector((0, 0, -0.06))
    return L(top, bot, v)
surf("面_錣", 20, 3, shikoro, M['kuro'], "Head", thick=0.01)
for k, frac in enumerate((0.3, 0.5, 0.7)):
    tube("面_錣線%d" % k, 6, 6, lambda t, frac=frac: shikoro(frac, t) + (shikoro(frac, t) - HEADC).normalized() * 0.008, lambda t: (0.0035, 0.0035), M['aka'], "Head")

# 肩衣（かたぎぬ）：肩から横へ大きく張り出す板。シルエットを逆三角形にする
WING = 0.33
for side in (-1, 1):
    def Pw(u, v, side=side):
        # u: 首→肩の先、v: 前→後ろ
        x = side * L(0.06, shX + WING, u)
        rx, ry, cy = torso(0.95)
        depth = L(ry + 0.07, 0.012, u ** 1.3)            # 外へ行くほど細く尖る
        y = cy + (v - 0.5) * 2 * depth
        z = L(chestTop + 0.02, SHL.z + 0.05, u) + 0.05 * math.sin(math.pi * u * 0.8) - 0.07 * u ** 3   # 先が少し垂れる
        z += 0.025 * (1 - abs(v - 0.5) * 2) * (1 - u)
        return Vector((x, y, z))
    surf("肩衣_%s" % ("右" if side * SIDE < 0 else "左"), 14, 6, Pw, M['kuro'], "UpperChest", thick=0.02)
    # 前身頃：胴の両脇を腰まで下りる黒い帯（肩衣と体をつなぐ）
    surf("肩衣前_%d" % side, 3, 10, lambda u, v, side=side: do_pt(math.radians(side * L(30, 52, u)), 1.0 - 0.85 * v, 0.016), M['kuro'], "Spine", thick=0.01)
    tube("肩衣縁_%d" % side, 8, 20, lambda t, Pw=Pw: Pw(1.0, t) + Vector((side * 0.006, 0, 0)), lambda t: (0.008, 0.008), M['aka'], "UpperChest")
    tube("肩衣ネオン_%d" % side, 6, 20, lambda t, Pw=Pw: Pw(0.35 + 0.6 * t, 0.12) + Vector((0, 0, 0.012)), lambda t: (0.0035, 0.0035), M['neon'], "UpperChest", line=False)
# 面紐：後ろへ長く流れる朱の紐（風になびく）
for k, side in enumerate((-1, 1)):
    def Ch(t, side=side):
        base = head_pt(math.radians(95), math.pi + side * 0.25, 0.02)
        return base + Vector((side * 0.05 * t + 0.12 * t * t, 0.22 * t + 0.06 * math.sin(t * 5), -0.75 * t))
    tube("面紐_%d" % k, 6, 30, Ch, lambda t: (0.012 * (1 - 0.4 * t), 0.004), M['aka'], "Head")

# ================= 左腕は巻き布（白） =================
for k in range(6):
    t0 = 0.12 + k * 0.13
    a = L(SHL, ELL, t0); b = L(SHL, ELL, t0 + 0.09)
    r = seg_radius(SHL, ELL, "Left_UpperArm") + 0.012
    tube("巻布_%d" % k, 14, 2, lambda t, a=a, b=b: L(a, b, t) + Vector((0, 0, 0.012 * (t - 0.5))), lambda t, r=r: (r, r), M['shiro'], "Left_UpperArm", sub=1)

# ================= 小手（大きくて四角い、剣道の小手） =================
log("kote")
for pre, EL, WR in (("Left_", ELL, WRL), ("Right_", ELR, WRR)):
    r = seg_radius(EL, WR, pre + "LowerArm") + 0.022
    tube("小手_" + pre[:-1], 14, 8, lambda t, EL=EL, WR=WR: L(EL, WR, 0.25 + 0.75 * t), lambda t, r=r: (r * (1.35 - 0.35 * t), r * (1.3 - 0.3 * t)), M['kuro'], pre + "LowerArm", sub=1)
    tube("小手筒_" + pre[:-1], 14, 2, lambda t, EL=EL, WR=WR: L(EL, WR, 0.2 + 0.08 * t), lambda t, r=r: (r * 1.5, r * 1.45), M['aka'], pre + "LowerArm", sub=1)

# ================= 背中に長い朱の布（なびく2本の帯） =================
log("scarf")
NB = NECK + Vector((0, 0.06, -0.02))
for k, side in enumerate((-1, 1)):
    def Ps(u, v, side=side):
        w = 0.17
        sway = 0.45 * v * v + 0.06 * math.sin(v * 5 + side * 1.3)
        base = NB + Vector((side * (0.06 + 0.04 * v) + sway, 0.06 + 0.42 * v * v, -1.0 * v + 0.12 * v * v))
        return base + Vector(((u - 0.5) * w * (1 - 0.25 * v), 0, 0))
    surf("帯_%d" % k, 3, 20, Ps, M['aka'], "UpperChest", thick=0.005)

# ================= 白い髷（兜の後ろから長くなびく） =================
log("mage")
MB = head_pt(math.radians(42), math.pi, -0.005)
def Cm(t):
    return MB + Vector((0.16 * t * t, 0.09 * t + 0.08 * t * t, 0.04 * t - 0.66 * t * t))
log("mage pts", tuple(round(x,3) for x in Cm(0)), tuple(round(x,3) for x in Cm(1)), "HEADC", tuple(round(x,3) for x in HEADC))
tube("髷", 12, 30, Cm, lambda t: (0.038 * (1 - 0.7 * t) + 0.006, 0.03 * (1 - 0.7 * t) + 0.005), M['shiro'], "Head", sub=1)
tube("髷_元結", 12, 2, lambda t: Cm(0.03 + 0.05 * t), lambda t: (0.045, 0.038), M['aka'], "Head", sub=1)

# ================= 右の上腕は黒い袖 =================
r_ua = seg_radius(SHR, ELR, "Right_UpperArm") + 0.024
tube("袖_右", 14, 6, lambda t: L(SHR, ELR, 0.05 + 0.85 * t), lambda t: (r_ua * (1 + 0.25 * t), r_ua * (1 + 0.25 * t)), M['kuro'], "Right_UpperArm", sub=1)

# ================= ポーズ（腕を下ろし、右手に刀を下段で） =================
pose_log = {}
for pre, SH, want in (("Left_", SHL, 74.0), ("Right_", SHR, 66.0)):
    try:
        ua = pre + "UpperArm"; hand = pre + "Hand"
        pb = arm.pose.bones[ua]; pb.rotation_mode = 'QUATERNION'
        v = bh(hand) - SH
        cur = math.degrees(math.atan2(-v.z, math.hypot(v.x, v.y)))
        axis_arm = arm.matrix_world.inverted().to_3x3() @ Vector((0, 1, 0))
        axis_bone = (arm.data.bones[ua].matrix_local.to_3x3().inverted() @ axis_arm).normalized()
        best = None
        for sgn in (1, -1):
            pb.rotation_quaternion = Quaternion(axis_bone, sgn * math.radians(want - cur))
            bpy.context.view_layer.update()
            z = (arm.matrix_world @ arm.pose.bones[hand].head).z
            if best is None or z < best[1]: best = (sgn, z)
        pb.rotation_quaternion = Quaternion(axis_bone, best[0] * math.radians(want - cur))
        la = pre + "LowerArm"; pl = arm.pose.bones[la]; pl.rotation_mode = 'QUATERNION'
        axis2 = (arm.data.bones[la].matrix_local.to_3x3().inverted() @ (arm.matrix_world.inverted().to_3x3() @ Vector((1, 0, 0)))).normalized()
        bpy.context.view_layer.update()
        best2 = None
        for sgn in (1, -1):
            pl.rotation_quaternion = Quaternion(axis2, sgn * math.radians(22))
            bpy.context.view_layer.update()
            y = (arm.matrix_world @ arm.pose.bones[hand].head).y
            if best2 is None or y < best2[1]: best2 = (sgn, y)
        pl.rotation_quaternion = Quaternion(axis2, best2[0] * math.radians(22))
        bpy.context.view_layer.update()
        pose_log[pre] = round(cur, 1)
    except Exception as e: log("ERR pose", pre, e)
log("pose", pose_log)

# ================= 刀（細く長い。刃にネオンの線） =================
log("katana")
try:
    hpb = arm.pose.bones["Right_Hand"]
    hh = arm.matrix_world @ hpb.head; ht = arm.matrix_world @ hpb.tail
    grip = L(hh, ht, 0.55)
    rsx = 1 if grip.x > 0 else -1
    bd = Vector((rsx * 0.22, -0.62, -0.62)).normalized()          # 下段：切っ先は前の下へ
    sori = Vector((0, 0, 1)) - bd * bd.z; sori.normalize()
    BL = 0.92
    base = grip + bd * 0.12
    Cb = lambda t: base + bd * (t * BL) + sori * (0.035 * math.sin(t * math.pi * 0.9) * BL * t)
    tube("刀_刃", 4, 30, Cb, lambda t: (0.003 + 0.002 * (1 - t), 0.017 * (1 - 0.65 * t ** 3)), M['ha'], "Right_Hand", sub=0)
    tube("刀_刃ネオン", 4, 30, lambda t: Cb(t) - sori * 0.017 * (1 - 0.65 * t ** 3), lambda t: (0.0035, 0.0035), M['neon'], "Right_Hand", sub=0, line=False)
    tube("刀_鍔", 20, 1, lambda t: grip + bd * (0.1 + 0.015 * t), lambda t: (0.045, 0.04), M['aka'], "Right_Hand", sub=1)
    tube("刀_柄", 12, 6, lambda t: grip + bd * (0.1 - 0.3 * t), lambda t: (0.016, 0.013), M['kuro'], "Right_Hand", sub=1)
    tube("刀_頭", 12, 1, lambda t: grip - bd * (0.2 + 0.015 * t), lambda t: (0.018, 0.015), M['kin'], "Right_Hand", sub=1)
except Exception as e: log("ERR katana", e, traceback.format_exc()[-300:])

# 素体にも輪郭線
outline(body, 0.005)

# ================= 舞台：EEVEE・ネオンの背景・床の輪 =================
sc = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try: sc.render.engine = eng; break
    except Exception: pass
try: sc.eevee.taa_render_samples = 48
except Exception: pass
sc.render.resolution_x, sc.render.resolution_y = 1000, 1400
sc.render.resolution_percentage = 45 if os.environ.get('KENSHI_QUICK') else 100
try: sc.view_settings.view_transform = 'Standard'; sc.view_settings.look = 'None'
except Exception: pass
log("engine", sc.render.engine)

world = bpy.data.worlds.new("夜"); sc.world = world; world.use_nodes = True
bg = world.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.01, 0.01, 0.03, 1); bg.inputs[1].default_value = 0.4

mins = Vector((1e9, 1e9, 1e9)); maxs = Vector((-1e9, -1e9, -1e9))
for o in COL.objects:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c); mins = Vector(map(min, mins, w)); maxs = Vector(map(max, maxs, w))
groundZ = min(p.z for p in BV)
H = maxs.z - groundZ; ctr = (mins + maxs) / 2

# 床：濃い藍の円盤と、足元のネオンの輪
bpy.ops.mesh.primitive_circle_add(vertices=96, radius=8, fill_type='NGON', location=(ctr.x, ctr.y, groundZ))
floor = bpy.context.active_object; floor.name = "床"
floor.data.materials.append(toon("床", (0.03, 0.03, 0.07), (0.012, 0.012, 0.035), rim_k=0))
bpy.ops.mesh.primitive_torus_add(major_radius=0.85, minor_radius=0.008, location=(ctr.x, ctr.y, groundZ + 0.004))
ring = bpy.context.active_object; ring.name = "床の輪"; ring.data.materials.append(M['neon'])

# 光：右上前からの1灯（影が2段にきれいに割れるように）
sun_d = bpy.data.lights.new("太陽", 'SUN'); sun_d.energy = 3.2; sun_d.angle = math.radians(3)
sun = bpy.data.objects.new("太陽", sun_d); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(52), 0, math.radians(-38))

cam_d = bpy.data.cameras.new("カメラ"); cam_d.lens = 70
cam = bpy.data.objects.new("カメラ", cam_d); sc.collection.objects.link(cam); sc.camera = cam
pivot = bpy.data.objects.new("カメラ軸", None); sc.collection.objects.link(pivot)
pivot.location = (ctr.x, ctr.y, groundZ)
cam.parent = pivot

# 背景：カメラと一緒に回る板。下は濃紺、上は紫のグラデーション
bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=1.0)
bdm = bpy.data.meshes.new("背景"); bm.to_mesh(bdm); bm.free()
back = bpy.data.objects.new("背景", bdm); sc.collection.objects.link(back)
back.parent = pivot; back.location = (0, 3.5, H * 0.6); back.rotation_euler = (math.radians(90), 0, 0); back.scale = (7, 5, 1)
bmat = bpy.data.materials.new("背景"); bmat.use_nodes = True; nt = bmat.node_tree
for n in list(nt.nodes): nt.nodes.remove(n)
o_ = nt.nodes.new('ShaderNodeOutputMaterial'); tc = nt.nodes.new('ShaderNodeTexCoord')
sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['Generated'], sep.inputs[0])
rp = nt.nodes.new('ShaderNodeValToRGB'); nt.links.new(sep.outputs['Y'], rp.inputs['Fac'])
rp.color_ramp.elements[0].position = 0.25; rp.color_ramp.elements[0].color = (0.012, 0.014, 0.05, 1)
rp.color_ramp.elements[1].position = 0.95; rp.color_ramp.elements[1].color = (0.24, 0.05, 0.3, 1)
em = nt.nodes.new('ShaderNodeEmission'); nt.links.new(rp.outputs['Color'], em.inputs['Color'])
nt.links.new(em.outputs[0], o_.inputs['Surface'])
back.data.materials.append(bmat)
log("scene ok H", round(H, 3), "tris", TRIS[0])

shots = [("hero", -32, 0.22, 0.52, 1.0), ("front", 0, 0.5, 0.5, 1.05), ("side", 90, 0.5, 0.5, 1.05), ("back", 180, 0.5, 0.5, 1.05)]
for name, ang, camh, aimh, distk in shots:
    pivot.rotation_euler = (0, 0, math.radians(ang))
    dist = H * 2.55 * distk
    cam.location = (0, -dist, H * camh)
    look = Vector((0, 0, H * aimh)) - Vector(cam.location)
    cam.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(OUT, "kenshi_" + name + ".png")
    log("render", name)
    try: bpy.ops.render.render(write_still=True)
    except Exception as e: log("ERR render", e)
pivot.rotation_euler = (0, 0, 0)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "Kenshi_Furi.blend"))
log("done")
