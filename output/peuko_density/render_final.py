import bpy, os, math
from mathutils import Vector, Quaternion
OUT = r'C:\Protergia\output\peuko_density'
blend = os.path.join(OUT, 'peuko_planes_dense_curved.blend')
bpy.ops.wm.open_mainfile(filepath=blend)
scene=bpy.context.scene
scene.cycles.samples=32
bpy.ops.wm.save_as_mainfile(filepath=blend)
cam=scene.camera
original_matrix=cam.matrix_world.copy()
original_scale=cam.data.ortho_scale
target=Vector((0,0,1))
cam.location=target + Quaternion(Vector((0,0,1)),math.radians(145)) @ (cam.location-target)
cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
bpy.context.view_layer.update()
inv=cam.matrix_world.inverted()
subjects=[o for o in scene.objects if o.type=='MESH' and o.name!='Preview ground']
pts=[inv@(o.matrix_world@v.co) for o in subjects for v in o.data.vertices]
x0,x1=min(p.x for p in pts),max(p.x for p in pts)
y0,y1=min(p.y for p in pts),max(p.y for p in pts)
cam.data.ortho_scale=max(y1-y0,(x1-x0)/(1100/1400))*1.10
cam.location+=cam.rotation_euler.to_quaternion()@Vector(((x0+x1)/2,(y0+y1)/2,0))
scene.render.resolution_percentage=50
scene.cycles.samples=16
scene.render.filepath=os.path.join(OUT,'peuko_planes_dense_back.png')
bpy.ops.render.render(write_still=True)
print('BACK_RENDER_READY',flush=True)
cam.matrix_world=original_matrix
cam.data.ortho_scale=original_scale
scene.render.resolution_percentage=100
scene.cycles.samples=32
scene.render.filepath=os.path.join(OUT,'peuko_planes_dense_preview.png')
bpy.ops.render.render(write_still=True)
print('FINAL_RENDER_READY',flush=True)
