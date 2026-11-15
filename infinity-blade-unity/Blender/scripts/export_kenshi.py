# -*- coding: utf-8 -*-
# 剣士（Kenshi_Furi.blend）を Unity 用の FBX に書き出す
#  ・ポーズを元に戻して（骨の初期姿勢）、輪郭線（裏返しの殻）も形に焼き込む
#  ・床・背景・光・カメラは書き出さない
import bpy, os
from mathutils import Matrix
ROOT = os.path.join(os.path.expanduser("~"), "インフィニティブレード", "Blender")
SRC = os.path.join(ROOT, "Kenshi_Furi.blend")
OUT = os.path.join(ROOT, "Export", "Kenshi.fbx")
bpy.ops.wm.open_mainfile(filepath=SRC)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
for pb in arm.pose.bones: pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()

keep = {arm}
def add_children(o):
    for c in o.children:
        if c.type == 'MESH': keep.add(c); add_children(c)
add_children(arm)
for o in bpy.data.objects:
    o.select_set(o in keep)
bpy.context.view_layer.objects.active = arm
print("export objects", len(keep))
bpy.ops.export_scene.fbx(
    filepath=OUT, use_selection=True, object_types={'ARMATURE', 'MESH'},
    use_mesh_modifiers=True, use_mesh_modifiers_render=False, mesh_smooth_type='FACE',
    add_leaf_bones=False, use_armature_deform_only=False, bake_anim=False,
    apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    axis_forward='-Z', axis_up='Y', primary_bone_axis='Y', secondary_bone_axis='X',
    armature_nodetype='NULL', path_mode='AUTO')
print("saved", OUT, os.path.getsize(OUT))
