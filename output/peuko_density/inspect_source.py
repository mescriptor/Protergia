import bpy, json
from mathutils import Vector
from collections import Counter
bpy.ops.wm.open_mainfile(filepath=r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko\peuko_planes.blend')
o=next(o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('Peuko |'))
m=o.data;uv=m.uv_layers.active.data; im=bpy.data.images.get('texture_diffuse.png');px=im.pixels[:];w,h=im.size
for flip in [False,True]:
 count=0;bins=Counter();pts=[]
 for p in m.polygons:
  u=sum(uv[i].uv.x for i in p.loop_indices)/len(p.loop_indices); v=sum(uv[i].uv.y for i in p.loop_indices)/len(p.loop_indices)
  x=max(0,min(w-1,int(u*(w-1)))); y=max(0,min(h-1,int((1-v if flip else v)*(h-1)))); k=(y*w+x)*4;r,g,b=px[k:k+3]
  if g>r*1.08 and g>b*1.2:
   c=o.matrix_world @ p.center;count+=1; bins[round(c.z,1)]+=1;pts.append(c)
 print('flip',flip,'green',count,'z',sorted(bins.items()))
print('MESH',o.name,len(m.polygons),'CARD_QUADS',len(next(x for x in bpy.data.objects if x.name.startswith('Pine foliage planes')).data.polygons))
