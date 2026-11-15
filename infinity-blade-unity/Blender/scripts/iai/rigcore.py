# -*- coding: utf-8 -*-
# 「姿勢人形」づくりの共通道具
#  ・キャラは硬い部品の集まり。骨アニメは使わず、決めた姿勢（ポーズ）を切り替える
#  ・姿勢は 手先・足先・刀 の位置から自動で関節を解く（IK）
#  ・Unity 用に、部品の形（メッシュ）と姿勢ごとの関節の向きを JSON に書き出す
import bpy, bmesh, math, json, os
from mathutils import Vector, Matrix, Quaternion, Euler

D2R = math.pi / 180
V = Vector


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


# ---------------------------------------------------------------- 数学
def rot_ypr(pitch=0, yaw=0, roll=0):
    """キャラは -Y 向き。+pitch=前に倒す、+yaw=左を向く、+roll=左へ傾く"""
    return (Matrix.Rotation(yaw * D2R, 3, 'Z') @ Matrix.Rotation(pitch * D2R, 3, 'X') @ Matrix.Rotation(roll * D2R, 3, 'Y'))


def frame(ydir, zhint):
    y = V(ydir).normalized()
    zh = V(zhint)
    zh = zh - y * zh.dot(y)
    if zh.length < 1e-5:
        zh = V((0, 1, 0)) - y * y.y
        if zh.length < 1e-5: zh = V((1, 0, 0))
    z = zh.normalized()
    x = y.cross(z).normalized()
    m = Matrix((x, y, z)).transposed()
    return m


def M4(rot3, pos):
    m = rot3.to_4x4()
    m.translation = V(pos)
    return m


def ik2(a, c, l1, l2, pole):
    a = V(a); c = V(c)
    d = c - a
    dist = d.length
    lo, hi = abs(l1 - l2) + 1e-3, l1 + l2 - 1e-3
    dd = max(lo, min(hi, dist))
    dirv = d.normalized() if dist > 1e-6 else V((0, 0, -1))
    cosA = (l1 * l1 + dd * dd - l2 * l2) / (2 * l1 * dd)
    cosA = max(-1, min(1, cosA))
    sinA = math.sqrt(max(0, 1 - cosA * cosA))
    p = V(pole)
    p = p - dirv * p.dot(dirv)
    if p.length < 1e-5: p = V((0, 1, 0)) - dirv * dirv.y
    p.normalize()
    mid = a + dirv * (l1 * cosA) + p * (l1 * sinA)
    end = a + dirv * dd
    return mid, end


# ---------------------------------------------------------------- マテリアル
MATS = {}

def mat(name, color, kind="toon", shade=None, rim=0.3, spec=0.0, intensity=1.8):
    """kind: toon / neon / line"""
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    em = nt.nodes.new('ShaderNodeEmission')
    em.inputs['Color'].default_value = (*color, 1)
    em.inputs['Strength'].default_value = intensity if kind == "neon" else 1.0
    nt.links.new(em.outputs[0], out.inputs['Surface'])
    if kind == "line": m.use_backface_culling = True
    if shade is None: shade = (color[0] * 0.38, color[1] * 0.36, color[2] * 0.5 + 0.01)
    MATS[name] = dict(kind=kind, lit=list(color), shade=list(shade), rim=rim, spec=spec, intensity=intensity)
    return m


# ---------------------------------------------------------------- 形づくり
def new_obj(name, bm, mats, smooth_angle=38):
    me = bpy.data.meshes.new(name)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    if not isinstance(mats, (list, tuple)): mats = [mats]
    for m in mats: me.materials.append(m)
    for p in me.polygons: p.use_smooth = True
    # 角度のきつい辺は折り目にする
    bm2 = bmesh.new(); bm2.from_mesh(me)
    for e in bm2.edges:
        if len(e.link_faces) == 2:
            e.smooth = e.calc_face_angle(0) < smooth_angle * D2R
    bm2.to_mesh(me); bm2.free()
    return o


def ring(center, bx, by, rx, ry, n=10, power=2.0, phase=0.0):
    """超楕円の輪。power=2 で楕円、大きいほど四角い"""
    pts = []
    for i in range(n):
        a = phase + 2 * math.pi * i / n
        c, s = math.cos(a), math.sin(a)
        ex = 2.0 / power
        px = (abs(c) ** ex) * (1 if c >= 0 else -1) * rx
        py = (abs(s) ** ex) * (1 if s >= 0 else -1) * ry
        pts.append(V(center) + V(bx) * px + V(by) * py)
    return pts


def loft(name, rings, mats, cap0=True, cap1=True, sub=0, smooth_angle=38, mat_fn=None):
    bm = bmesh.new()
    n = len(rings[0])
    vs = [[bm.verts.new(p) for p in r] for r in rings]
    for i in range(len(rings) - 1):
        for j in range(n):
            f = bm.faces.new((vs[i][j], vs[i][(j + 1) % n], vs[i + 1][(j + 1) % n], vs[i + 1][j]))
            if mat_fn: f.material_index = mat_fn(i, j)
    if cap0: bm.faces.new(list(reversed(vs[0])))
    if cap1: bm.faces.new(vs[-1])
    if sub: bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=sub, use_grid_fill=True, smooth=1.0)
    return new_obj(name, bm, mats, smooth_angle)


def tube(name, pts, radii, mats, n=10, power=2.0, zhint=(0, 1, 0), flat=1.0, cap0=True, cap1=True, smooth_angle=38, phase=0.0, mat_fn=None):
    """点列に沿った管。radii は各点の半径（数 or (rx, ry)）。flat は奥行きの倍率"""
    pts = [V(p) for p in pts]
    rings = []
    for i, p in enumerate(pts):
        if i == 0: d = pts[1] - pts[0]
        elif i == len(pts) - 1: d = pts[-1] - pts[-2]
        else: d = pts[i + 1] - pts[i - 1]
        f = frame(d, zhint)
        bx = f.col[0]; by = f.col[2]
        r = radii[i]
        rx, ry = (r if isinstance(r, (tuple, list)) else (r, r * flat))
        rings.append(ring(p, bx, by, rx, ry, n, power, phase))
    return loft(name, rings, mats, cap0, cap1, smooth_angle=smooth_angle, mat_fn=mat_fn)


def lathe(name, profile, mats, n=24, center=(0, 0, 0), axis_rot=None, cap0=True, cap1=True, smooth_angle=38, sx=1.0, sy=1.0, mat_fn=None):
    """profile: [(radius, z)] を Z 軸まわりに回す"""
    rings = []
    for r, z in profile:
        pts = []
        for i in range(n):
            a = 2 * math.pi * i / n
            p = V((math.cos(a) * r * sx, math.sin(a) * r * sy, z))
            if axis_rot is not None: p = axis_rot @ p
            pts.append(V(center) + p)
        rings.append(pts)
    return loft(name, rings, mats, cap0, cap1, smooth_angle=smooth_angle, mat_fn=mat_fn)


def sheet(name, grid, mats, thick=0.012, smooth_angle=60, mat_fn=None):
    """grid[u][v] の点でできた布・板。厚みを付ける"""
    bm = bmesh.new()
    nu, nv = len(grid), len(grid[0])
    vs = [[bm.verts.new(V(p)) for p in row] for row in grid]
    for u in range(nu - 1):
        for v in range(nv - 1):
            f = bm.faces.new((vs[u][v], vs[u + 1][v], vs[u + 1][v + 1], vs[u][v + 1]))
            if mat_fn: f.material_index = mat_fn(u, v)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if thick > 0:
        geom = bm.faces[:]
        bmesh.ops.solidify(bm, geom=geom, thickness=thick)
    return new_obj(name, bm, mats, smooth_angle)


def box(name, center, size, mats, rot=None, bevel=0.0, taper=(1, 1), smooth_angle=30):
    """taper: 上面(+Z側)の X・Y 倍率"""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        k = (v.co.z + 0.5)
        v.co.x *= size[0] * (1 + (taper[0] - 1) * k)
        v.co.y *= size[1] * (1 + (taper[1] - 1) * k)
        v.co.z *= size[2]
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=bm.edges[:], offset=bevel, segments=1, affect='EDGES')
    for v in bm.verts:
        if rot is not None: v.co = rot @ v.co
        v.co += V(center)
    return new_obj(name, bm, mats, smooth_angle)


def join(name, objs):
    """部品をひとつにまとめる（マテリアルは名前で寄せる）"""
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name; o.data.name = name
    return o


def mirror_x(o, name):
    """左右反対の写しを作る"""
    me = o.data.copy()
    for v in me.vertices: v.co.x = -v.co.x
    me.flip_normals()
    c = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(c)
    return c


# ---------------------------------------------------------------- 人形（リグ）
class Rig:
    def __init__(self, name, line_mat, line_w=0.006):
        self.name = name
        self.joints = {}      # name -> dict(parent, rest(4x4))
        self.order = []
        self.parts = []       # (obj, joint)
        self.line_mat = line_mat
        self.line_w = line_w
        self.poses = {}       # name -> {joint: 4x4 world}
        self.pose_order = []

    def joint(self, name, parent, pos, rot=None):
        self.joints[name] = dict(parent=parent, rest=M4(rot if rot is not None else Matrix.Identity(3), pos))
        self.order.append(name)

    def attach(self, obj, joint, line=True, line_w=None):
        if line:
            md = obj.modifiers.new("line", 'SOLIDIFY')
            md.thickness = line_w or self.line_w
            md.offset = 1.0
            md.use_flip_normals = True
            md.use_rim = False
            md.use_quality_normals = True
            if self.line_mat.name not in [m.name for m in obj.data.materials]:
                obj.data.materials.append(self.line_mat)
            li = [m.name for m in obj.data.materials].index(self.line_mat.name)
            md.material_offset = li
            md.material_offset_rim = li
        self.parts.append((obj, joint))
        return obj

    def rest_pose(self):
        return {n: j["rest"].copy() for n, j in self.joints.items()}

    def follow(self, P, name, local=None):
        """親に付いていく関節。local は親基準の追加回転(3x3)"""
        j = self.joints[name]
        par = j["parent"]
        rel = self.joints[par]["rest"].inverted() @ j["rest"]
        m = P[par] @ rel
        if local is not None:
            m = m @ local.to_4x4()
        P[name] = m

    def apply(self, P):
        for o, jn in self.parts:
            o.matrix_world = P[jn] @ self.joints[jn]["rest"].inverted()

    def add_pose(self, name, P):
        self.poses[name] = {k: v.copy() for k, v in P.items()}
        if name not in self.pose_order: self.pose_order.append(name)

    # ---------- 書き出し（Unity の座標へ：x→-x, z→y, -y→z） ----------
    def export(self, path, extra=None):
        C = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
        C4 = C.to_4x4()

        def conv(m):
            return C4 @ m @ C4.inverted()

        dg = bpy.context.evaluated_depsgraph_get()
        joints = []
        idx = {n: i for i, n in enumerate(self.order)}
        for n in self.order:
            j = self.joints[n]
            joints.append(dict(name=n, parent=idx[j["parent"]] if j["parent"] else -1))

        # 部品の形は「関節の基準姿勢」から見た座標で持つ
        parts = []
        used = set()
        for o, jn in self.parts:
            o.matrix_world = Matrix.Identity(4)
        bpy.context.view_layer.update()
        dg = bpy.context.evaluated_depsgraph_get()
        for o, jn in self.parts:
            ev = o.evaluated_get(dg)
            me = ev.to_mesh()
            me.calc_loop_triangles()
            inv = conv(self.joints[jn]["rest"]).inverted()
            names = [m.name for m in me.materials]
            subs = {}
            cn = me.corner_normals
            for t in me.loop_triangles:
                mn = names[t.material_index] if names else "?"
                s = subs.setdefault(mn, dict(v=[], n=[], key={}, tri=[]))
                tri = []
                for li in t.loops:
                    vi = me.loops[li].vertex_index
                    nrm = cn[li].vector
                    key = (vi, round(nrm.x, 3), round(nrm.y, 3), round(nrm.z, 3))
                    k = s["key"].get(key)
                    if k is None:
                        k = len(s["v"]) // 3
                        s["key"][key] = k
                        p = inv @ (C @ me.vertices[vi].co)
                        nn = (inv.to_3x3() @ (C @ nrm)).normalized()
                        s["v"] += [round(p.x, 5), round(p.y, 5), round(p.z, 5)]
                        s["n"] += [round(nn.x, 4), round(nn.y, 4), round(nn.z, 4)]
                    tri.append(k)
                s["tri"] += [tri[0], tri[2], tri[1]]   # 鏡映したので巻きを逆に
            for mn, s in subs.items():
                used.add(mn)
                parts.append(dict(name=o.name + ("" if len(subs) == 1 else "_" + mn), obj=o.name, joint=idx[jn], mat=mn, v=s["v"], n=s["n"], tri=s["tri"]))
            ev.to_mesh_clear()

        def trs(m):
            m = conv(m)
            q = m.to_quaternion(); p = m.translation
            return [round(p.x, 5), round(p.y, 5), round(p.z, 5), round(q.x, 6), round(q.y, 6), round(q.z, 6), round(q.w, 6)]

        def local_pose(P):
            out = []
            for n in self.order:
                par = self.joints[n]["parent"]
                m = P[n] if not par else P[par].inverted() @ P[n]
                out += trs(m)
            return out

        data = dict(
            name=self.name,
            joints=joints,
            rest=local_pose(self.rest_pose()),
            poses=[dict(name=n, t=local_pose(self.poses[n])) for n in self.pose_order],
            materials=[dict(name=k, **v) for k, v in MATS.items() if k in used],
            parts=parts,
        )
        if extra: data.update(extra)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, separators=(",", ":"))
        nv = sum(len(p["v"]) // 3 for p in parts); nt = sum(len(p["tri"]) // 3 for p in parts)
        print("export", path, "parts", len(parts), "verts", nv, "tris", nt, "poses", len(self.pose_order), os.path.getsize(path) // 1024, "KB")


# ---------------------------------------------------------------- 絵にして確認
def setup_render(w=700, h=900, bg=(0.33, 0.13, 0.42)):
    s = bpy.context.scene
    s.render.engine = 'BLENDER_WORKBENCH'
    s.display.shading.light = 'FLAT'
    s.display.shading.color_type = 'MATERIAL'
    s.display.shading.show_backface_culling = True
    s.display.shading.show_object_outline = False
    s.display.render_aa = '8'
    s.render.resolution_x = w; s.render.resolution_y = h
    s.render.film_transparent = False
    s.view_settings.view_transform = 'Standard'
    if s.world is None: s.world = bpy.data.worlds.new("w")
    s.world.color = bg
    s.display.shading.background_type = 'WORLD'
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    s.collection.objects.link(cam)
    s.camera = cam
    return cam


def shoot(cam, path, eye, target, lens=50, w=None, h=None):
    s = bpy.context.scene
    if w: s.render.resolution_x = w
    if h: s.render.resolution_y = h
    cam.data.lens = lens
    cam.location = V(eye)
    d = V(target) - V(eye)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    s.render.filepath = path
    bpy.ops.render.render(write_still=True)
