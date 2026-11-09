"""Publish verified V10 lighting and the three shared charcoal fascia surfaces."""
from pathlib import Path
import hashlib
import json
import re
import shutil
import sys

ROOT = Path(__file__).resolve().parents[1]
LIVE = ROOT / 'Assets/ReferenceHouse'
STAGE = ROOT / 'Tools/UnityBuild/Assets/ReferenceHouse'
REPORTS = ROOT / 'Blender/ReferenceHouse'
BACKUP = REPORTS / 'BeforeContrastV10'
FASCIA = 'Materials/Charcoal_architectural_frame.mat'
SCENE = 'Scenes/ReferenceHouse.unity'


def hashes(root):
    return {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in root.rglob('*') if p.is_file()}


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def transform_overrides(path):
    pattern = re.compile(r'- target: \{fileID: ([-\d]+), guid: ([a-f0-9]+), type: \d+\}\s+'
                         r'propertyPath: (m_Local(?:Position|Rotation|Scale)\.[xyzw])\s+value: ([^\n]+)')
    return {(target, guid, prop): float(value)
            for target, guid, prop, value in pattern.findall(path.read_text(encoding='utf-8-sig'))}


def camera_blocks(path):
    blocks = re.split(r'(?=--- !u!)', path.read_text(encoding='utf-8-sig'))
    cameras = [b for b in blocks if b.startswith('--- !u!20 ')]
    game_objects = [re.search(r'm_GameObject: \{fileID: ([-\d]+)\}', b).group(1) for b in cameras]
    poses = [b for b in blocks if b.startswith('--- !u!4 ')
             and any(f'm_GameObject: {{fileID: {go}}}' in b for go in game_objects)]
    if not cameras or len(poses) != len(cameras):
        raise RuntimeError('Could not resolve every camera and its transform')
    return cameras + poses


def allowed(name):
    exact = {
        SCENE, SCENE + '.meta', 'Scenes/ReferenceHouse.meta', FASCIA,
        'Settings/ReferenceHouse_BakedLighting.lighting',
        'Settings/ReferenceHouse_Renderer.asset',
        'Settings/ReferenceHouse_Volume.asset',
        'Settings/ReferenceHouse_URP.asset',
        'Editor/ReferenceHouseContrastV10.cs',
    }
    settings_and_code_meta = {n + '.meta' for n in exact
                              if n.startswith(('Settings/', 'Editor/'))}
    return name in exact | settings_and_code_meta or bool(re.fullmatch(
        r'Scenes/ReferenceHouse/(?:LightingData\.asset|'
        r'Lightmap-\d+_comp_(?:light\.exr|dir\.png|shadowmask\.png)|'
        r'ReflectionProbe-\d+\.exr)(?:\.meta)?', name))


def main(dry=False):
    baseline_path = BACKUP / 'baseline.json'
    baseline = read_json(baseline_path)
    current, incoming = hashes(LIVE), hashes(STAGE)
    # Reject concurrent live edits before touching any output, including assets
    # which this pass is not going to copy. Update the live README after publishing.
    for name, sha in baseline.items():
        if current.get(name) != sha:
            raise RuntimeError('Concurrent live change: ' + name)

    immutable = ('Materials/', 'Textures/', 'Meshes/', 'Prefabs/')
    preserved = 0
    for name, sha in baseline.items():
        if name.startswith(immutable) and name != FASCIA:
            if incoming.get(name) != sha:
                raise RuntimeError('Protected surface/model asset changed: ' + name)
            preserved += 1
    for name in incoming:
        if name.startswith(immutable) and name not in baseline:
            raise RuntimeError('Unexpected new surface/model asset: ' + name)
    for name in baseline:
        if name not in incoming:
            raise RuntimeError('Staged asset missing: ' + name)

    original_scene = BACKUP / 'Assets' / SCENE
    proposed_scene = STAGE / SCENE
    original_overrides = transform_overrides(original_scene)
    if not original_overrides or original_overrides != transform_overrides(proposed_scene):
        raise RuntimeError('Model transforms changed or could not be verified')
    if camera_blocks(original_scene) != camera_blocks(proposed_scene):
        raise RuntimeError('Camera settings changed')

    verified = read_json(REPORTS / 'Unity_ContrastV10_Verified.json')
    reload = read_json(REPORTS / 'Unity_GPU_Reload.json')
    baked = read_json(REPORTS / 'Unity_GPU_House.json')
    bake_time = (REPORTS / 'Unity_GPU_House.json').stat().st_mtime
    if bake_time < baseline_path.stat().st_mtime:
        raise RuntimeError('Bake predates the V10 snapshot')
    for name in ('Unity_ContrastV10_Verified.json', 'Unity_GPU_Reload.json',
                 'Preview_Contrast_After_v10.png', 'Unity_V10_After.log'):
        if (REPORTS / name).stat().st_mtime < bake_time:
            raise RuntimeError('Stale lighting validation: ' + name)
    if (not verified['valid'] or reload['assignedRenderers'] < 400
            or baked['assignedRenderers'] < 400 or not baked['lightmaps']
            or baked['backend'] != 'ProgressiveGPU'
            or reload['backend'] != 'ProgressiveGPU' or 'RTX 4060' not in baked['gpu']):
        raise RuntimeError('Lighting validation incomplete')
    log = (REPORTS / 'Unity_V10_After.log').read_text(encoding='utf-8-sig', errors='replace')
    if 'REFERENCE_HOUSE_RENDER_COMPLETE' not in log or 'HOUSE_GPU_PERSISTENCE_VERIFIED' not in log:
        raise RuntimeError('Fresh reload/render incomplete')

    changed = []
    for name, sha in incoming.items():
        if current.get(name) == sha:
            continue
        if not allowed(name):
            if name in baseline:
                raise RuntimeError('Unexpected changed asset: ' + name)
            # The reusable staging project may contain unrelated utility scripts.
            continue
        if current.get(name) != baseline.get(name):
            raise RuntimeError('Concurrent live change: ' + name)
        changed.append(name)

    report = dict(files=sorted(changed), preservedSurfaceAndModelFiles=preserved,
                  changedMaterials=[FASCIA] if FASCIA in changed else [],
                  otherMaterialsUnchanged=True, texturesUnchanged=True,
                  cameraUnchanged=True, modelTransformsUnchanged=True,
                  lighting=verified, restoredReceivers=reload['assignedRenderers'])
    if not dry:
        for name in sorted(changed, key=lambda n: (2 if n.endswith('.unity') else 0 if n.endswith('.meta') else 1, n)):
            target = LIVE / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(STAGE / name, target)
        final = hashes(LIVE)
        if any(final.get(name) != incoming[name] for name in changed):
            raise RuntimeError('Published file hash mismatch')
        (REPORTS / 'Unity_ContrastV10_Publish.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main(dry='--check' in sys.argv)
