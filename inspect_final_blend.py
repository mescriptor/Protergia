import bpy
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath=r'C:\Protergia\peuko_planes.blend')
for o in bpy.data.objects:
 if o.type=='MESH': print('OBJ',o.name,'faces',len(o.data.polygons),'hidden',o.hide_render,'collections',[c.name for c in o.users_collection])
obj=bpy.data.objects.get('Peuko | trunk and branches'); img=bpy.data.images.get('texture_diffuse.png')
px=img.pixels[:]; w,h=img.size; uv=obj.data.uv_layers.active.data; cnt=0
for poly in obj.data.polygons:
 u=sum(uv[i].uv.x for i in poly.loop_indices)/len(poly.loop_indices); v=sum(uv[i].uv.y for i in poly.loop_indices)/len(poly.loop_indices); x=max(0,min(w-1,int(u*(w-1)))); y=max(0,min(h-1,int((1-v)*(h-1)))); k=(y*w+x)*4; r,g,b=px[k],px[k+1],px[k+2]
 if g>r*1.12 and g>b*1.08 and g>.12: cnt+=1
print('VISIBLE_GREEN_REMAIN',cnt)
