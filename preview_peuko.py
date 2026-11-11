import bpy, os
from mathutils import Vector
p = r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko'
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.wm.obj_import(filepath=os.path.join(p,'base.obj'))
model = next(o for o in bpy.context.scene.objects if o.type=='MESH')
mat = bpy.data.materials.new('Peuko bark and foliage | source diffuse')
mat.diffuse_color=(0.22,0.32,0.12,1)
mat.use_nodes=True
nodes=mat.node_tree.nodes; links=mat.node_tree.links
bsdf=nodes.get('Principled BSDF')
tex=nodes.new('ShaderNodeTexImage'); tex.image=bpy.data.images.load(os.path.join(p,'texture_diffuse.png'),check_existing=True)
links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
model.data.materials.append(mat)
# Aim camera at the tree as a whole
bpy.ops.object.camera_add(location=(3.8,-5.3,2.4))
cam=bpy.context.object
target=Vector((0,0,0.9)); cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.type='ORTHO'; cam.data.ortho_scale=3.25
bpy.context.scene.camera=cam
bpy.ops.object.light_add(type='AREA', location=(3,-4,5)); bpy.context.object.data.energy=500; bpy.context.object.data.shape='DISK'; bpy.context.object.data.size=4
bpy.context.scene.render.engine='BLENDER_EEVEE'
bpy.context.scene.render.resolution_x=720; bpy.context.scene.render.resolution_y=720; bpy.context.scene.render.resolution_percentage=100
bpy.context.scene.world.color=(0.25,0.25,0.25)
bpy.context.scene.render.image_settings.file_format='PNG'
bpy.context.scene.render.filepath=r'C:\Protergia\peuko_original_preview.png'
bpy.ops.render.render(write_still=True)
print('OUTPUT',bpy.context.scene.render.filepath)

