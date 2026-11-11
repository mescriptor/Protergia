import bpy, os, math
from collections import defaultdict
from mathutils import Vector
p=r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko'
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.wm.obj_import(filepath=os.path.join(p,'base.obj'))
obj=next(o for o in bpy.data.objects if o.type=='MESH'); mesh=obj.data
img=bpy.data.images.load(os.path.join(p,'texture_diffuse.png'),check_existing=True); pix=img.pixels[:]; W,H=img.size[:]; uv=mesh.uv_layers.active.data
def color_at(u,v):
 x=max(0,min(W-1,int(u*(W-1)))); y=max(0,min(H-1,int((1-v)*(H-1)))); i=(y*W+x)*4; return pix[i],pix[i+1],pix[i+2]
greenfaces=[]; vfaces=defaultdict(list)
for poly in mesh.polygons:
 u=sum(uv[i].uv.x for i in poly.loop_indices)/len(poly.loop_indices); v=sum(uv[i].uv.y for i in poly.loop_indices)/len(poly.loop_indices)
 r,g,b=color_at(u,v)
 if g>r*1.12 and g>b*1.08 and g>0.12:
  greenfaces.append(poly.index)
  for vi in poly.vertices: vfaces[vi].append(poly.index)
parent={f:f for f in greenfaces}
def find(x):
 while parent[x]!=x: parent[x]=parent[parent[x]]; x=parent[x]
 return x
def union(a,b):
 ra,rb=find(a),find(b)
 if ra!=rb: parent[rb]=ra
for fs in vfaces.values():
 for f in fs[1:]: union(fs[0],f)
comps=defaultdict(list)
for f in greenfaces: comps[find(f)].append(f)
items=[]
for fs in comps.values():
 verts=sorted(set(v for f in fs for v in mesh.polygons[f].vertices))
 pts=[obj.matrix_world@mesh.vertices[v].co for v in verts]
 c=sum(pts,Vector())/len(pts)
 mn=[min(p[i] for p in pts) for i in range(3)]; mx=[max(p[i] for p in pts) for i in range(3)]
 dims=[mx[i]-mn[i] for i in range(3)]
 items.append((len(fs),len(verts),c.z,math.hypot(c.x,c.y),max(dims)))
print('components',len(items))
print('largest',sorted(items,reverse=True)[:20])
print('vertical bins',[sum(1 for x in items if i*.2<=x[2]<(i+1)*.2) for i in range(9)])
