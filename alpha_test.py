import bpy, os
for o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)
img=bpy.data.images.load(r'C:\Protergia\pine_sprig_card.png')
print('ALPHA_MODE',img.alpha_mode,'RENDER_METHODS', [e.identifier for e in bpy.data.materials.new('probe').bl_rna.properties['surface_render_method'].enum_items])
for i,method in enumerate(['DITHERED','BLENDED']):
 m=bpy.data.materials.new('m'+method); m.use_nodes=True; m.surface_render_method=method
 ns=m.node_tree.nodes; lk=m.node_tree.links; bs=ns.get('Principled BSDF'); tx=ns.new('ShaderNodeTexImage'); tx.image=img; lk.new(tx.outputs['Color'],bs.inputs['Base Color']); lk.new(tx.outputs['Alpha'],bs.inputs['Alpha'])
 bpy.ops.mesh.primitive_plane_add(size=1.8,location=(i*2-1,0,0)); ob=bpy.context.object; ob.name=method; ob.data.materials.append(m); ob.rotation_euler[1]=1.5708
bpy.ops.mesh.primitive_plane_add(size=10,location=(0,0,-1)); bpy.context.object.name='ground'; bpy.context.object.data.materials.append(bpy.data.materials.new('groundmat'))
# camera and light
bpy.ops.object.camera_add(location=(0,-4,0.2)); c=bpy.context.object; c.data.type='ORTHO'; c.data.ortho_scale=5; c.rotation_euler=(1.5708,0,0); bpy.context.scene.camera=c
bpy.ops.object.light_add(type='AREA',location=(0,-2,3)); bpy.context.object.data.energy=800; bpy.context.object.data.size=5; bpy.context.object.rotation_euler=(0,0,0)
s=bpy.context.scene; s.render.engine='BLENDER_EEVEE'; s.eevee.taa_render_samples=16; s.render.resolution_x=1000; s.render.resolution_y=500; s.render.resolution_percentage=100; s.render.image_settings.file_format='PNG'; s.render.filepath=r'C:\Protergia\alpha_test.png'
bpy.ops.render.render(write_still=True)
