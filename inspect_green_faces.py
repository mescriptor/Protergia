import bpy, os, math
from collections import defaultdict
from mathutils import Vector
p=r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko'
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.wm.obj_import(filepath=os.path.join(p,'base.obj'))
obj=next(o for o in bpy.data.objects if o.type=='MESH')
img=bpy.data.images.load(os.path.join(p,'texture_diffuse.png'),check_existing=True)
pix=img.pixels[:]; W,H=img.size[:]
def color_at(u,v):
 x=max(0,min(W-1,int(u*(W-1)))); y=max(0,min(H-1,int((1-v)*(H-1))))
 i=(y*W+x)*4; return pix[i],pix[i+1],pix[i+2]
mesh=obj.data; uv=mesh.uv_layers.active.data
centers=[]; green=0
for poly in mesh.polygons:
 u=sum(uv[i].uv.x for i in poly.loop_indices)/len(poly.loop_indices)
 v=sum(uv[i].uv.y for i in poly.loop_indices)/len(poly.loop_indices)
 r,g,b=color_at(u,v)
 if g>r*1.12 and g>b*1.08 and g>0.12:
  green+=1
  c=sum((obj.matrix_world@mesh.vertices[i].co for i in poly.vertices),Vector())/len(poly.vertices)
  centers.append((c,r,g,b))
print('faces',len(mesh.polygons),'green',green,'centroids',len(centers))
for i in range(9):
 z0=i*0.2; z1=z0+.2; group=[x for x in centers if z0<=x[0].z<z1]
 print('z',round(z0,1),round(z1,1),'n',len(group),'ravg',round(sum(math.hypot(x[0].x,x[0].y) for x in group)/len(group),3) if group else None)
# coarse spatial bins at 0.1 grid
bins=defaultdict(int)
for c,*_ in centers: bins[(round(c.x,1),round(c.y,1),round(c.z,1))]+=1
print('green bins',len(bins),'top bins',sorted(bins.items(),key=lambda x:-x[1])[:10])
