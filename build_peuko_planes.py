import bpy, os, math, random, bmesh
from collections import defaultdict
from mathutils import Vector, Quaternion

random.seed(44)
source_dir = r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko'
foliage_image_path = r'C:\Protergia\pine_sprig_card.png'
work_blend = r'C:\Protergia\peuko_planes.blend'
preview_path = '//peuko_planes_preview.png'

# Clear the startup scene and import the user's supplied OBJ.
for obj in list(bpy.data.objects): bpy.data.objects.remove(obj, do_unlink=True)
for coll in list(bpy.data.collections):
    if coll.name != 'Collection' and coll.users == 0:
        bpy.data.collections.remove(coll)
bpy.ops.wm.obj_import(filepath=os.path.join(source_dir, 'base.obj'))
source_obj = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH')
source_obj.name = 'Peuko | trunk, branches and original foliage'
source_obj.data.name = 'Peuko | source geometry'
scene = bpy.context.scene
root = scene.collection
visible_col = bpy.data.collections.new('01 | Original peuko model')
root.children.link(visible_col)
for col in list(source_obj.users_collection): col.objects.unlink(source_obj)
visible_col.objects.link(source_obj)

# Load and pack the original tree maps, then assign a complete PBR material.
source_mat = bpy.data.materials.new('Peuko | original atlas PBR')
source_mat.use_nodes = True
nodes = source_mat.node_tree.nodes
links = source_mat.node_tree.links
bsdf = nodes.get('Principled BSDF')
bsdf.inputs['Roughness'].default_value = 0.86

def load_tex(filename, non_color=False):
    img = bpy.data.images.load(os.path.join(source_dir, filename), check_existing=True)
    img.pack()
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = img
    tex.label = filename
    if non_color:
        tex.image.colorspace_settings.name = 'Non-Color'
    return tex
base_tex = load_tex('texture_diffuse.png')
links.new(base_tex.outputs['Color'], bsdf.inputs['Base Color'])
rough_tex = load_tex('texture_roughness.png', True)
links.new(rough_tex.outputs['Color'], bsdf.inputs['Roughness'])
normal_tex = load_tex('texture_normal.png', True)
normal_map = nodes.new('ShaderNodeNormalMap')
normal_map.inputs['Strength'].default_value = 0.72
links.new(normal_tex.outputs['Color'], normal_map.inputs['Color'])
links.new(normal_map.outputs['Normal'], bsdf.inputs['Normal'])

# Identify the source's broadleaf faces by their UV-mapped green atlas regions.
source_mesh = source_obj.data
uv_layer = source_mesh.uv_layers.active.data
img = base_tex.image
pixels = img.pixels[:]
tex_w, tex_h = img.size[:]
def atlas_rgb(u, v):
    x = max(0, min(tex_w - 1, int(u * (tex_w - 1))))
    y = max(0, min(tex_h - 1, int((1.0 - v) * (tex_h - 1))))
    k = (y * tex_w + x) * 4
    return pixels[k], pixels[k + 1], pixels[k + 2]
green_face_ids = set()
vertex_to_faces = defaultdict(list)
for poly in source_mesh.polygons:
    u = sum(uv_layer[i].uv.x for i in poly.loop_indices) / len(poly.loop_indices)
    v = sum(uv_layer[i].uv.y for i in poly.loop_indices) / len(poly.loop_indices)
    r, g, b = atlas_rgb(u, v)
    local_center = sum((source_mesh.vertices[i].co for i in poly.vertices), Vector()) / len(poly.vertices)
    center = source_obj.matrix_world @ local_center
    if center.z >= 0.56 and g > r * 1.12 and g > b * 1.08 and g > 0.12:
        green_face_ids.add(poly.index)
        for vi in poly.vertices:
            vertex_to_faces[vi].append(poly.index)

# Cluster leaf-face islands into card anchor points.
parent = {f: f for f in green_face_ids}
def find(x):
    while parent[x] != x:
        parent[x] = parent[parent[x]]
        x = parent[x]
    return x
def union(a, b):
    ra, rb = find(a), find(b)
    if ra != rb: parent[rb] = ra
for face_ids in vertex_to_faces.values():
    for f in face_ids[1:]: union(face_ids[0], f)
components = defaultdict(list)
for f in green_face_ids: components[find(f)].append(f)
candidates = []
for face_ids in components.values():
    vert_ids = sorted(set(vi for f in face_ids for vi in source_mesh.polygons[f].vertices))
    points = [source_obj.matrix_world @ source_mesh.vertices[vi].co for vi in vert_ids]
    center = sum(points, Vector()) / len(points)
    dims = [max(p[i] for p in points) - min(p[i] for p in points) for i in range(3)]
    candidates.append((center, max(dims), len(face_ids)))
# Greedy spatial thinning stops repeated cards from forming one oversized ball.
selected = []
for center, extent, face_count in sorted(candidates, key=lambda item: (item[1], item[2]), reverse=True):
    if all((center - other[0]).length > 0.115 for other in selected):
        selected.append((center, extent, face_count))

source_obj.data.materials.clear()
source_obj.data.materials.append(source_mat)

# A packed transparent pine-sprig image forms crossed, textured foliage planes.
fol_image = bpy.data.images.load(foliage_image_path, check_existing=True)
fol_image.pack()
fol_image.alpha_mode = 'STRAIGHT'
fol_mat = bpy.data.materials.new('Pine needles | transparent cutout card')
fol_mat.use_nodes = True
fol_mat.diffuse_color = (0.08, 0.22, 0.08, 1)
fol_mat.surface_render_method = 'BLENDED'
fol_nodes = fol_mat.node_tree.nodes
fol_links = fol_mat.node_tree.links
fol_bsdf = fol_nodes.get('Principled BSDF')
fol_bsdf.inputs['Roughness'].default_value = 0.88
fol_tex = fol_nodes.new('ShaderNodeTexImage')
fol_tex.image = fol_image
fol_tex.label = 'Pine sprig cutout'
darken = fol_nodes.new('ShaderNodeHueSaturation')
darken.inputs['Saturation'].default_value = 1.15
darken.inputs['Value'].default_value = 0.72
fol_links.new(fol_tex.outputs['Color'], darken.inputs['Color'])
fol_links.new(darken.outputs['Color'], fol_bsdf.inputs['Base Color'])
transparent = fol_nodes.new('ShaderNodeBsdfTransparent')
mix = fol_nodes.new('ShaderNodeMixShader')
output = fol_nodes.get('Material Output')
fol_links.new(fol_tex.outputs['Alpha'], mix.inputs[0])
fol_links.new(transparent.outputs['BSDF'], mix.inputs[1])
fol_links.new(fol_bsdf.outputs['BSDF'], mix.inputs[2])
fol_links.new(mix.outputs['Shader'], output.inputs['Surface'])

# Add three crossed image cards at each spaced foliage anchor.
fol_col = bpy.data.collections.new('02 | Pine foliage planes')
root.children.link(fol_col)
card_verts = []
card_faces = []
card_uvs = []
card_sizes = []
for center, extent, face_count in selected:
    radial = Vector((center.x + 0.025, center.y - 0.02, 0.18 + max(0.0, center.z - 1.15) * 1.1))
    if radial.length < 1e-5: radial = Vector((1, 0, 0.35))
    axis = radial.normalized()
    up = Vector((0, 0, 1))
    up_proj = up - axis * up.dot(axis)
    if up_proj.length < 0.1:
        up_proj = Vector((-axis.y, axis.x, 0))
    up_proj.normalize()
    width = max(0.22, min(0.34, extent * 2.35)) * random.uniform(0.88, 1.12)
    height = width * (fol_image.size[1] / fol_image.size[0]) * random.uniform(0.91, 1.10)
    for card_index, angle in enumerate((0, math.radians(60), math.radians(120))):
        vertical = (Quaternion(axis, angle) @ up_proj).normalized()
        direction = axis
        # Small per-card rotation and offset break up repeated silhouettes.
        direction = (direction + vertical * random.uniform(-0.07, 0.07)).normalized()
        vertical = (vertical - direction * vertical.dot(direction)).normalized()
        w = width * random.uniform(0.92, 1.08)
        h = height * random.uniform(0.92, 1.08)
        offset = direction * random.uniform(-0.018, 0.025) + vertical * random.uniform(-0.012, 0.012)
        c = center + offset
        p0 = c - direction * (w * 0.5) - vertical * (h * 0.5)
        p1 = c + direction * (w * 0.5) - vertical * (h * 0.5)
        p2 = c + direction * (w * 0.5) + vertical * (h * 0.5)
        p3 = c - direction * (w * 0.5) + vertical * (h * 0.5)
        start = len(card_verts)
        card_verts.extend([tuple(p0), tuple(p1), tuple(p2), tuple(p3)])
        card_faces.append((start, start+1, start+2, start+3))
        if random.random() < 0.5:
            card_uvs.append(((1,0),(0,0),(0,1),(1,1)))
        else:
            card_uvs.append(((0,0),(1,0),(1,1),(0,1)))
        card_sizes.append((w, h))

card_mesh = bpy.data.meshes.new('Crossed pine foliage cards | planar quads')
card_mesh.from_pydata(card_verts, [], card_faces)
card_mesh.update()
uvs = card_mesh.uv_layers.new(name='FoliageUV')
for poly, uv_coords in zip(card_mesh.polygons, card_uvs):
    for loop_index, uv in zip(poly.loop_indices, uv_coords):
        uvs.data[loop_index].uv = uv
card_mesh.materials.append(fol_mat)
fol_obj = bpy.data.objects.new(f'Pine foliage planes | {len(selected)} clumps', card_mesh)
fol_col.objects.link(fol_obj)
fol_obj['foliage_clumps'] = len(selected)
fol_obj['crossed_planes_per_clump'] = 3
fol_obj['plane_count'] = len(card_faces)
fol_obj['texture'] = 'Packed transparent pine sprig cutout'

# Presentation setup with soft, balanced lighting and a portrait render.
rig = bpy.data.collections.new('03 | Camera and lighting')
root.children.link(rig)
def link_to_rig(obj):
    for col in list(obj.users_collection): col.objects.unlink(obj)
    rig.objects.link(obj)
floor_mat = bpy.data.materials.new('Preview ground | warm slate')
floor_mat.diffuse_color = (0.095, 0.115, 0.10, 1)
floor_mat.use_nodes = True
floor_mat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value = floor_mat.diffuse_color
floor_mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value = 1.0
bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-0.008))
floor = bpy.context.object
floor.name = 'Preview ground'
link_to_rig(floor)
floor.data.materials.append(floor_mat)
bpy.ops.object.camera_add(location=(3.25, -5.1, 2.35))
camera = bpy.context.object
camera.name = 'Preview camera'
link_to_rig(camera)
target = Vector((0,0,0.88))
camera.rotation_euler = (target - camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 2.30
scene.camera = camera
def add_area(name, location, energy, size, color):
    bpy.ops.object.light_add(type='AREA', location=location)
    light = bpy.context.object
    light.name = name
    link_to_rig(light)
    light.data.energy = energy
    light.data.shape = 'DISK'
    light.data.size = size
    light.data.color = color
    light.rotation_euler = (Vector((0,0,0.95)) - light.location).to_track_quat('-Z','Y').to_euler()
add_area('Key | soft daylight', (2.4,-3.5,4.2), 280, 3.1, (1.0,0.91,0.76))
add_area('Fill | cool sky', (-3.0,-1.2,2.6), 120, 3.8, (0.72,0.84,1.0))
add_area('Rim | canopy edge', (0.8,3.0,3.4), 180, 2.8, (0.82,1.0,0.78))
scene.world.color = (0.10,0.12,0.11)
scene.render.engine = 'CYCLES'
scene.cycles.samples = 32
scene.cycles.use_denoising = True
scene.cycles.transparent_max_bounces = 16
scene.render.resolution_x = 1100
scene.render.resolution_y = 1400
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = preview_path
scene.view_settings.view_transform = 'AgX'
scene.render.film_transparent = False
for obj in bpy.context.selected_objects: obj.select_set(False)
fol_obj.select_set(True)
bpy.context.view_layer.objects.active = fol_obj
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            area.spaces.active.region_3d.view_distance = 3.5
            area.spaces.active.region_3d.view_location = (0,0,0.9)
            area.spaces.active.region_3d.view_rotation = camera.rotation_euler.to_quaternion()

bpy.ops.wm.save_as_mainfile(filepath=work_blend)
bpy.ops.render.render(write_still=True)
print('SAVED', work_blend)
print('PREVIEW', preview_path)
print('SOURCE_GREEN_FACES', len(green_face_ids))
print('FOLIAGE_ANCHORS', len(selected), 'PLANES', len(card_faces))
