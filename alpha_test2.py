import bpy, os
from mathutils import Vector
for o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)
img=bpy.data.images.load(r'C:\Protergia\pine_sprig_card.png')
for i,method in enumerate(['DITHERED','BLENDED']):
 m=bpy.data.materials.new('m'+method); m.use_nodes=True; m.surface_render_method=method
 ns=m.node_tree.nodes; lk=m.node_tree.links; bs=ns.get('Principled BSDF'); tx=ns.new('ShaderNodeTexImage'); tx.image=img; lk.new(tx.outputs['Color'],bs.inputs['Base Color']); lk.new(tx.outputs['Alpha'],bs.inputs['Alpha'])
 bpy.ops.mesh.primitive_plane_add(size=1.8,location=((i*2-1),0,0)); bpy.context.object.name=method; bpy.context.object.data.materials.append(m)
g=bpy.data.materials.new('ground'); g.diffuse_color=(.8,.8,.8,1); g.use_nodes=True; g.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.8,.8,.8,1)
bpy.ops.mesh.primitive_plane_add(size=8,location=(0,0,-.2)); bpy.context.object.name='ground'; bpy.context.object.data.materials.append(g)
bpy.ops.object.camera_add(location=(0,0,4)); c=bpy.context.object; c.rotation_euler=(Vector((0,0,0))-c.location).to_track_quat('-Z','Y').to_euler(); c.data.type='ORTHO'; c.data.ortho_scale=5; bpy.context.scene.camera=c
bpy.ops.object.light_add(type='AREA',location=(0,-2,3)); l=bpy.context.object; l.data.energy=800; l.data.size=5; l.rotation_euler=(Vector((0,0,0))-l.location).to_track_quat('-Z','Y').to_euler()
s=bpy.context.scene; s.render.engine='BLENDER_EEVEE'; s.eevee.taa_render_samples=16; s.render.resolution_x=1000; s.render.resolution_y=500; s.render.resolution_percentage=100; s.render.image_settings.file_format='PNG'; s.render.filepath=r'C:\Protergia\alpha_test2.png'
bpy.ops.render.render(write_still=True)
