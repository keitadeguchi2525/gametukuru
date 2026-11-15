# -*- coding: utf-8 -*-
# 武士 v2：体格（骨と素体）だけ引き継いで、鎧・兜・服をすべて Blender で作り直す
#  ・曲面の板は「厚み（Solidify）→ 細分化（Subdivision）」で縁の丸い本物の板にする
#  ・漆・威糸・金具・鉄・布は Cycles の手続き的マテリアル（擦れ・槌目・織り目）
#  ・出力：Blender/Samurai_v2.blend と Blender/Renders/v2_*.png
import bpy, bmesh, os, math, json, traceback, random
from mathutils import Vector, Matrix, Quaternion

ROOT = os.path.join(os.path.expanduser("~"), "インフィニティブレード", "Blender")
FBX = os.path.join(ROOT, "Export", "SamuraiCharacter.fbx")
OUT = os.path.join(ROOT, "Renders"); os.makedirs(OUT, exist_ok=True)
LOG = os.path.join(OUT, "v2_log.txt"); open(LOG, "w", encoding="utf-8").close()
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

# ================= マテリアル =================
def new_mat(name):
    m = bpy.data.materials.new(name); m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial'); out.location = (600, 0)
    b = nt.nodes.new('ShaderNodeBsdfPrincipled'); b.location = (300, 0)
    nt.links.new(b.outputs['BSDF'], out.inputs['Surface'])
    return m, nt, b
def sock(n, name, typ, out=False):
    for s in (n.outputs if out else n.inputs):
        if s.name == name and (typ is None or s.type == typ): return s
    return (n.outputs if out else n.inputs)[name]
def mix_rgb(nt, fac, a, b):
    n = nt.nodes.new('ShaderNodeMix'); n.data_type = 'RGBA'
    nt.links.new(fac, sock(n, 'Factor', 'VALUE'))
    if isinstance(a, tuple): sock(n, 'A', 'RGBA').default_value = (*a, 1)
    else: nt.links.new(a, sock(n, 'A', 'RGBA'))
    if isinstance(b, tuple): sock(n, 'B', 'RGBA').default_value = (*b, 1)
    else: nt.links.new(b, sock(n, 'B', 'RGBA'))
    return sock(n, 'Result', 'RGBA', True)
def maprange(nt, val, lo, hi, a=0.0, b=1.0, clamp=True):
    n = nt.nodes.new('ShaderNodeMapRange'); n.clamp = clamp
    nt.links.new(val, n.inputs['Value'])
    n.inputs['From Min'].default_value = lo; n.inputs['From Max'].default_value = hi
    n.inputs['To Min'].default_value = a; n.inputs['To Max'].default_value = b
    return n.outputs['Result']
def noise(nt, scale, detail=6, rough=0.55, coord=None):
    n = nt.nodes.new('ShaderNodeTexNoise')
    n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail
    try: n.inputs['Roughness'].default_value = rough
    except Exception: pass
    if coord is not None: nt.links.new(coord, n.inputs['Vector'])
    return n.outputs['Fac']
def bump(nt, height, strength, dist=0.002, normal=None):
    n = nt.nodes.new('ShaderNodeBump'); n.inputs['Strength'].default_value = strength
    try: n.inputs['Distance'].default_value = dist
    except Exception: pass
    nt.links.new(height, n.inputs['Height'])
    if normal is not None: nt.links.new(normal, n.inputs['Normal'])
    return n.outputs['Normal']
def mth(nt, op, a, b=None):
    n = nt.nodes.new('ShaderNodeMath'); n.operation = op
    if isinstance(a, float) or isinstance(a, int): n.inputs[0].default_value = a
    else: nt.links.new(a, n.inputs[0])
    if b is not None:
        if isinstance(b, float) or isinstance(b, int): n.inputs[1].default_value = b
        else: nt.links.new(b, n.inputs[1])
    return n.outputs[0]
def setin(b, name, v):
    try: b.inputs[name].default_value = v
    except Exception: pass

def lacquer(name, base, worn=(0.11, 0.05, 0.03)):
    # 漆：深い艶、角が擦れて下地が出る、うっすらムラ
    m, nt, b = new_mat(name)
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    edge = maprange(nt, geo.outputs['Pointiness'], 0.52, 0.58)
    nz = noise(nt, 18, 8, 0.6)
    wear = mth(nt, 'MULTIPLY', edge, maprange(nt, nz, 0.35, 0.65))
    col = mix_rgb(nt, wear, base, worn)
    nt.links.new(col, b.inputs['Base Color'])
    nt.links.new(maprange(nt, wear, 0, 1, 0.16, 0.55), b.inputs['Roughness'])
    setin(b, 'Coat Weight', 0.6); setin(b, 'Coat Roughness', 0.08)
    nt.links.new(bump(nt, noise(nt, 140, 4), 0.04), b.inputs['Normal'])
    return m
def metal(name, base, rough=0.38, hammer=0.25, dark=0.5):
    m, nt, b = new_mat(name)
    nz = noise(nt, 6, 6)
    col = mix_rgb(nt, maprange(nt, nz, 0.3, 0.7), tuple(c * dark for c in base), base)
    nt.links.new(col, b.inputs['Base Color'])
    b.inputs['Metallic'].default_value = 1.0
    nt.links.new(maprange(nt, noise(nt, 30, 5), 0.3, 0.7, rough - 0.12, rough + 0.15), b.inputs['Roughness'])
    vor = nt.nodes.new('ShaderNodeTexVoronoi'); vor.inputs['Scale'].default_value = 90
    nt.links.new(bump(nt, sock(vor, 'Distance', None, True), hammer, 0.003), b.inputs['Normal'])
    return m
def cloth(name, base, sheen=0.4, weave=220):
    m, nt, b = new_mat(name)
    nz = noise(nt, 4, 5)
    col = mix_rgb(nt, maprange(nt, nz, 0.3, 0.7), tuple(c * 0.75 for c in base), base)
    nt.links.new(col, b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.92
    setin(b, 'Sheen Weight', sheen); setin(b, 'Sheen Roughness', 0.4)
    tc = nt.nodes.new('ShaderNodeTexCoord')
    wv = nt.nodes.new('ShaderNodeTexWave'); wv.inputs['Scale'].default_value = weave
    nt.links.new(tc.outputs['Object'], wv.inputs['Vector'])
    nt.links.new(bump(nt, wv.outputs['Fac'], 0.08, 0.001), b.inputs['Normal'])
    return m
def laced(name, lac, silk, cols_per_unit=1.0):
    # 威（おどし）：漆の小札に、絹の組紐が縦に密に通る。UV の U＝弧長/間隔、V＝板の上下
    m, nt, b = new_mat(name)
    tc = nt.nodes.new('ShaderNodeTexCoord')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['UV'], sep.inputs[0])
    fu = mth(nt, 'FRACT', sep.outputs['X'])
    band = mth(nt, 'MULTIPLY', mth(nt, 'GREATER_THAN', fu, 0.22), mth(nt, 'LESS_THAN', fu, 0.78))
    vmask = mth(nt, 'LESS_THAN', sep.outputs['Y'], 0.8)
    mask = mth(nt, 'MULTIPLY', band, vmask)
    # 組紐の斜めの編み目
    diag = mth(nt, 'FRACT', mth(nt, 'ADD', mth(nt, 'MULTIPLY', sep.outputs['Y'], 9.0), mth(nt, 'MULTIPLY', fu, 1.5)))
    weave = maprange(nt, mth(nt, 'SINE', mth(nt, 'MULTIPLY', diag, 6.283)), -1, 1, 0.7, 1.0)
    silkc = mix_rgb(nt, weave, tuple(c * 0.55 for c in silk), silk)
    geo = nt.nodes.new('ShaderNodeNewGeometry')
    edge = maprange(nt, geo.outputs['Pointiness'], 0.52, 0.58)
    lacc = mix_rgb(nt, edge, lac, (0.1, 0.05, 0.03))
    col = mix_rgb(nt, mask, lacc, silkc)
    nt.links.new(col, b.inputs['Base Color'])
    nt.links.new(maprange(nt, mask, 0, 1, 0.2, 0.75), b.inputs['Roughness'])
    setin(b, 'Sheen Weight', 0.3)
    h = mth(nt, 'MULTIPLY', mask, weave)
    nt.links.new(bump(nt, h, 0.5, 0.003), b.inputs['Normal'])
    return m
def tsuka_mat(name):
    # 柄巻：菱形に巻いた黒い組紐と、白い鮫皮
    m, nt, b = new_mat(name)
    tc = nt.nodes.new('ShaderNodeTexCoord')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ'); nt.links.new(tc.outputs['UV'], sep.inputs[0])
    a = mth(nt, 'FRACT', mth(nt, 'ADD', mth(nt, 'MULTIPLY', sep.outputs['X'], 4.0), mth(nt, 'MULTIPLY', sep.outputs['Y'], 9.0)))
    c = mth(nt, 'FRACT', mth(nt, 'SUBTRACT', mth(nt, 'MULTIPLY', sep.outputs['X'], 4.0), mth(nt, 'MULTIPLY', sep.outputs['Y'], 9.0)))
    d1 = mth(nt, 'ABSOLUTE', mth(nt, 'SUBTRACT', a, 0.5)); d2 = mth(nt, 'ABSOLUTE', mth(nt, 'SUBTRACT', c, 0.5))
    diamond = mth(nt, 'LESS_THAN', mth(nt, 'ADD', d1, d2), 0.42)
    col = mix_rgb(nt, diamond, (0.012, 0.011, 0.012), (0.75, 0.72, 0.64))
    nt.links.new(col, b.inputs['Base Color'])
    b.inputs['Roughness'].default_value = 0.6
    nt.links.new(bump(nt, mth(nt, 'SUBTRACT', 1.0, diamond), 0.6, 0.002), b.inputs['Normal'])
    return m

M = {}
M['kuro'] = lacquer("漆_黒", (0.014, 0.012, 0.011))
M['aka'] = lacquer("漆_朱", (0.2, 0.012, 0.01), worn=(0.05, 0.02, 0.015))
M['odoshi'] = laced("威_紺赤", (0.016, 0.014, 0.013), (0.2, 0.012, 0.014))
M['odoshi_k'] = laced("威_黒", (0.016, 0.014, 0.013), (0.05, 0.05, 0.06))
M['kin'] = metal("金具_真鍮", (0.62, 0.45, 0.2), rough=0.32, hammer=0.1, dark=0.45)
M['tetsu'] = metal("鉄", (0.12, 0.12, 0.13), rough=0.45, hammer=0.35, dark=0.4)
M['ai'] = cloth("布_藍", (0.018, 0.022, 0.04))
M['sumi'] = cloth("布_墨", (0.022, 0.021, 0.024), sheen=0.2)
M['beni'] = cloth("絹_紅", (0.28, 0.02, 0.025), sheen=0.6, weave=400)
M['kuro_nuno'] = cloth("布_黒", (0.008, 0.009, 0.014), sheen=0.35, weave=300)
M['tsuka'] = tsuka_mat("柄巻")
for s in body.material_slots: s.material = M['sumi']
log("materials ok")

# ================= 形を作る道具 =================
COL = bpy.data.collections.new("鎧"); bpy.context.scene.collection.children.link(COL)
TRIS = [0]

def make_obj(name, bm, mat, parent_bone=None, thick=0.0, sub=2, crease=0.6, smooth=True):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = smooth
    o = bpy.data.objects.new(name, me); COL.objects.link(o)
    me.materials.append(mat)
    if thick > 0:
        so = o.modifiers.new("厚み", 'SOLIDIFY'); so.thickness = thick; so.offset = 0.0; so.use_even_offset = True
        so.use_rim = True
        try: so.edge_crease_outer = crease; so.edge_crease_inner = crease; so.edge_crease_rim = crease
        except Exception: pass
    if sub > 0:
        sd = o.modifiers.new("なめらか", 'SUBSURF'); sd.levels = 1; sd.render_levels = sub
    if parent_bone:
        mw = o.matrix_world.copy()
        o.parent = arm; o.parent_type = 'BONE'; o.parent_bone = parent_bone
        bpy.context.view_layer.update()
        o.matrix_world = mw
    TRIS[0] += len(me.polygons)
    return o

def grid(nu, nv, P, closed=False, uv_arc=0.0):
    """P(u,v) -> Vector（ワールド座標）。uv_arc>0 なら U を弧長/uv_arc にする（威糸の間隔をそろえる）"""
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new()
    cols = nu if closed else nu + 1
    V = [[bm.verts.new(P(i / nu, j / nv)) for j in range(nv + 1)] for i in range(cols)]
    ucoord = [i / nu for i in range(nu + 1)]
    if uv_arc > 0:
        mid = nv // 2; acc = 0; ucoord = [0.0]
        for i in range(1, nu + 1):
            acc += (P(i / nu, mid / nv) - P((i - 1) / nu, mid / nv)).length; ucoord.append(acc / uv_arc)
    for i in range(nu):
        i2 = (i + 1) % cols
        for j in range(nv):
            f = bm.faces.new((V[i][j], V[i2][j], V[i2][j + 1], V[i][j + 1]))
            uvs = ((ucoord[i], j / nv), (ucoord[i + 1], j / nv), (ucoord[i + 1], (j + 1) / nv), (ucoord[i], (j + 1) / nv))
            for lp, uv in zip(f.loops, uvs): lp[uvl].uv = uv
    bm.normal_update()
    return bm

def surf(name, nu, nv, P, mat, bone, thick=0.006, closed=False, uv_arc=0.0, sub=2, crease=0.6):
    try:
        return make_obj(name, grid(nu, nv, P, closed, uv_arc), mat, bone, thick, sub, crease)
    except Exception as e:
        log("ERR", name, e, traceback.format_exc()[-400:])

def tube(name, n_around, n_along, C, R, mat, bone, sub=1, uvx=1.0):
    """中心線 C(t)、断面の半径 R(t)->(横, 縦)。両端はふさぐ"""
    def P(u, v):
        t = v
        c = C(t); e = 0.002
        d = (C(min(1, t + e)) - C(max(0, t - e))).normalized()
        s = d.cross(Vector((0, 0, 1)))
        if s.length < 0.1: s = d.cross(Vector((0, 1, 0)))
        s.normalize(); up = s.cross(d).normalized()
        r = R(t); a = u * 2 * math.pi
        return c + s * math.cos(a) * r[0] + up * math.sin(a) * r[1]
    try:
        bm = grid(n_around, n_along, P, closed=True)
        uvl = bm.loops.layers.uv.active
        for f in bm.faces:
            for lp in f.loops: lp[uvl].uv.x *= uvx
        bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
        return make_obj(name, bm, mat, bone, 0, sub)
    except Exception as e:
        log("ERR", name, e, traceback.format_exc()[-400:])

def frame(a, b, outward):
    d = (b - a).normalized()
    o = outward - d * outward.dot(d); o.normalize()
    f = d.cross(o).normalized()
    return d, o, f

def L(a, b, t): return a + (b - a) * t

# ================= 胴（桶側胴：横板を重ねた胴） =================
log("do")
NL = 7
def torso_pt(ang, t, grow=0.0):
    rx, ry, cy = torso(t)
    rx += 0.034 + grow; ry += 0.03 + grow
    front = max(0, -math.cos(ang))  # 正面（-Y）ほど少し張り出す
    return Vector((math.sin(ang) * rx, cy - math.cos(ang) * ry - front * 0.012, 0))
for i in range(NL):
    t0, t1 = i / NL, (i + 1) / NL + 0.035
    def P(u, v, t0=t0, t1=t1, i=i):
        ang = u * 2 * math.pi
        t = t0 + (t1 - t0) * v
        p = torso_pt(ang, t, grow=0.006 * (1 - v))   # 下の縁を少し外へ：板が重なって見える
        p.z = waistZ + (chestTop - waistZ) * t
        return p
    surf("胴_板%d" % i, 48, 3, P, M['kuro'], "Chest" if i < 3 else "UpperChest", thick=0.006, closed=True)
# 胸板（上端の金縁と朱の帯）
def Pm(u, v):
    ang = (u - 0.5) * 2 * math.pi * 0.42
    p = torso_pt(ang, 1.0, 0.012); p.z = chestTop + 0.012 + 0.03 * v + 0.025 * math.cos(ang) ** 2
    return p
surf("胴_胸板", 28, 3, Pm, M['aka'], "UpperChest", thick=0.006)
def Pmk(u, v):
    ang = (u - 0.5) * 2 * math.pi * 0.43
    p = torso_pt(ang, 1.0, 0.016); p.z = chestTop + 0.042 + 0.006 * v + 0.025 * math.cos(ang) ** 2
    return p
surf("胴_胸板金", 28, 1, Pmk, M['kin'], "UpperChest", thick=0.004)
# 鋲（各板の正面に真鍮の鋲を並べる）
try:
    bm = bmesh.new()
    for i in range(NL):
        t = (i + 0.35) / NL
        for k in range(-5, 6):
            ang = k * 13 * D2R + math.pi
            p = torso_pt(ang + math.pi, t, 0.012); p.z = waistZ + (chestTop - waistZ) * t
            r = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.0065)
            bmesh.ops.translate(bm, verts=r['verts'], vec=p)
    make_obj("胴_鋲", bm, M['kin'], "Chest", 0, 1)
except Exception as e: log("ERR rivets", e)
# 肩上（わたがみ）
for s in (1, -1):
    sx = SIDE * s
    pts = []
    rxT, ryT, cyT = torso(1.0)
    def Pw(u, v, sx=sx):
        y = cyT + (u - 0.5) * 2 * (ryT + 0.05)
        zt = chestTop + 0.05 + 0.06 * math.cos((u - 0.5) * math.pi) ** 1.5
        x = sx * (shX * 0.42 + (v - 0.5) * 0.11)
        return Vector((x, y, zt + 0.012 * math.sin(v * math.pi)))
    surf("胴_肩上" + ("L" if s > 0 else "R"), 16, 3, Pw, M['kuro'], "UpperChest", thick=0.007)
# 背中の総角（あげまき）
try:
    rxT, ryT, cyT = torso(0.62)
    bc = Vector((0, cyT + ryT + 0.05, waistZ + (chestTop - waistZ) * 0.62))
    for s in (-1, 1):
        tube("胴_総角輪" + str(s), 10, 24, lambda t, s=s: bc + Vector((s * (0.05 * (1 - math.cos(t * 2 * math.pi))), 0.005, 0.04 * math.sin(t * 2 * math.pi))), lambda t: (0.007, 0.007), M['beni'], "UpperChest")
        tube("胴_総角房" + str(s), 10, 12, lambda t, s=s: bc + Vector((s * (0.012 + 0.05 * t), 0.008 + 0.01 * t, -0.012 - 0.2 * t)), lambda t: (0.008 - 0.004 * t, 0.006), M['beni'], "UpperChest")
except Exception as e: log("ERR agemaki", e)
log("do done")

# ================= 陣羽織（背中を覆う袖なしの羽織）と家紋 =================
USE_JINBAORI = False   # 試したが板っぽく見えるので、いったん外す
try:
    if not USE_JINBAORI: raise StopIteration
    rxT, ryT, cyT = torso(0.9)
    backY = cyT + ryT + 0.07
    KB = HIP[2] + HIP[1] + 0.07 + 5 * 0.019 + 0.04          # 草摺の後ろより外
    topZ = chestTop + 0.05; botZ = KNL.z - 0.06
    W0, W1 = 0.40, 0.66
    def Pj(u, v, side):
        # side=-1 左身頃（u: 外→中心）、side=1 右身頃（u: 中心→外）
        x = side * (0.004 + u * (W0 + (W1 - W0) * v) / 2)
        bul = (KB - backY) * min(1.0, max(0.0, (v - 0.12) / 0.3)) ** 0.7 if KB > backY else 0.0
        y = backY + bul + 0.02 * v - 0.11 * (u ** 2) * (0.4 + 0.6 * v)    # 外側ほど体へ回り込む
        y += 0.012 * math.sin(u * 7 + v * 5 + side) * v                      # 布のうねり
        z = topZ - v * (topZ - botZ) + 0.01 * u * u
        if v < 0.1: y -= (0.1 - v) * 0.7                                      # 肩へ掛かる
        return Vector((x, y, z))
    for side in (-1, 1):
        surf("陣羽織" + ("L" if side < 0 else "R"), 12, 24, lambda u, v, side=side: Pj(u, v, side), M['kuro_nuno'], "UpperChest", thick=0.006, crease=0.2)
        surf("陣羽織_裾" + ("L" if side < 0 else "R"), 12, 1, lambda u, v, side=side: Pj(u, 1.0, side) + Vector((0, 0.003, 0.035 * v)), M['beni'], "UpperChest", thick=0.007, crease=0.2)
        surf("陣羽織_縁" + ("L" if side < 0 else "R"), 1, 24, lambda u, v, side=side: Pj(0.0, v, side) + Vector((side * 0.012 * u, 0.003, 0)), M['beni'], "UpperChest", thick=0.007, crease=0.2)
    # 家紋：丸に三日月（オリジナル）
    mc = Pj(0.0, 0.3, 1) + Vector((-0.004, 0.012, 0))
    surf("家紋_輪", 40, 1, lambda u, v: mc + Vector((math.cos(u * 2 * math.pi) * (0.085 - 0.012 * v), 0, math.sin(u * 2 * math.pi) * (0.085 - 0.012 * v))), M['kin'], "UpperChest", thick=0.004, closed=True, sub=1)
    def Pmo(u, v):
        a = (105 + u * 150) * D2R
        w = 0.026 * math.sin(u * math.pi) ** 0.8 + 0.002
        r = 0.058 - v * w
        return mc + Vector((math.cos(a) * r + 0.012, 0, math.sin(a) * r))
    surf("家紋_月", 30, 1, Pmo, M['kin'], "UpperChest", thick=0.004, sub=1)
except StopIteration: pass
except Exception as e: log("ERR jinbaori", e, traceback.format_exc()[-300:])

# ================= 帯と、背中に垂れる長い帯 =================
def Po(u, v):
    ang = u * 2 * math.pi
    rx, ry, cy = torso(0.0)
    return Vector((math.sin(ang) * (rx + 0.05), cy - math.cos(ang) * (ry + 0.045), waistZ - 0.01 - 0.06 * v))
surf("帯", 48, 3, Po, M['beni'], "Spine", thick=0.006, closed=True)
rx0, ry0, cy0 = torso(0.0)
for s in (-1, 1):
    def Pt(u, v, s=s):
        x = s * (0.04 + 0.06 * v) + (u - 0.5) * (0.075 - 0.02 * v)
        y = cy0 + ry0 + 0.06 + 0.06 * math.sin(v * 2.2) + 0.03 * v
        z = waistZ - 0.03 - v * (waistZ - KNL.z + 0.05)
        return Vector((x + 0.02 * math.sin(v * 7 + s), y, z))
    surf("帯_垂れ" + str(s), 4, 22, Pt, M['beni'], "Spine", thick=0.004, crease=0.2)

# ================= 草摺（腰から垂れる板） =================
log("kusazuri")
NK = 7
for k in range(NK):
    ac = k * 2 * math.pi / NK
    half = math.pi / NK * 1.12
    for i in range(5):
        zT = waistZ - 0.05 - i * 0.052
        def Pk(u, v, ac=ac, half=half, i=i, zT=zT, k=k):
            ang = ac + (u - 0.5) * 2 * half
            j = 0.006 * math.sin(k * 2.3 + i * 1.7)
            gx = HIP[0] + 0.055 + i * 0.019 + v * 0.016 + j
            gy = HIP[1] + 0.07 + i * 0.019 + v * 0.016 + j
            return Vector((math.sin(ang) * gx, HIP[2] - math.cos(ang) * gy, zT - v * 0.062))
        surf("草摺_%d_%d" % (k, i), 12, 3, Pk, M['odoshi'], "Spine", thick=0.006, uv_arc=0.03)
log("kusazuri done")

# ================= 袖・腕 =================
log("arms")
for side, (SH, EL, WR, pre) in {"L": (SHL, ELL, WRL, "Left_"), "R": (SHR, ELR, WRR, "Right_")}.items():
    sx = SIDE if side == "L" else -SIDE
    out = Vector((sx, 0, 0.9)).normalized()
    d, o, f = frame(SH, EL, out)
    ulen = (EL - SH).length
    ru = seg_radius(SH, EL, pre + "UpperArm")
    big = 1.15 if side == "L" else 1.0   # 左の袖を少し大きく（左右非対称）
    # 大袖：腕より大きな半径で平たく、6段
    RC = 0.2 * big
    for i in range(6):
        s0 = -0.05 + i * 0.058 * big
        def Ps(u, v, s0=s0, i=i):
            th = (u - 0.5) * 2 * 0.55
            r = RC + i * 0.004 + v * 0.01
            c = SH + d * (s0 + v * 0.068 * big) - o * (RC - ru - 0.045)
            return c + (o * math.cos(th) + f * math.sin(th)) * r
        surf("袖%s_%d" % (side, i), 14, 3, Ps, M['odoshi'], pre + "UpperArm", thick=0.006, uv_arc=0.03)
    def Pcap(u, v):
        th = (u - 0.5) * 2 * 0.6
        r = RC - 0.01
        c = SH + d * (-0.075 + v * 0.03) - o * (RC - ru - 0.045)
        return c + (o * math.cos(th) + f * math.sin(th)) * (r - 0.02 * (1 - v))
    surf("袖%s_冠板" % side, 14, 3, Pcap, M['kuro'], pre + "UpperArm", thick=0.008)
    # 二の腕の布
    def Pu(u, v):
        a = u * 2 * math.pi; r = ru + 0.012 + 0.012 * math.sin(v * math.pi) + 0.004 * math.sin(a * 7)
        return SH + d * (0.02 + v * ulen) + (o * math.cos(a) + f * math.sin(a)) * r
    surf("袖%s_布" % side, 24, 6, Pu, M['ai'], pre + "UpperArm", thick=0.004, closed=True)
    # 籠手：布の袖、鉄の筏（いかだ）、金の輪、手甲
    d2, o2, f2 = frame(EL, WR, o)
    flen = (WR - EL).length; rf = seg_radius(EL, WR, pre + "LowerArm")
    def Pk(u, v):
        a = u * 2 * math.pi; r = rf + 0.014 + 0.006 * math.sin(v * math.pi)
        return EL + d2 * (v * flen * 0.96) + (o2 * math.cos(a) + f2 * math.sin(a)) * r
    surf("籠手%s_布" % side, 24, 6, Pk, M['ai'], pre + "LowerArm", thick=0.004, closed=True)
    for j in range(5):
        th0 = (j - 2) * 0.42
        def Pi(u, v, th0=th0):
            th = th0 + (u - 0.5) * 0.3
            r = rf + 0.022 + 0.004 * math.sin(v * math.pi)
            return EL + d2 * (0.04 + v * flen * 0.78) + (o2 * math.cos(th) + f2 * math.sin(th)) * r
        surf("籠手%s_筏%d" % (side, j), 3, 6, Pi, M['tetsu'], pre + "LowerArm", thick=0.004)
    for z0 in (0.05, flen * 0.82):
        def Pr(u, v, z0=z0):
            a = u * 2 * math.pi
            return EL + d2 * (z0 + v * 0.016) + (o2 * math.cos(a) + f2 * math.sin(a)) * (rf + 0.028)
        surf("籠手%s_輪%d" % (side, int(z0 * 100)), 24, 1, Pr, M['kin'], pre + "LowerArm", thick=0.004, closed=True)
    hand_tip = bt(pre + "Hand") if (bt(pre + "Hand") - WR).length > 0.03 else WR + d2 * 0.09
    def Ph(u, v):
        th = (u - 0.5) * 2 * 1.0
        return WR + d2 * (-0.01 + v * 0.1) + (o2 * math.cos(th) + f2 * math.sin(th)) * (rf + 0.006 - 0.008 * v)
    surf("籠手%s_手甲" % side, 10, 4, Ph, M['tetsu'], pre + "Hand", thick=0.005)
log("arms done")

# ================= 袴・佩楯・脛当 =================
log("legs")
for side, (HP, KN, AN, pre) in {"L": (HPL, KNL, ANL, "Left_"), "R": (HPR, KNR, ANR, "Right_")}.items():
    sx = SIDE if side == "L" else -SIDE
    rt = seg_radius(HP, KN, pre + "UpperLeg"); rs = seg_radius(KN, AN, pre + "LowerLeg")
    fwd = Vector((0, -1, 0))
    d, o, f = frame(HP, AN, fwd)   # o＝前、f＝横
    llen = (AN - HP).length
    # 袴：裾に向かって大きく広がる。ひだ付き
    def Ph(u, v):
        a = u * 2 * math.pi
        r = rt + 0.025 + 0.05 * math.sin(v * math.pi * 0.75)
        r += 0.01 * math.sin(v * math.pi) * math.sin(a * 6) + 0.004 * math.sin(a * 13)
        p = HP + d * (-0.05 + v * (llen * 0.66)) + (o * math.cos(a) * 1.05 + f * math.sin(a)) * r
        return p
    surf("袴" + side, 40, 10, Ph, M['ai'], pre + "UpperLeg", thick=0.005, closed=True, crease=0.2)
    # 佩楯：太ももの前に垂れる、小札の前掛け
    dT, oT, fT = frame(HP, KN, fwd)
    tl = (KN - HP).length
    for i in range(3):
        def Pd(u, v, i=i):
            th = (u - 0.5) * 2 * 0.8
            r = rt + 0.05 + i * 0.008 + v * 0.008
            return HP + dT * (0.06 + i * 0.07 + v * 0.078) + (oT * math.cos(th) + fT * math.sin(th)) * r
        surf("佩楯%s_%d" % (side, i), 12, 3, Pd, M['odoshi_k'], pre + "UpperLeg", thick=0.005, uv_arc=0.024)
    # 脛当：縦の鉄の筏と、金の帯
    d2, o2, f2 = frame(KN, AN, fwd)
    slen = (AN - KN).length
    for j in range(7):
        th0 = (j - 3) * 0.3
        def Ps(u, v, th0=th0):
            th = th0 + (u - 0.5) * 0.24
            r = rs + 0.03 + 0.01 * math.sin(v * math.pi)
            return KN + d2 * (0.07 + v * slen * 0.84) + (o2 * math.cos(th) + f2 * math.sin(th)) * r
        surf("脛当%s_%d" % (side, j), 3, 8, Ps, M['tetsu'], pre + "LowerLeg", thick=0.005)
    for z0 in (0.12, slen * 0.5, slen * 0.86):
        def Pb(u, v, z0=z0):
            th = (u - 0.5) * 2 * 1.15
            return KN + d2 * (z0 + v * 0.018) + (o2 * math.cos(th) + f2 * math.sin(th)) * (rs + 0.04)
        surf("脛当%s_帯%d" % (side, int(z0 * 100)), 16, 1, Pb, M['kin'], pre + "LowerLeg", thick=0.004)
    def Pkn(u, v):
        th = (u - 0.5) * 2 * 1.0; el = (v - 0.4) * 1.6
        r = rs + 0.05
        return KN + d2 * (0.02 - math.sin(el) * 0.07) + (o2 * math.cos(th) * math.cos(el * 0.6) + f2 * math.sin(th)) * r
    surf("脛当%s_膝" % side, 14, 6, Pkn, M['kuro'], pre + "LowerLeg", thick=0.006)
log("legs done")

# ================= 兜 =================
log("kabuto")
C = HEADC
def dome(a, ph, r):
    return C + Vector((math.sin(a) * math.sin(ph) * r, -math.cos(a) * math.sin(ph) * r * 1.08, math.cos(ph) * r * 0.9))
surf("兜_鉢", 48, 14, lambda u, v: dome(u * 2 * math.pi, (6 + v * 82) * D2R, HR), M['kuro'], "Head", thick=0.007, closed=True)
for k in range(28):
    a0 = k / 28 * 2 * math.pi
    surf("兜_筋%d" % k, 1, 14, lambda u, v, a0=a0: dome(a0 + (u - 0.5) * 0.025, (7 + v * 80) * D2R, HR + 0.004), M['kuro'], "Head", thick=0.004, sub=1)
surf("兜_天辺", 24, 1, lambda u, v: dome(u * 2 * math.pi, (5 + v * 4) * D2R, HR + 0.006), M['kin'], "Head", thick=0.006, closed=True)
# 眉庇
surf("兜_眉庇", 24, 3, lambda u, v: C + Vector((math.sin((u - 0.5) * 2.4) * (HR + 0.004 + v * 0.05), -math.cos((u - 0.5) * 2.4) * (HR + 0.004 + v * 0.05) * 1.08, (HR * 0.9) * math.cos(88 * D2R) - 0.004 - v * 0.022)), M['kuro'], "Head", thick=0.006)
surf("兜_眉庇金", 24, 1, lambda u, v: C + Vector((math.sin((u - 0.5) * 2.4) * (HR + 0.056), -math.cos((u - 0.5) * 2.4) * (HR + 0.056) * 1.08, -0.02 - v * 0.006)), M['kin'], "Head", thick=0.004)
# 錣：大きく裾を広げた4段
for i in range(4):
    zT = -0.005 - i * 0.045
    def Pw(u, v, i=i, zT=zT):
        a = (62 + u * 236) * D2R
        back = max(0.0, math.cos(a - math.pi)) ** 1.5      # 後ろほど大きく張り出す
        r = HR + 0.014 + (i * 0.016 + v * 0.02) * (0.6 + 0.8 * back)
        return C + Vector((math.sin(a) * r, -math.cos(a) * r * 1.05, zT - v * 0.055))
    surf("兜_錣%d" % i, 32, 3, Pw, M['odoshi'], "Head", thick=0.006, uv_arc=0.03)
# 吹返
for s in (-1, 1):
    def Pf(u, v, s=s):
        a = s * (50 + 22 * u) * D2R
        r = HR + 0.035 + v * 0.07 + u * 0.03
        return C + Vector((math.sin(a) * r, -math.cos(a) * r * 1.05 + 0.03 * v, -0.01 - v * 0.08))
    surf("兜_吹返" + str(s), 8, 4, Pf, M['aka'], "Head", thick=0.006)
# 前立：三日月
c0 = C + Vector((0, -(HR * 1.08) - 0.03, 0.05 + 0.16))
def Pmoon(u, v):
    a = (-168 + u * 156) * D2R
    w = 0.034 * math.sin(u * math.pi) ** 0.8 + 0.003
    r = 0.16 - v * w
    return c0 + Vector((math.cos(a) * r, 0.02 * (1 - math.cos((u - 0.5) * 2)), math.sin(a) * r))
surf("兜_前立", 40, 2, Pmoon, M['kin'], "Head", thick=0.006, crease=0.3)
log("kabuto done")

# ================= 面頬 =================
def Pmen(u, v):
    a = (u - 0.5) * 2 * 1.45
    z = -0.115 + v * 0.11
    r = HR - 0.012 + 0.012 * math.exp(-(a * a) * 8) * math.sin(v * math.pi)  # 鼻
    r += 0.02 * (1 - v) * math.cos(a) ** 2
    return C + Vector((math.sin(a) * r, -math.cos(a) * r * 1.05, z))
surf("面頬", 28, 8, Pmen, M['tetsu'], "Head", thick=0.006)
for i in range(3):
    def Py(u, v, i=i):
        a = (u - 0.5) * 2 * 1.5
        r = HR - 0.01 + i * 0.012 + v * 0.025
        return C + Vector((math.sin(a) * r, -math.cos(a) * r * 1.0, -0.12 - i * 0.03 - v * 0.035))
    surf("面頬_垂%d" % i, 20, 2, Py, M['odoshi_k'], "Head", thick=0.005, uv_arc=0.025)

# ================= 刀（大小）を左腰に =================
log("daisho")
try:
    rx, ry, cy = torso(0.0)
    hipL = Vector((SIDE * (rx + 0.06), cy - ry * 0.3, waistZ - 0.03))
    for name, length, tl, ang in (("刀", 0.74, 0.25, (0.25, 1.0, -0.42)), ("脇差", 0.5, 0.18, (0.32, 1.0, -0.2))):
        dd = Vector((SIDE * ang[0], ang[1], ang[2])).normalized()
        base = hipL + Vector((0, -0.02, 0.0 if name == "刀" else -0.04))
        sori = Vector((0, 0, 1)) - dd * dd.z; sori.normalize()
        Cs = lambda t, base=base, dd=dd, length=length, sori=sori: base + dd * (t * length) + sori * (0.03 * math.sin(t * math.pi) * length)
        tube(name + "_鞘", 16, 24, Cs, lambda t: (0.017 - 0.004 * t, 0.012 - 0.003 * t), M['kuro'], "Spine", sub=1)
        Ct = lambda t, base=base, dd=dd, tl=tl: base - dd * (0.035 + t * tl)
        tube(name + "_柄", 14, 10, Ct, lambda t: (0.016, 0.013), M['tsuka'], "Spine", sub=1, uvx=1.0)
        tube(name + "_鍔", 24, 1, lambda t, base=base, dd=dd: base - dd * (0.018 + t * 0.008), lambda t: (0.045, 0.04), M['tetsu'], "Spine", sub=1)
        tube(name + "_頭", 12, 2, lambda t, base=base, dd=dd, tl=tl: base - dd * (0.035 + tl + t * 0.015), lambda t: (0.017, 0.014), M['kin'], "Spine", sub=1)
        tube(name + "_鐺", 12, 2, lambda t, base=base, dd=dd, length=length, sori=sori: base + dd * (length + t * 0.02) + sori * 0.0, lambda t: (0.013, 0.009), M['kin'], "Spine", sub=1)
        # 下げ緒
        tube(name + "_下げ緒", 8, 20, lambda t, base=base, dd=dd: base + dd * (0.1 + 0.08 * t) + Vector((SIDE * 0.02, 0, -0.03 - 0.1 * math.sin(t * math.pi))), lambda t: (0.005, 0.005), M['beni'], "Spine", sub=1)
except Exception as e: log("ERR daisho", e, traceback.format_exc()[-300:])
log("daisho done")

# ================= 腕を下ろしたポーズ =================
pose_log = {}
for pre, SH in (("Left_", SHL), ("Right_", SHR)):
    try:
        ua = pre + "UpperArm"; hand = pre + "Hand"
        pb = arm.pose.bones[ua]; pb.rotation_mode = 'QUATERNION'
        v = bh(hand) - SH
        cur = math.degrees(math.atan2(-v.z, math.hypot(v.x, v.y)))   # 今の下がり角
        want = 72.0
        axis_arm = arm.matrix_world.inverted().to_3x3() @ Vector((0, 1, 0))
        axis_bone = (arm.data.bones[ua].matrix_local.to_3x3().inverted() @ axis_arm).normalized()
        best = None
        for sgn in (1, -1):
            pb.rotation_quaternion = Quaternion(axis_bone, sgn * math.radians(want - cur))
            bpy.context.view_layer.update()
            z = (arm.matrix_world @ arm.pose.bones[hand].head).z
            if best is None or z < best[1]: best = (sgn, z)
        pb.rotation_quaternion = Quaternion(axis_bone, best[0] * math.radians(want - cur))
        # 肘を少し曲げる
        la = pre + "LowerArm"; pl = arm.pose.bones[la]; pl.rotation_mode = 'QUATERNION'
        axis2 = (arm.data.bones[la].matrix_local.to_3x3().inverted() @ (arm.matrix_world.inverted().to_3x3() @ Vector((1, 0, 0)))).normalized()
        bpy.context.view_layer.update()
        y0 = (arm.matrix_world @ arm.pose.bones[hand].head).y
        best2 = None
        for sgn in (1, -1):
            pl.rotation_quaternion = Quaternion(axis2, sgn * math.radians(14))
            bpy.context.view_layer.update()
            y = (arm.matrix_world @ arm.pose.bones[hand].head).y
            if best2 is None or y < best2[1]: best2 = (sgn, y)
        pl.rotation_quaternion = Quaternion(axis2, best2[0] * math.radians(14))
        bpy.context.view_layer.update()
        pose_log[pre] = round(cur, 1)
    except Exception as e: log("ERR pose", pre, e)
log("pose", pose_log)

# ================= 舞台：光・床・カメラ（Cycles） =================
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
dev = "CPU"
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences
    for typ in ('OPTIX', 'CUDA', 'HIP', 'ONEAPI'):
        try:
            prefs.compute_device_type = typ
            prefs.get_devices()
            gpus = [d for d in prefs.devices if d.type == typ]
            if gpus:
                for d_ in prefs.devices: d_.use = (d_.type == typ)
                sc.cycles.device = 'GPU'; dev = typ; break
        except Exception: pass
except Exception as e: log("gpu", e)
sc.cycles.samples = 160 if dev != "CPU" else 64
sc.cycles.use_denoising = True
try: sc.cycles.denoiser = 'OPENIMAGEDENOISE'
except Exception: pass
sc.render.resolution_x, sc.render.resolution_y = 1000, 1400
sc.render.resolution_percentage = 100 if dev != "CPU" else 70
try:
    sc.view_settings.view_transform = 'AgX'; sc.view_settings.look = 'AgX - Punchy'
except Exception: pass
log("device", dev)

world = bpy.data.worlds.new("夕闇"); sc.world = world; world.use_nodes = True
bg = world.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.03, 0.032, 0.04, 1); bg.inputs[1].default_value = 1.0

mins = Vector((1e9, 1e9, 1e9)); maxs = Vector((-1e9, -1e9, -1e9))
for o in COL.objects:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c); mins = Vector(map(min, mins, w)); maxs = Vector(map(max, maxs, w))
H = maxs.z - min(mins.z, ANL.z - 0.1); ctr = (mins + maxs) / 2
groundZ = min(p.z for p in BV)

bpy.ops.mesh.primitive_circle_add(vertices=64, radius=6, fill_type='NGON', location=(ctr.x, ctr.y, groundZ))
floor = bpy.context.active_object; floor.name = "床"
fm, fnt, fb = new_mat("床"); fb.inputs['Base Color'].default_value = (0.03, 0.028, 0.026, 1); fb.inputs['Roughness'].default_value = 0.55
floor.data.materials.append(fm)

def light(name, loc, energy, color, size, kind='AREA'):
    d = bpy.data.lights.new(name, kind); d.energy = energy; d.color = color
    if kind == 'AREA': d.size = size
    o = bpy.data.objects.new(name, d); sc.collection.objects.link(o); o.location = loc
    o.rotation_euler = (Vector((ctr.x, ctr.y, ctr.z + 0.2)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    return o
light("キー", (ctr.x + 2.2, ctr.y - 2.6, ctr.z + 2.2), 420, (1.0, 0.9, 0.78), 1.4)
light("リム左", (ctr.x - 2.4, ctr.y + 2.4, ctr.z + 1.4), 900, (0.62, 0.75, 1.0), 0.8)
light("リム右", (ctr.x + 2.2, ctr.y + 2.6, ctr.z + 0.9), 650, (1.0, 0.55, 0.32), 0.8)
light("フィル", (ctr.x - 2.6, ctr.y - 2.0, ctr.z - 0.2), 70, (0.8, 0.85, 1.0), 3.0)
light("天", (ctr.x, ctr.y + 0.3, ctr.z + 3.0), 180, (1.0, 0.95, 0.9), 1.0)

cam_d = bpy.data.cameras.new("カメラ"); cam_d.lens = 75; cam_d.dof.use_dof = False
cam = bpy.data.objects.new("カメラ", cam_d); sc.collection.objects.link(cam); sc.camera = cam
pivot = bpy.data.objects.new("カメラ軸", None); sc.collection.objects.link(pivot)
pivot.location = (ctr.x, ctr.y, groundZ)
cam.parent = pivot
log("scene ok", "H", round(H, 3), "tris(base)", TRIS[0])

shots = [("hero", -30, 0.2, 0.55, 0.95), ("front", 0, 0.5, 0.5, 1.05), ("side", 90, 0.5, 0.5, 1.05), ("back", 180, 0.5, 0.5, 1.05)]
for name, ang, camh, aimh, distk in shots:
    pivot.rotation_euler = (0, 0, math.radians(ang))
    dist = H * 2.75 * distk
    cam.location = (0, -dist, H * camh)
    look = Vector((0, 0, H * aimh)) - Vector(cam.location)
    cam.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(OUT, "v2_" + name + ".png")
    log("render", name)
    try: bpy.ops.render.render(write_still=True)
    except Exception as e: log("ERR render", e)
pivot.rotation_euler = (0, 0, 0)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "Samurai_v2.blend"))
log("done")
