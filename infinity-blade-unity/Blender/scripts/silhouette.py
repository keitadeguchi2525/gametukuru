# -*- coding: utf-8 -*-
# 今開いている武士を、白地に黒の影絵（正面・横）で書き出す。体格の比較用
import bpy, os, math
from mathutils import Vector
ROOT = os.path.join(os.path.expanduser("~"), "インフィニティブレード", "Blender")
OUT = os.path.join(ROOT, "Renders")
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'FLAT'; sh.color_type = 'SINGLE'; sh.single_color = (0, 0, 0)
sc.render.film_transparent = False
try:
    sh.background_type = 'VIEWPORT'; sh.background_color = (1, 1, 1)
except Exception: pass
sc.view_settings.view_transform = 'Standard'
try: sc.view_settings.look = 'None'
except Exception: pass
floor = bpy.data.objects.get("床")
if floor: floor.hide_render = True
cam = sc.camera
cam.data.type = 'ORTHO'
pivot = cam.parent
# 全体の大きさ
mins = Vector((1e9, 1e9, 1e9)); maxs = Vector((-1e9, -1e9, -1e9))
for o in sc.objects:
    if o.type != 'MESH' or o.hide_render: continue
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c); mins = Vector(map(min, mins, w)); maxs = Vector(map(max, maxs, w))
H = maxs.z - mins.z
cam.data.ortho_scale = H * 1.15
sc.render.resolution_x, sc.render.resolution_y = 800, 1000
sc.render.resolution_percentage = 100
for name, ang in (("front", 0), ("side", 90)):
    pivot.rotation_euler = (0, 0, math.radians(ang))
    cam.location = (0, -6, (mins.z + maxs.z) / 2 - pivot.location.z)
    cam.rotation_euler = (math.radians(90), 0, 0)
    sc.render.filepath = os.path.join(OUT, "sil_" + name + ".png")
    bpy.ops.render.render(write_still=True)
with open(os.path.join(OUT, "sil_info.txt"), "w") as f:
    f.write("%f %f %f\n" % (mins.z, maxs.z, H))
print("silhouette done")
