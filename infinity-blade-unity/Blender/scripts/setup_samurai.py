# -*- coding: utf-8 -*-
# 武士キャラを Blender に取り込んで、編集しやすい作業用ファイルにする
#  1. Unity から書き出した Blender/Export/SamuraiCharacter.fbx を読み込む
#  2. 鎧の板を「重複頂点の結合 → なめらか表示 → 細分化（表示のみ）」で角を落とす
#  3. マテリアルを Blender 用に作り直す（威糸の模様テクスチャ付き）
#  4. 腕を下ろした A ポーズ、ライトとカメラを置いて、前・横・後ろを書き出す
#  5. Blender/Samurai.blend に保存する
# 実行：Blender の Python コンソールで
#   exec(open(r"<このファイルのパス>", encoding="utf-8").read())
import bpy, bmesh, os, math, json
from mathutils import Vector, Quaternion

ROOT = os.path.join(os.path.expanduser("~"), "インフィニティブレード", "Blender")
FBX = os.path.join(ROOT, "Export", "SamuraiCharacter.fbx")
TEX = os.path.join(ROOT, "Export", "Textures")
OUT = os.path.join(ROOT, "Renders")
os.makedirs(OUT, exist_ok=True)
info = {}
LOG = os.path.join(OUT, "log.txt")
open(LOG, "w", encoding="utf-8").close()
def log(*a):
    with open(LOG, "a", encoding="utf-8") as f:
        f.write(" ".join(str(x) for x in a) + "\n"); f.flush()
log("start")

log("step1 import")
# ---------- 1. 読み込み ----------
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.lights, bpy.data.cameras, bpy.data.images):
    for d in list(coll):
        if d.users == 0: coll.remove(d)
bpy.ops.import_scene.fbx(filepath=FBX, use_anim=False, ignore_leaf_bones=True, automatic_bone_orientation=True)
objs = list(bpy.context.scene.objects)
arm = next((o for o in objs if o.type == 'ARMATURE'), None)
meshes = [o for o in objs if o.type == 'MESH']
info["objects"] = len(objs)
info["meshes"] = len(meshes)
info["armature"] = arm.name if arm else None
info["bones"] = [b.name for b in arm.data.bones] if arm else []
log("imported", len(objs), "objects")
info["parenting"] = {o.name: [o.parent.name if o.parent else None, o.parent_type, o.parent_bone] for o in meshes[:12]}

log("step2 smooth")
# ---------- 2. 鎧をなめらかに ----------
body = None
for o in meshes:
    if any(m.type == 'ARMATURE' for m in o.modifiers) or o.vertex_groups:
        body = o  # 素体（骨で変形するメッシュ）
        continue
    me = o.data
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0002)
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = True
    try:
        bpy.context.view_layer.objects.active = o
        o.select_set(True)
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
        o.select_set(False)
    except Exception:
        pass
    sub = o.modifiers.new("なめらか", 'SUBSURF')
    sub.levels = 1; sub.render_levels = 1
    try: sub.boundary_smooth = 'PRESERVE_CORNERS'
    except Exception: pass
info["body"] = body.name if body else None

log("step3 materials")
# ---------- 3. マテリアル ----------
COL = {  # Unity の色と同じ（くすんだ実戦用の配色）
    "S_Kuro":  ((0.05, 0.042, 0.038), 0.1, 0.38),
    "S_Aka":   ((0.42, 0.035, 0.045), 0.1, 0.2),
    "S_Kin":   ((0.6, 0.47, 0.26), 0.9, 0.5),
    "S_Ito":   ((0.38, 0.05, 0.05), 0.0, 0.7),
    "S_Tetsu": ((0.17, 0.17, 0.19), 0.85, 0.45),
    "S_Nuno":  ((0.075, 0.075, 0.085), 0.0, 0.8),
    "S_Tsuka": ((0.12, 0.1, 0.09), 0.0, 0.65),
}
def principled(mat):
    # 読み込んだマテリアルの中身は捨てて、きれいな Principled BSDF だけにする
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes): nt.nodes.remove(n)
    out = nt.nodes.new('ShaderNodeOutputMaterial'); out.location = (300, 0)
    bsdf = nt.nodes.new('ShaderNodeBsdfPrincipled')
    nt.links.new(bsdf.outputs['BSDF'], out.inputs['Surface'])
    return bsdf
done = set()
for o in meshes:
    for slot in o.material_slots:
        m = slot.material
        if m is None or m.name in done: continue
        done.add(m.name)
        key = next((k for k in list(COL) + ["S_Odoshi"] if m.name.startswith(k)), None)
        bsdf = principled(m)
        if key == "S_Odoshi":
            img_path = os.path.join(TEX, "T_Odoshi.png")
            if os.path.exists(img_path):
                img = bpy.data.images.load(img_path, check_existing=True)
                tn = m.node_tree.nodes.new('ShaderNodeTexImage'); tn.image = img
                m.node_tree.links.new(tn.outputs['Color'], bsdf.inputs['Base Color'])
            bsdf.inputs['Metallic'].default_value = 0.05
            bsdf.inputs['Roughness'].default_value = 0.5
        elif key:
            c, met, rough = COL[key]
            bsdf.inputs['Base Color'].default_value = (*c, 1)
            bsdf.inputs['Metallic'].default_value = met
            bsdf.inputs['Roughness'].default_value = rough
info["materials"] = sorted(done)

log("step4 pose")
# ---------- 4. A ポーズ ----------
def find_bone(*keys):
    for b in arm.data.bones:
        n = b.name.lower().replace("_", "").replace(" ", "")
        if all(k in n for k in keys): return b.name
    return None
pose_log = {}
DO_APOSE = False   # 書き出された姿勢はもう腕が下がっているので回さない
if arm and DO_APOSE:
    bpy.context.view_layer.update()
    for side in ("left", "right"):
        ua = find_bone(side, "upperarm") or find_bone(side[0] + "upperarm") or find_bone(side, "arm")
        hand = find_bone(side, "hand")
        if not ua or not hand: pose_log[side] = "bone not found"; continue
        pb = arm.pose.bones[ua]; pb.rotation_mode = 'QUATERNION'
        axis_world = Vector((0, 1, 0))  # 前後の軸まわりに回して腕を下ろす
        axis_arm = arm.matrix_world.inverted().to_3x3() @ axis_world
        axis_bone = (arm.data.bones[ua].matrix_local.to_3x3().inverted() @ axis_arm).normalized()
        z0 = (arm.matrix_world @ arm.pose.bones[hand].head).z
        best = None
        for sgn in (1, -1):
            pb.rotation_quaternion = Quaternion(axis_bone, sgn * math.radians(55))
            bpy.context.view_layer.update()
            z = (arm.matrix_world @ arm.pose.bones[hand].head).z
            if best is None or z < best[1]: best = (sgn, z)
        pb.rotation_quaternion = Quaternion(axis_bone, best[0] * math.radians(55))
        bpy.context.view_layer.update()
        pose_log[side] = [ua, round(z0, 3), round(best[1], 3)]
info["pose"] = pose_log

log("step5 scene")
# ---------- 5. ライト・カメラ・床 ----------
sc = bpy.context.scene
try: sc.render.engine = 'BLENDER_EEVEE_NEXT'
except Exception: sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 900, 1200
sc.render.film_transparent = False
world = bpy.data.worlds.new("World"); sc.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.32, 0.31, 0.3, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.35
try:
    sc.view_settings.view_transform = 'AgX'
    sc.view_settings.look = 'AgX - Medium High Contrast'
except Exception: pass

# 全体の大きさ
mins = Vector((1e9, 1e9, 1e9)); maxs = Vector((-1e9, -1e9, -1e9))
for o in meshes:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        mins = Vector(map(min, mins, w)); maxs = Vector(map(max, maxs, w))
H = maxs.z - mins.z; ctr = (mins + maxs) / 2
info["height"] = round(H, 3); info["center"] = [round(v, 3) for v in ctr]

bpy.ops.mesh.primitive_plane_add(size=12, location=(ctr.x, ctr.y, mins.z))
floor = bpy.context.active_object; floor.name = "床"
fm = bpy.data.materials.new("床"); b = principled(fm); b.inputs['Base Color'].default_value = (0.25, 0.245, 0.235, 1); b.inputs['Roughness'].default_value = 0.8
floor.data.materials.append(fm)

def area(name, loc, energy, color, size):
    d = bpy.data.lights.new(name, 'AREA'); d.energy = energy; d.color = color; d.size = size
    o = bpy.data.objects.new(name, d); sc.collection.objects.link(o); o.location = loc
    o.rotation_euler = (Vector(ctr) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    return o
area("キー", (ctr.x + 2.5, ctr.y - 3.0, ctr.z + 2.5), 260, (1.0, 0.95, 0.88), 2.0)
area("リム", (ctr.x - 2.5, ctr.y + 3.0, ctr.z + 2.0), 320, (0.7, 0.82, 1.0), 1.5)
area("フィル", (ctr.x - 3.0, ctr.y - 2.0, ctr.z), 50, (1.0, 0.9, 0.85), 3.0)

cam_d = bpy.data.cameras.new("カメラ"); cam_d.lens = 70
cam = bpy.data.objects.new("カメラ", cam_d); sc.collection.objects.link(cam); sc.camera = cam
pivot = bpy.data.objects.new("カメラ軸", None); sc.collection.objects.link(pivot); pivot.location = (ctr.x, ctr.y, ctr.z)
cam.parent = pivot
dist = H * 2.2
cam.location = (0, -dist, 0.05 * H)
cam.rotation_euler = (math.radians(88), 0, 0)

log("step6 render")
# ---------- 6. 書き出し ----------
renders = []
for name, ang in (("0", 0), ("90", 90), ("180", 180), ("270", 270)):
    pivot.rotation_euler = (0, 0, math.radians(ang))
    sc.render.filepath = os.path.join(OUT, "samurai_" + name + ".png")
    log("render", name)
    bpy.ops.render.render(write_still=True)
    renders.append(sc.render.filepath)
pivot.rotation_euler = (0, 0, 0)
info["renders"] = renders

log("save")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "Samurai.blend"))
with open(os.path.join(OUT, "info.json"), "w", encoding="utf-8") as f:
    json.dump(info, f, ensure_ascii=False, indent=1)
log("done", json.dumps(info, ensure_ascii=False)[:2000])
print("[IBProto] 武士を Blender に取り込みました", info.get("height"))
