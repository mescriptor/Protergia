import bpy, os
from collections import Counter
from mathutils import Vector
p=r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko'
for o in list(bpy.data.objects): bpy.data.objects.remove(o,do_unlink=True)
bpy.ops.wm.obj_import(filepath=os.path.join(p,'base.obj')); obj=next(o for o in bpy.data.objects if o.type=='MESH'); m=obj.data
im=bpy.data.images.load(os.path.join(p,'texture_diffuse.png')); px=im.pixels[:]; w,h=im.size; uv=m.uv_layers.active.data
cnt=Counter()
def sample(u,v):
 x=max(0,min(w-1,int(u*(w-1)))); y=max(0,min(h-1,int((1-v)*(h-1)))); k=(y*w+x)*4; return tuple(px[k:k+3])
for poly in m.polygons:
 uvpts=[uv[i].uv.copy() for i in poly.loop_indices]
 samples=[sample(sum(p.x for p in uvpts)/len(uvpts),sum(p.y for p in uvpts)/len(uvpts))]+[sample(p.x,p.y) for p in uvpts]
 flags=[g>r*1.12 and g>b*1.08 and g>.12 for r,g,b in samples]
 c=sum((obj.matrix_world@m.vertices[i].co for i in poly.vertices),Vector())/len(poly.vertices)
 cnt['total']+=1
 for tag,val in [('center',flags[0]),('any',any(flags)),('2plus',sum(flags)>=2),('all',all(flags))]:
  if val: cnt[tag]+=1; 
  if val and c.z>=.56: cnt[tag+'_high']+=1
print(dict(cnt))
