import bpy, math, random, os, json
from collections import defaultdict
from mathutils import Vector, Quaternion
from mathutils.kdtree import KDTree

random.seed(20261110)
SOURCE = r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko\peuko_planes.blend'
OUT = r'C:\Protergia\output\peuko_density'
bpy.ops.wm.open_mainfile(filepath=SOURCE)
scene = bpy.context.scene
tree = next(o for o in scene.objects if o.type == 'MESH' and o.name.startswith('Peuko |'))
fol = next(o for o in scene.objects if o.type == 'MESH' and o.name.startswith('Pine foliage planes'))
old_count = len(fol.data.polygons)
material = fol.data.materials[0]
mesh = tree.data
uv = mesh.uv_layers.active.data
tex = bpy.data.images.get('texture_diffuse.png')
pixels = tex.pixels[:]
iw, ih = tex.size

def rgb_at(p):
    # Blender image pixels and UVs both have their origin at bottom left.
    x = max(0, min(iw - 1, int(p.x * (iw - 1))))
    y = max(0, min(ih - 1, int(p.y * (ih - 1))))
    i = (y * iw + x) * 4
    return Vector(pixels[i:i + 3])

leaves, wood = [], []
for p in mesh.polygons:
    uvs = [uv[i].uv.copy() for i in p.loop_indices]
    middle = sum(uvs, Vector((0, 0))) / len(uvs)
    samples = [rgb_at(middle)] + [rgb_at(middle * .6 + u * .4) for u in uvs]
    r, g, b = sum(samples, Vector((0, 0, 0))) / len(samples)
    point = tree.matrix_world @ p.center
    if point.z < .56:
        continue
    if g > r * 1.08 and g > b * 1.2:
        leaves.append((point, p.area))
    elif g < r * 1.035 and b > g * .55:
        wood.append(point)

# Foliage anchors follow the actual green parts of the model, grouped into
# irregular branch-tip pads like the photographed Aleppo pine.
cells = defaultdict(list)
for point, area in leaves:
    key = tuple(math.floor(v / .145) for v in point)
    cells[key].append((point, area))
candidates = []
for group in cells.values():
    total = sum(a for _, a in group)
    if len(group) < 3 or total < .00008:
        continue
    center = sum((p * a for p, a in group), Vector()) / total
    candidates.append((center, total))
anchors = []
for center, area in sorted(candidates, key=lambda p: -p[1]):
    if all((center - other[0]).length > .090 for other in anchors):
        anchors.append((center, area))

kd = KDTree(len(wood))
for i, p in enumerate(wood):
    kd.insert(p, i)
kd.balance()

verts, faces, card_uvs, widths = [], [], [], []
for center, area in anchors:
    radial = Vector((center.x + .025, center.y - .02, 0))
    if radial.length < .04:
        radial = Vector((math.cos(center.z * 13), math.sin(center.z * 13), 0)) * .08
    outward = radial.normalized()
    local_wood = kd.find_range(center, .20)
    inward_points = [(p, distance) for p, _, distance in local_wood
                     if (center - p).dot(outward) > .015]
    if inward_points:
        root, _ = min(inward_points, key=lambda item: item[1])
        local_axis = (center - root).normalized()
        local_axis.z = max(-.1, min(.45, local_axis.z))
        axis = (outward * .72 + local_axis * .28).normalized()
    else:
        axis = outward.copy()
    # Predominantly spreading boughs, with the small top shoots lifting upward.
    axis.z = .17 + (.35 if center.z > 1.55 and radial.length < .28 else 0)
    axis.normalize()
    width = min(.58, .43 + math.sqrt(area) * .9) * random.uniform(.92, 1.08)
    for j, roll in enumerate((-65, -37, -10, 18, 45, 74)):
        yaw = math.radians(random.uniform(-28, 28))
        direction = Quaternion(Vector((0, 0, 1)), yaw) @ axis
        direction.z += random.uniform(-.10, .12)
        direction.normalize()
        tangent = Vector((-direction.y, direction.x, 0)).normalized()
        side = Quaternion(direction, math.radians(roll + random.uniform(-8, 8))) @ tangent
        side.normalize()
        length = width * random.uniform(.88, 1.12)
        height = length * random.uniform(.49, .58)
        # Cards overlap into the bough, with their textured twig bases inward.
        c = center + direction * random.uniform(-.045, .055)
        c += tangent * random.uniform(-.042, .042) + Vector((0, 0, random.uniform(.005, .055)))
        start = len(verts)
        verts.extend(tuple(p) for p in (
            c - direction * length / 2 - side * height / 2,
            c + direction * length / 2 - side * height / 2,
            c + direction * length / 2 + side * height / 2,
            c - direction * length / 2 + side * height / 2))
        faces.append((start, start + 1, start + 2, start + 3))
        card_uvs.append(((0, 0), (1, 0), (1, 1), (0, 1)))
        widths.append(length)

new_mesh = bpy.data.meshes.new('Pine | large overlapping branch-tip cards')
new_mesh.from_pydata(verts, [], faces)
new_mesh.update()
new_uv = new_mesh.uv_layers.new(name='FoliageUV')
for p, coords in zip(new_mesh.polygons, card_uvs):
    for loop, val in zip(p.loop_indices, coords):
        new_uv.data[loop].uv = val
new_mesh.materials.append(material)
fol.data = new_mesh
fol.matrix_world.identity()
fol.name = 'Pine foliage | dense branch-tip groups'
fol['foliage_clumps'] = len(anchors)
fol['crossed_planes_per_clump'] = 6
fol['plane_count'] = len(faces)
fol['placement'] = 'UV identified foliage; outward twig bases; overlapping bough-tip pads'
fol['reference'] = 'Pinus halepensis, Castelleone di Suasa, Wikimedia Commons'
fol['reference_url'] = 'https://commons.wikimedia.org/wiki/File:Pino_secolare_-_Castelleone_di_Suasa.JPG'
for node in material.node_tree.nodes:
    if node.type == 'HUE_SAT':
        node.inputs['Value'].default_value = .86
        node.inputs['Saturation'].default_value = 1.0

# Preserve self-contained textures and use paths appropriate to the final folder.
for image in bpy.data.images:
    if image.source == 'FILE':
        if not image.packed_file:
            image.pack()
        image.filepath = '//' + os.path.basename(image.filepath.replace('\\', '/'))

scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.cycles.preview_samples = 12
scene.cycles.use_denoising = True
scene.cycles.use_preview_denoising = True
scene.cycles.transparent_max_bounces = 64
scene.render.resolution_x = 1100
scene.render.resolution_y = 1400
scene.render.resolution_percentage = 100
scene.render.filepath = '//peuko_planes_dense_preview.png'

# Fit the widened crown without clipping the outer foliage.
bpy.context.view_layer.update()
cam = scene.camera
inv = cam.matrix_world.inverted()
points = [inv @ (o.matrix_world @ v.co) for o in (tree, fol) for v in o.data.vertices]
xmin, xmax = min(p.x for p in points), max(p.x for p in points)
ymin, ymax = min(p.y for p in points), max(p.y for p in points)
aspect = scene.render.resolution_x / scene.render.resolution_y
cam.data.ortho_scale = max(ymax - ymin, (xmax - xmin) / aspect) * 1.10
cam.location += cam.rotation_euler.to_quaternion() @ Vector(((xmin + xmax) / 2, (ymin + ymax) / 2, 0))
for o in bpy.context.selected_objects:
    o.select_set(False)
fol.select_set(True)
bpy.context.view_layer.objects.active = fol
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            space = area.spaces.active
            space.region_3d.view_perspective = 'CAMERA'
            space.shading.type = 'RENDERED'
            space.overlay.show_overlays = False

report = dict(previous_planes=old_count, planes=len(faces), clumps=len(anchors),
              average_width=sum(widths)/len(widths), min_width=min(widths), max_width=max(widths),
              foliage_samples=len(leaves), reference=fol['reference_url'])
with open(os.path.join(OUT, 'density_report.json'), 'w', encoding='utf8') as f:
    json.dump(report, f, indent=2)
print('DENSITY_REPORT', json.dumps(report), flush=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'peuko_planes_dense.blend'))
scene.render.resolution_percentage = 60
scene.cycles.samples = 20
scene.render.filepath = os.path.join(OUT, 'density_draft.png')
bpy.ops.render.render(write_still=True)
