"""Copy verified lightmaps and their scene dependencies into the live project."""

from pathlib import Path
import hashlib
import json
import os
import re
import shutil
import tempfile


project = Path(__file__).resolve().parents[1]
live = project / "Assets" / "ReferenceHouse"
staged = project / "Tools" / "UnityBuild" / "Assets" / "ReferenceHouse"
reports = project / "Blender" / "ReferenceHouse"
baseline_path = reports / "BeforeGpuBake" / "original-asset-hashes.json"
protected = json.loads((reports / "BeforeGpuBake" / "protected-assets.json").read_text(encoding="utf-8-sig"))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def asset_hashes(root):
    return {p.relative_to(root).as_posix(): digest(p) for p in root.rglob("*") if p.is_file()}


def check_protected():
    for key, expected in protected.items():
        for location in (live, staged):
            if digest(location / key) != expected.upper():
                raise RuntimeError(f"A protected vehicle/plant asset changed: {location / key}")


def transform_overrides(path):
    pattern = re.compile(r"- target: \{fileID: ([-\d]+), guid: ([a-f0-9]+), type: \d+\}\s+"
                         r"propertyPath: (m_Local(?:Position|Rotation|Scale)\.[xyzw])\s+value: ([^\n]+)")
    return {(target, guid, prop): float(value) for target, guid, prop, value in pattern.findall(path.read_text())}


def intended(relative):
    if relative.startswith(("Meshes/", "Materials/")):
        return True
    if relative.startswith("Scenes/ReferenceHouse/") or relative in ("Scenes/ReferenceHouse.meta", "Scenes/ReferenceHouse.unity"):
        return True
    if relative.startswith(("Settings/ReferenceHouse_BakedLighting.lighting", "Settings/ReferenceHouse_Volume.asset", "Settings/ReferenceBackdrop.shader")):
        return True
    return relative.startswith(("Editor/ReferenceHouseDaylight.cs", "Editor/ReferenceHouseMaterialLook.cs", "Editor/ReferenceHouseLightingBake.cs"))


def publish():
    saved = json.loads((reports / "Unity_GPU_Reload.json").read_text())
    baked = json.loads((reports / "Unity_GPU_House.json").read_text())
    if saved["backend"] != "ProgressiveGPU" or "RTX 4060" not in baked["gpu"]:
        raise RuntimeError("Missing confirmed NVIDIA bake.")
    if saved["assignedRenderers"] < 100 or not all(a["litSamples"] > 0 for a in saved["atlases"]):
        raise RuntimeError("Missing verified baked renderer/lightmap assignments.")
    check_protected()
    baseline = json.loads(baseline_path.read_text())
    current = asset_hashes(live)
    incoming = asset_hashes(staged)
    scene = "Scenes/ReferenceHouse.unity"
    authored = transform_overrides(live / scene)
    staged_overrides = transform_overrides(staged / scene)
    for key, value in authored.items():
        if key not in staged_overrides or abs(staged_overrides[key] - value) > 1e-6:
            raise RuntimeError(f"Authored transform was not preserved: {key}")

    pending = []
    for key, value in incoming.items():
        if current.get(key) == value or not intended(key):
            continue
        if current.get(key) != baseline.get(key):
            raise RuntimeError(f"An authored file changed during the bake: {key}")
        pending.append(key)

    # Publish metadata first and the scene last, so every referenced native
    # asset GUID is available before the refreshed scene can be opened.
    pending.sort(key=lambda key: (2 if key == scene else (0 if key.endswith(".meta") else 1), key))
    scratch = project / "Tools" / "UnityBuild" / "Temp" / "HousePublish"
    scratch.mkdir(parents=True, exist_ok=True)
    for key in pending:
        destination = live / key
        destination.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(dir=scratch, delete=False) as tmp:
            temp_path = Path(tmp.name)
        try:
            shutil.copyfile(staged / key, temp_path)
            os.replace(temp_path, destination)
        finally:
            temp_path.unlink(missing_ok=True)

    for key in pending:
        if digest(live / key) != incoming[key]:
            raise RuntimeError(f"Published dependency differs: {key}")
    for atlas in saved["atlases"]:
        for channel in ("color", "direction"):
            key = Path(atlas[channel]).relative_to("Assets/ReferenceHouse").as_posix()
            for exported in (key, key + ".meta"):
                if digest(live / exported) != digest(staged / exported):
                    raise RuntimeError(f"Lightmap asset or GUID differs: {exported}")
    check_protected()
    result = {"scene": str(live / scene), "gpu": baked["gpu"], "atlases": len(saved["atlases"]),
              "lightmappedRenderers": saved["assignedRenderers"], "probes": saved["probeCount"],
              "authoredTransformPropertiesVerified": len(authored), "protectedAssetsVerified": len(protected),
              "verifiedFilesCopied": len(pending), "files": pending}
    (reports / "Unity_GPU_Publish.json").write_text(json.dumps(result, indent=2))
    print(json.dumps({k:v for k,v in result.items() if k != "files"}))


if __name__ == "__main__":
    import sys
    if sys.argv[1:] == ["--record-baseline"]:
        if not baseline_path.exists():
            baseline_path.write_text(json.dumps(asset_hashes(live), indent=1))
        print("Baseline ready")
    else:
        publish()
