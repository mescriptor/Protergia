import bpy, os, json
import numpy as np

OUT = r'C:\Protergia\output\peuko_density'
SOURCE = r'C:\Users\Yannis\OneDrive\Υπολογιστής\Protergia\Models\peuko\peuko_planes_dense.blend'
FACTOR = 1.45
bpy.ops.wm.open_mainfile(filepath=SOURCE)
scene = bpy.context.scene
fol = next(o for o in scene.objects if o.type == 'MESH' and o.name.startswith('Pine foliage |'))
mesh = fol.data
parent = list(range(len(mesh.vertices)))
def find(i):
    while parent[i] != i:
        parent[i] = parent[parent[i]]
        i = parent[i]
    return i
for edge in mesh.edges:
    a, b = (find(i) for i in edge.vertices)
    if a != b:
        parent[b] = a
groups = {}
for i in range(len(mesh.vertices)):
    groups.setdefault(find(i), []).append(i)

old_max, new_max, center_error, normal_error = 0.0, 0.0, 0.0, 0.0
for indices in groups.values():
    points = np.array([mesh.vertices[i].co[:] for i in indices], dtype=np.float64)
    center = points.mean(axis=0)
    local = points - center
    _, vectors = np.linalg.eigh(local.T @ local)
    normal = vectors[:, 0]
    offsets = local @ normal
    updated = points + (FACTOR - 1) * offsets[:, None] * normal[None, :]
    for index, point in zip(indices, updated):
        mesh.vertices[index].co = point.tolist()
    old_max = max(old_max, float(np.abs(offsets).max()))
    new_max = max(new_max, float(np.abs((updated - center) @ normal).max()))
    center_error = max(center_error, float(np.linalg.norm(updated.mean(axis=0) - center)))
    centered = updated - updated.mean(axis=0)
    _, new_vectors = np.linalg.eigh(centered.T @ centered)
    normal_error = max(normal_error, float(1 - abs(normal @ new_vectors[:, 0])))

mesh.update()
fol['bend_increase_from_previous'] = FACTOR
fol['max_bend_distance'] = new_max
fol['camera_alignment'] = False
fol['curvature'] = 'Two-axis bend increased 45%; card centers and average orientations preserved'
scene.render.filepath = '//peuko_planes_dense_v2_preview.png'
scene.render.resolution_percentage = 100
scene.cycles.samples = 32
report = dict(cards=len(groups), bend_factor=FACTOR, previous_max_bend=old_max,
              new_max_bend=new_max, max_center_change=center_error,
              max_normal_dot_error=normal_error, camera_alignment=False,
              packed_images=all(im.packed_file for im in bpy.data.images if im.source == 'FILE'))
with open(os.path.join(OUT, 'bend_v2_report.json'), 'w', encoding='utf8') as f:
    json.dump(report, f, indent=2)
print('BEND_V2', json.dumps(report), flush=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, 'peuko_planes_dense_v2.blend'))
bpy.ops.render.render(write_still=True)
print('FINAL_PREVIEW_READY', flush=True)
