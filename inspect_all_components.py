import bpy, os, math
from collections import defaultdict
from mathutils import Vector
p=r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko'
for o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)
bpy.ops.wm.obj_import(filepath=os.path.join(p,'base.obj')); obj=next(o for o in bpy.data.objects if o.type=='MESH'); mesh=obj.data
img=bpy.data.images.load(os.path.join(p,'texture_diffuse.png')); px=img.pixels[:]; w,h=img.size; uv=mesh.uv_layers.active.data
vfaces=defaultdict(list); green=set()
for poly in mesh.polygons:
 for vi in poly.vertices: vfaces[vi].append(poly.index)
 u=sum(uv[i].uv.x for i in poly.loop_indices)/len(poly.loop_indices); v=sum(uv[i].uv.y for i in poly.loop_indices)/len(poly.loop_indices); x=max(0,min(w-1,int(u*(w-1)))); y=max(0,min(h-1,int((1-v)*(h-1)))); k=(y*w+x)*4; r,g,b=px[k:k+3]
 if g>r*1.12 and g>b*1.08 and g>.12: green.add(poly.index)
seen=set(); comps=[]
for pidx in range(len(mesh.polygons)):
 if pidx in seen: continue
 stack=[pidx]; seen.add(pidx); faces=[]
 while stack:
  fi=stack.pop(); faces.append(fi)
  for vi in mesh.polygons[fi].vertices:
   for nb in vfaces[vi]:
    if nb not in seen: seen.add(nb); stack.append(nb)
 pts=[]
 for fi in faces:
  poly=mesh.polygons[fi]
  pts.extend(obj.matrix_world@mesh.vertices[vi].co for vi in poly.vertices)
 c=sum(pts,Vector())/len(pts)
 comps.append((len(faces),sum(1 for f in faces if f in green),c.z))
print('ALL_FACE_COMPONENTS',len(comps),'GREEN_FACES',len(green))
for row in sorted(comps,reverse=True)[:25]: print(row,'ratio',round(row[1]/row[0],3))
print('components >20 faces',sum(1 for x in comps if x[0]>20),'green ratio >0.3',sum(1 for x in comps if x[1]/x[0]>.3))
