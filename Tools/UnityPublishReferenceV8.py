"""Stage and publish the reviewed V8 reference-look revision, preserving identities."""
from pathlib import Path
import hashlib
import json
import shutil
import sys
from UnityPublishGpuBake import transform_overrides

ROOT = Path(__file__).resolve().parents[1]
LIVE = ROOT / 'Assets/ReferenceHouse'
STAGE = ROOT / 'Tools/UnityBuild/Assets/ReferenceHouse'
REPORTS = ROOT / 'Blender/ReferenceHouse'
BACKUP = REPORTS / 'BeforeReferenceV8'

def hashes(root):
    return {p.relative_to(root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in root.rglob('*') if p.is_file()}

def stage():
    if not (BACKUP / 'baseline.json').exists():
        BACKUP.mkdir(parents=True, exist_ok=True)
        shutil.copytree(LIVE, BACKUP / 'Assets', dirs_exist_ok=True)
        for name in ['Preview_Unity.png', 'Unity_GPU_House.json', 'Unity_GPU_Reload.json']:
            shutil.copy2(REPORTS / name, BACKUP / name)
        (BACKUP / 'baseline.json').write_text(json.dumps(hashes(LIVE), indent=1))
    current = hashes(STAGE)
    count = 0
    for name, sha in hashes(LIVE).items():
        if current.get(name) != sha:
            destination = STAGE / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(LIVE / name, destination)
            count += 1
    print(f'Stage synchronized: {count} files; original assets backed up in {BACKUP}')

def publish(dry=False):
    baseline = json.loads((BACKUP / 'baseline.json').read_text())
    current, incoming = hashes(LIVE), hashes(STAGE)
    report = json.loads((REPORTS / 'Unity_ReferenceV8_Verified.json').read_text())
    if not report['valid']:
        raise RuntimeError('Missing V8 validation')
    proposed = transform_overrides(STAGE / 'Scenes/ReferenceHouse.unity')
    for key, value in transform_overrides(BACKUP / 'Assets/Scenes/ReferenceHouse.unity').items():
        if key not in proposed or abs(proposed[key] - value) > 1e-6:
            raise RuntimeError(f'Authored transform would change: {key}')
    protected = json.loads((REPORTS / 'BeforeGpuBake/protected-assets.json').read_text(encoding='utf-8-sig'))
    for name, sha in protected.items():
        if current.get(name, '').lower() != sha.lower() or incoming.get(name, '').lower() != sha.lower():
            raise RuntimeError(f'Protected asset changed: {name}')
    changed = []
    for name, sha in incoming.items():
        if current.get(name) == sha:
            continue
        # New authored helpers/assets are already in the live source; all other
        # existing files must still match the pre-revision baseline.
        if current.get(name) != baseline.get(name):
            raise RuntimeError(f'Concurrent live modification: {name}')
        changed.append(name)
    if dry:
        print(json.dumps({'pendingFiles': len(changed), 'protectedFiles': len(protected), 'files': changed}, indent=2))
        return
    for name in sorted(changed, key=lambda p: (2 if p.endswith('.unity') else 0 if p.endswith('.meta') else 1, p)):
        target = LIVE / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(STAGE / name, target)
    final = hashes(LIVE)
    if any(final.get(name) != incoming[name] for name in changed):
        raise RuntimeError('Publish hash mismatch')
    (REPORTS / 'Unity_ReferenceV8_Publish.json').write_text(json.dumps({'files': changed, 'verified': report, 'protectedFiles': len(protected)}, indent=2))
    print(f'Published {len(changed)} verified files; {len(protected)} protected assets preserved.')

if __name__ == '__main__':
    stage() if sys.argv[1:] == ['--stage'] else publish(dry=sys.argv[1:] == ['--check'])
