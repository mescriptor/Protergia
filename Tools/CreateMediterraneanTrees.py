"""Run with Blender 4.1 --background --python. Metre-scale authored tree assets."""
import bpy, math, random, json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Blender/MediterraneanTrees'
ASSETS = ROOT / 'Assets/ReferenceHouse/Vegetation'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
random.seed(739)

def material(name, color, roughness=.85):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Roughness'].default_value = roughness
    return m

mats = [material('Olive_Bark', (.16,.135,.10)),
        material('Olive_Bark_Light', (.18,.158,.12)),
        material('Olive_Bark_Furrow', (.13,.109,.080)),
        material('Olive_Leaf_Green', (.15,.205,.085)),
        material('Olive_Leaf_Sage', (.22,.265,.14)),
        material('Olive_Leaf_Silver', (.34,.38,.255)),
        material('Cypress_Bark', (.12,.075,.043)),
        material('Cypress_Deep_Green', (.035,.082,.025)),
        material('Cypress_Green', (.065,.125,.036)),
        material('Cypress_Tips', (.11,.17,.055))]

class Geometry:
    def __init__(self): self.v=[]; self.f=[]; self.mi=[]; self.smooth=[]
    def face(self, indices, mat, smooth=False):
        self.f.append(indices); self.mi.append(mat); self.smooth.append(smooth)
    def tube(self, points, radii, mat=0, sides=9, rugged=.09):
        start=len(self.v)
        for i, (p,r) in enumerate(zip(points,radii)):
            p=Vector(p)
            d=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(i-1,0)])
            q=d.to_track_quat('Z','Y')
            for j in range(sides):
                a=j*2*math.pi/sides
                rr=r*(1+rugged*math.sin(j*7.31+i*.45)+rugged*.5*math.sin(j*2.1))
                self.v.append(p+q@Vector((rr*math.cos(a),rr*math.sin(a),0)))
        self.face(tuple(start+j for j in reversed(range(sides))),mat)
        for i in range(len(points)-1):
            for j in range(sides):
                shade=mat
                if mat==0 and radii[i]>.04:
                    shade=[0,0,1,0,2,0,1,0,2][j%9]
                self.face((start+i*sides+j,start+i*sides+(j+1)%sides,
                           start+(i+1)*sides+(j+1)%sides,start+(i+1)*sides+j),shade,True)
        self.face(tuple(start+(len(points)-1)*sides+j for j in range(sides)),mat)
    def leaf(self, p, d, length, width, shade, silver=False, roll=0):
        d=Vector(d).normalized()
        q=d.to_track_quat('Z','Y')
        side=q@Vector((math.cos(roll),math.sin(roll),0))
        n=d.cross(side).normalized()
        p=Vector(p); start=len(self.v)
        # Folded lanceolate leaf: tapered tip, raised centre vein, curved silhouette.
        self.v.extend([p,p+d*length*.42+side*width*.5,
                       p+d*length*.48+n*width*.16,
                       p+d*length*.42-side*width*.5,
                       p+d*length+n*length*.08])
        for f in [(0,1,2),(1,4,2),(4,3,2),(3,0,2)]:
            self.face(tuple(start+i for i in f),shade)
            self.face(tuple(start+i for i in reversed(f)),5 if silver else shade)
    def object(self, name, parent):
        mesh=bpy.data.meshes.new(name)
        mesh.from_pydata(self.v,[],self.f); mesh.update()
        ob=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(ob)
        ob.parent=parent
        for m in mats: mesh.materials.append(m)
        for p,mi,sm in zip(mesh.polygons,self.mi,self.smooth):
            p.material_index=mi; p.use_smooth=sm
        return ob

def curve(a,b,bend=0):
    a,b=Vector(a),Vector(b)
    return [a.lerp(b,t)+Vector((math.sin(t*math.pi)*bend,math.sin(t*math.pi)*bend*.4,0)) for t in [0,.25,.5,.75,1]]

def build_olive():
    bark=Geometry(); leaves=[]
    trunk=[(0,0,0),(.035,-.02,.22),(-.035,.025,.6),(.04,.04,1.03),(.02,0,1.5)]
    bark.tube(trunk,[.23,.185,.135,.115,.075],0,22,.24)
    for i in range(7):
        a=i*math.tau/7
        bark.tube([(math.cos(a)*.43,math.sin(a)*.43,.015),
                   (math.cos(a)*.20,math.sin(a)*.20,.1),(.015,0,.32)],[.026,.075,.1],0,9,.2)
    for i in range(5):
        a=i*math.tau/5+.18
        base=Vector((.015,.02,1.04+i*.07))
        end=Vector((math.cos(a)*1.10,math.sin(a)*1.10,2.45+random.uniform(-.28,.28)))
        bark.tube(curve(base,end,.14),[.095,.078,.055,.033,.022],0,12,.22)
        for j in range(7):
            t=.40+j*.085
            p=base.lerp(end,t)+Vector((math.sin(t*math.pi)*.14,math.sin(t*math.pi)*.056,0))
            aj=a+random.uniform(-1.25,1.25)
            e=p+Vector((math.cos(aj)*random.uniform(.65,1.12),math.sin(aj)*random.uniform(.65,1.12),random.uniform(.55,1.03)))
            bark.tube(curve(p,e,.09),[.025,.021,.016,.010,.004],0,7,.10)
            for k in range(28):
                u=random.uniform(.30,1)
                p2=p.lerp(e,u)+Vector((math.sin(u*math.pi)*.09,math.sin(u*math.pi)*.036,0))
                az=aj+random.uniform(-2.1,2.1)
                e2=p2+Vector((math.cos(az)*random.uniform(.25,.65),math.sin(az)*random.uniform(.25,.65),random.uniform(-.06,.48)))
                bark.tube([p2,p2.lerp(e2,.5),e2],[.0045,.0028,.0008],0,5,.02)
                d=(e2-p2).normalized(); side=d.cross(Vector((0,0,1))).normalized()
                for n in range(13):
                    p3=p2.lerp(e2,.1+n*.069)
                    for sign in [-1,1]:
                        ld=(d*.46+side*sign*.85+Vector((random.uniform(-.3,.3),random.uniform(-.3,.3),random.uniform(-.25,.5)))).normalized()
                        leaves.append((p3,ld,random.uniform(.085,.125),random.uniform(.019,.028),random.choice([3,3,3,4,4]),True,random.uniform(-1.9,1.9)))
    return bark,leaves

def build_cypress():
    bark=Geometry(); leaves=[]
    bark.tube([(0,0,0),(.02,0,.7),(.01,.02,2.4),(-.025,.025,4.5),(.015,0,5.8)],[.115,.09,.065,.025,.0015],6,12,.16)
    for level in range(39):
        z=.5+level*.132
        # Columnar Italian cypress, slight bulges and an organically tapered spire.
        width=.55*min(1,(z/.95))*(1-(z/5.95)**2)**.65
        for j in range(9):
            a=j*math.tau/9+level*2.399
            radius=width*random.uniform(.78,1.13)
            p=Vector((.01,0,z))
            end=p+Vector((math.cos(a)*radius,math.sin(a)*radius,random.uniform(.28,.48)))
            bark.tube([p,p.lerp(end,.5),end],[.012,.007,.001],6,5,.02)
            d=(end-p).normalized(); sideways=Vector((-math.sin(a),math.cos(a),0))
            for twig in range(8):
                t=.2+twig*.10
                p2=p.lerp(end,t)
                sign=1 if twig%2 else -1
                td=(d*.4+sideways*sign*.55+Vector((0,0,.85))).normalized()
                tip=p2+td*random.uniform(.18,.29)
                for n in range(10):
                    center=p2.lerp(tip,n/10)
                    for s in [-1,1]:
                        ld=(td*.75+sideways*s*.48).normalized()
                        leaves.append((center,ld,random.uniform(.047,.068),random.uniform(.010,.018),random.choices([7,8,9],[.35,.5,.15])[0],False,a*.3))
    return bark,leaves

stats=[]
for name, builder, pos in [('Olive',build_olive,(-2.2,0,0)),('ItalianCypress',build_cypress,(2.3,0,0))]:
    root=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(root)
    bark,leaves=builder()
    objects=[]
    for level, keep, size in [(0,1,1),(1,.48,1.19),(2,.19,1.55)]:
        random.seed(501)
        geo=Geometry()
        for i,leaf in enumerate(leaves):
            if random.random()>keep: continue
            p,d,l,w,mat,silver,roll=leaf
            geo.leaf(p,d,l*size,w*size,mat,silver,roll)
        foliage=geo.object(name+'_Foliage_LOD'+str(level),root)
        wood=bark.object(name+'_Wood_LOD'+str(level),root)
        if level>0:
            bpy.context.view_layer.objects.active=wood
            mod=wood.modifiers.new('Simplified branches','DECIMATE'); mod.ratio=.5 if level==1 else .22
            bpy.ops.object.modifier_apply(modifier=mod.name)
        # Join foliage and branches to keep one renderer per tree per LOD.
        bpy.ops.object.select_all(action='DESELECT'); foliage.select_set(True); wood.select_set(True)
        bpy.context.view_layer.objects.active=foliage; bpy.ops.object.join()
        foliage.name=name+'_LOD'+str(level)
        objects.append(foliage)
        stats.append({'tree':name,'lod':level,'vertices':len(foliage.data.vertices),'triangles':sum(len(p.vertices)-2 for p in foliage.data.polygons)})
    bpy.ops.object.select_all(action='DESELECT'); root.select_set(True)
    for ob in objects: ob.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(ASSETS/'Models'/f'{name}.fbx'),use_selection=True,
        object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
        bake_space_transform=True,use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False)
    root.location=pos
    for ob in objects[1:]: ob.hide_render=True; ob.hide_set(True)

(ASSETS/'tree_materials.json').write_text(json.dumps({'materials':[{'name':m.name,'linearColor':list(m.diffuse_color)} for m in mats]},indent=2))
(OUT/'geometry_report.json').write_text(json.dumps(stats,indent=2))

# A separate studio collection makes the source file immediately inspectable.
studio=bpy.data.collections.new('Preview studio (not exported)'); bpy.context.scene.collection.children.link(studio)
def studio_object(ob):
    for col in list(ob.users_collection): col.objects.unlink(ob)
    studio.objects.link(ob)
bpy.ops.mesh.primitive_plane_add(size=200)
ground=bpy.context.object; ground.name='Preview ground'; studio_object(ground)
ground.data.materials.append(material('Studio sand',(.28,.27,.235)))
bpy.ops.object.camera_add(location=(8,-18,8))
camera=bpy.context.object; studio_object(camera)
camera.rotation_euler=(Vector((0,0,2.8))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type='ORTHO'; camera.data.ortho_scale=10.8; bpy.context.scene.camera=camera
bpy.ops.object.light_add(type='AREA',location=(-4,-6,10))
key=bpy.context.object; studio_object(key); key.data.energy=1900; key.data.shape='DISK'; key.data.size=7
key.rotation_euler=(Vector((0,0,2))-key.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.light_add(type='SUN',location=(0,0,8))
sun=bpy.context.object; studio_object(sun); sun.rotation_euler=(.45,-.4,-.6); sun.data.energy=2; sun.data.angle=.12
scene=bpy.context.scene; scene.world.color=(.35,.35,.35)
scene.render.engine='CYCLES'; scene.cycles.samples=32
scene.cycles.use_denoising=True
scene.render.resolution_x=1600; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
scene.render.image_settings.file_format='PNG'; scene.render.filepath=str(OUT/'Trees_Blender.png')
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'MediterraneanTrees.blend'))
bpy.ops.render.render(write_still=True)
print('MEDITERRANEAN_TREES_COMPLETE',json.dumps(stats))
