import bpy, os
from collections import Counter
from mathutils import Vector
p=r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko'
for o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)
bpy.ops.wm.obj_import(filepath=os.path.join(p,'base.obj'))
o=next(o for o in bpy.data.objects if o.type=='MESH'); m=o.data
im=bpy.data.images.load(os.path.join(p,'texture_diffuse.png')); px=im.pixels[:]; w,h=im.size; uv=m.uv_layers.active.data
counts=Counter(); zvals=[]
for poly in m.polygons:
 u=sum(uv[i].uv.x for i in poly.loop_indices)/len(poly.loop_indices); v=sum(uv[i].uv.y for i in poly.loop_indices)/len(poly.loop_indices)
 x=max(0,min(w-1,int(u*(w-1)))); y=max(0,min(h-1,int((1-v)*(h-1)))); k=(y*w+x)*4
 r,g,b=px[k],px[k+1],px[k+2]
 c=sum((m.vertices[i].co for i in poly.vertices),Vector())/len(poly.vertices)
 isgreen=g>r*1.12 and g>b*1.08 and g>0.12
 counts['all']+=1
 if isgreen:
  counts['green']+=1; counts['high']+= c.z>=.56; zvals.append(c.z)
print('OBJECT',o.name,o.matrix_world[:]); print('COUNTS',counts)
print('ZRANGE',min(zvals) if zvals else None,max(zvals) if zvals else None)
print('ZBINS',[sum(1 for z in zvals if i*.2<=z<(i+1)*.2) for i in range(9)])
