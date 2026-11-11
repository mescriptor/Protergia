import bpy, os, math, random, json
from mathutils import Vector
OUT=r'C:\Protergia\output\peuko_density'
bpy.ops.wm.open_mainfile(filepath=os.path.join(OUT,'peuko_planes_dense.blend'))
scene=bpy.context.scene
fol=next(o for o in scene.objects if o.name.startswith('Pine foliage |'))
old=fol.data
material=old.materials[0]
random.seed(517)
verts=[]
faces=[]
face_uvs=[]
original_centers=[]
new_centers=[]
max_deviation=0
NU,NV=4,3
for poly in old.polygons:
    assert len(poly.vertices)==4
    p0,p1,p2,p3=[old.vertices[i].co.copy() for i in poly.vertices]
    center=(p0+p1+p2+p3)/4
    long_axis=p1-p0
    short_axis=p3-p0
    normal=long_axis.cross(short_axis).normalized()
    length=long_axis.length
    sign=random.choice((-1,1))
    bend_length=length*.028*random.uniform(.85,1.15)*sign
    bend_width=length*.042*random.uniform(.85,1.15)*sign
    twist=length*random.uniform(-.009,.009)
    offsets=[]
    for j in range(NV+1):
        for i in range(NU+1):
            x=2*i/NU-1
            y=2*j/NV-1
            offsets.append(bend_length*x*x+bend_width*y*y+twist*x*y)
    mean_offset=sum(offsets)/len(offsets)
    start=len(verts)
    for j in range(NV+1):
        for i in range(NU+1):
            u,v=i/NU,j/NV
            offset=offsets[j*(NU+1)+i]-mean_offset
            max_deviation=max(max_deviation,abs(offset))
            point=center+long_axis*(u-.5)+short_axis*(v-.5)+normal*offset
            verts.append(tuple(point))
    original_centers.append(center)
    new_centers.append(sum((Vector(p) for p in verts[start:]),Vector())/((NU+1)*(NV+1)))
    for j in range(NV):
        for i in range(NU):
            a=start+j*(NU+1)+i
            faces.append((a,a+1,a+1+(NU+1),a+(NU+1)))
            face_uvs.append(((i/NU,j/NV),((i+1)/NU,j/NV),((i+1)/NU,(j+1)/NV),(i/NU,(j+1)/NV)))

new=bpy.data.meshes.new('Pine foliage cards | gentle two-axis bends')
new.from_pydata(verts,[],faces)
new.update()
uv=new.uv_layers.new(name='FoliageUV')
for poly,coords in zip(new.polygons,face_uvs):
    poly.use_smooth=True
    for loop,coord in zip(poly.loop_indices,coords):
        uv.data[loop].uv=coord
new.materials.append(material)
fol.data=new
fol['plane_count']=len(old.polygons)
fol['surface_panels_per_card']=NU*NV
fol['curvature']='Gentle compound bends; original orientation and card centers retained'
fol['camera_alignment']=False
fol['max_bend_distance']=max_deviation
scene.render.filepath='//peuko_planes_dense_preview.png'
scene.cycles.samples=32
scene.render.resolution_percentage=100
report={'cards':len(old.polygons),'mesh_quads':len(faces),'max_bend':max_deviation,
        'max_center_shift':max((a-b).length for a,b in zip(original_centers,new_centers)),
        'camera_alignment':False}
with open(os.path.join(OUT,'bend_report.json'),'w',encoding='utf8') as f:json.dump(report,f,indent=2)
print('BEND_REPORT',json.dumps(report),flush=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT,'peuko_planes_dense_curved.blend'))
scene.render.resolution_percentage=60
scene.cycles.samples=20
scene.render.filepath=os.path.join(OUT,'curved_draft.png')
bpy.ops.render.render(write_still=True)
