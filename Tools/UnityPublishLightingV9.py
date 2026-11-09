"""Publish verified lighting only, retaining the original surface and model assets."""
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
BACKUP = REPORTS / 'BeforeRealismV9'

def hashes(root):
    return {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in root.rglob('*') if p.is_file()}

def transform_overrides(path):
    pattern = re.compile(r'- target: \{fileID: ([-\d]+), guid: ([a-f0-9]+), type: \d+\}\s+'
                         r'propertyPath: (m_Local(?:Position|Rotation|Scale)\.[xyzw])\s+value: ([^\n]+)')
    return {(target, guid, prop): float(value) for target, guid, prop, value in pattern.findall(path.read_text())}

def camera_blocks(path):
    blocks = re.split(r'(?=--- !u!)', path.read_text())
    cameras = [b for b in blocks if b.startswith('--- !u!20 ')]
    game_objects = [re.search(r'm_GameObject: \{fileID: ([-\d]+)\}', b).group(1) for b in cameras]
    poses = [b for b in blocks if b.startswith('--- !u!4 ')
             and any(f'm_GameObject: {{fileID: {go}}}' in b for go in game_objects)]
    if len(poses) != len(cameras):
        raise RuntimeError('Could not resolve every camera transform')
    return cameras + poses

def allowed(name):
    return (name.startswith('Scenes/ReferenceHouse/')
            or name in ('Scenes/ReferenceHouse.unity', 'Scenes/ReferenceHouse.meta')
            or name.startswith(('Settings/ReferenceHouse_BakedLighting.lighting',
                'Settings/ReferenceHouse_Renderer.asset', 'Settings/ReferenceHouse_Volume.asset',
                'Settings/ReferenceHouse_URP.asset', 'Editor/ReferenceHouseLightingV9.cs',
                'Editor/ReferenceHouseLightingReview.cs')))

def main(dry=False):
    baseline = json.loads((BACKUP / 'baseline.json').read_text())
    current, incoming = hashes(LIVE), hashes(STAGE)
    immutable = ('Materials/', 'Textures/', 'Meshes/', 'Prefabs/')
    preserved = 0
    for name, sha in baseline.items():
        if name.startswith(immutable):
            if current.get(name) != sha or incoming.get(name) != sha:
                raise RuntimeError('Surface/model asset changed: ' + name)
            preserved += 1
    original_scene = BACKUP / 'Assets/Scenes/ReferenceHouse.unity'
    proposed_scene = STAGE / 'Scenes/ReferenceHouse.unity'
    original_overrides, proposed_overrides = transform_overrides(original_scene), transform_overrides(proposed_scene)
    if original_overrides != proposed_overrides:
        raise RuntimeError('Model transforms changed')
    if camera_blocks(original_scene) != camera_blocks(proposed_scene):
        raise RuntimeError('Camera settings changed')
    verified = json.loads((REPORTS / 'Unity_LightingV9_Verified.json').read_text())
    reload = json.loads((REPORTS / 'Unity_GPU_Reload.json').read_text())
    baked = json.loads((REPORTS / 'Unity_GPU_House.json').read_text())
    bake_time = (REPORTS / 'Unity_GPU_House.json').stat().st_mtime
    for name in ('Unity_LightingV9_Verified.json', 'Unity_GPU_Reload.json', 'Preview_Lighting_After_v9.png'):
        if (REPORTS / name).stat().st_mtime < bake_time:
            raise RuntimeError('Stale lighting validation: ' + name)
    if not verified['valid'] or reload['assignedRenderers'] < 400 or 'RTX 4060' not in baked['gpu']:
        raise RuntimeError('Lighting validation incomplete')
    log = (REPORTS / 'Unity_V9_After.log').read_text(errors='replace')
    if 'REFERENCE_HOUSE_RENDER_COMPLETE' not in log or 'HOUSE_GPU_PERSISTENCE_VERIFIED' not in log:
        raise RuntimeError('Fresh reload/render incomplete')
    changed = []
    for name, sha in incoming.items():
        if current.get(name) == sha:
            continue
        if not allowed(name):
            if name in baseline:
                raise RuntimeError('Unexpected changed asset: ' + name)
            continue
        if current.get(name) != baseline.get(name):
            raise RuntimeError('Concurrent live change: ' + name)
        changed.append(name)
    report = dict(files=changed, preservedSurfaceAndModelFiles=preserved,
                  materialsUnchanged=True, texturesUnchanged=True,
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
        (REPORTS / 'Unity_LightingV9_Publish.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report, indent=2))

if __name__ == '__main__':
    main(dry='--check' in sys.argv)
