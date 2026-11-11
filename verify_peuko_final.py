import bpy, os
bpy.ops.wm.open_mainfile(filepath=r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko\peuko_planes.blend')
scene=bpy.context.scene
print('ENGINE',scene.render.engine,'OUTPUT',scene.render.filepath,'RESOLUTION',scene.render.resolution_x,scene.render.resolution_y)
for o in bpy.data.objects:
 if o.type=='MESH': print('MESH',o.name,'faces',len(o.data.polygons),'hidden',o.hide_render)
for img in bpy.data.images:
 if img.source=='FILE': print('IMAGE',img.name,'packed',bool(img.packed_file),'path',img.filepath)
print('OUTPUT_ABS',bpy.path.abspath(scene.render.filepath))
