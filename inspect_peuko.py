import bpy, math, os
from mathutils import Vector
p = r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko'
bpy.ops.wm.obj_import(filepath=os.path.join(p,'base.obj'))
objs = [o for o in bpy.context.scene.objects if o.type=='MESH']
print('MESHES', [(o.name, len(o.data.vertices), len(o.data.polygons), len(o.data.uv_layers)) for o in objs])
for o in objs:
    coords = [o.matrix_world @ v.co for v in o.data.vertices]
    mn = [min(v[i] for v in coords) for i in range(3)]
    mx = [max(v[i] for v in coords) for i in range(3)]
    print('BOUNDS', o.name, mn, mx, 'DIM', [mx[i]-mn[i] for i in range(3)])
    print('UV', o.data.uv_layers.active.name if o.data.uv_layers.active else None)
    print('MATS', [m.name if m else None for m in o.data.materials])
img=bpy.data.images.load(os.path.join(p,'texture_diffuse.png'), check_existing=True)
print('IMAGE', img.size[:], img.channels, img.alpha_mode)
