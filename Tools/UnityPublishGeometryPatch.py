"""Publish the checked geometry delta plus its RTX lightmaps without rebuilding the project."""

from pathlib import Path
import json
import os
import re
import shutil
import sys
import tempfile

from UnityPublishGpuBake import asset_hashes, check_protected, digest, live, project, protected, reports, staged, transform_overrides


backup = reports / "BeforeGeometryV7"
baseline_file = backup / "original-asset-hashes.json"


def snapshot():
    backup.mkdir(exist_ok=True)
    if not baseline_file.exists():
        baseline_file.write_text(json.dumps(asset_hashes(live), indent=1))
        for source, destination in ((live / "Scenes/ReferenceHouse.unity", backup / "ReferenceHouse.unity"),
                                    (live / "Prefabs/ReferenceHouse.prefab", backup / "ReferenceHouse.prefab"),
                                    (reports / "Preview_Unity.png", backup / "Preview_Unity.png")):
            shutil.copy2(source, destination)
    check_protected()
    print("Current-scene hashes and rollback preview recorded")


def prefab_identities(path):
    objects, transforms, mesh_guids = {}, {}, {}
    for block in re.split(r"(?=^--- !u!)", path.read_text(encoding="utf-8"), flags=re.M):
        record=re.match(r"^--- !u!(1|4|33) &([-\d]+)",block)
        if not record:
            continue
        kind,local_id=record.groups()
        if kind=="1":
            objects[local_id]=re.search(r"^  m_Name: (.+)$",block,re.M)[1]
        else:
            go=re.search(r"m_GameObject: \{fileID: ([-\d]+)\}",block)[1]
            if kind=="4":
                transforms[go]=local_id
            else:
                mesh_guids[go]=re.search(r"m_Mesh: \{fileID: [-\d]+, guid: ([a-f0-9]+)",block)[1]
    return {objects[go]:(transforms[go],guid) for go,guid in mesh_guids.items()}


def publish():
    geom = json.loads((reports / "Unity_GeometryPatch.json").read_text())
    bake = json.loads((reports / "Unity_GPU_House.json").read_text())
    reload = json.loads((reports / "Unity_GPU_Reload.json").read_text())
    if bake["backend"] != "ProgressiveGPU" or "RTX 4060" not in bake["gpu"]:
        raise RuntimeError("Expected an RTX GPU bake")
    if not all(m["litSamples"] for m in reload["atlases"]) or reload["assignedRenderers"]<100:
        raise RuntimeError("Saved lightmaps have not been verified")
    scene_path="Scenes/ReferenceHouse.unity"
    authored=transform_overrides(backup / "ReferenceHouse.unity")
    incoming_overrides=transform_overrides(staged / scene_path)
    if any(k not in incoming_overrides or abs(v-incoming_overrides[k])>1e-6 for k,v in authored.items()):
        raise RuntimeError("An authored transform override would be lost")
    initial_ids=prefab_identities(backup/"ReferenceHouse.prefab")
    incoming_ids=prefab_identities(staged/"Prefabs/ReferenceHouse.prefab")
    remove=set(geom["removed"])
    preserved=[n for n in initial_ids if n not in remove]
    if any(incoming_ids.get(n)!=initial_ids[n] for n in preserved):
        raise RuntimeError("An original model Transform ID or Mesh GUID would change")
    check_protected()
    old=json.loads(baseline_file.read_text())
    current=asset_hashes(live)
    incoming=asset_hashes(staged)
    allowed_assets={Path(p).relative_to("Assets/ReferenceHouse").as_posix() for p in geom["meshPaths"]+geom["materialPaths"]}
    allowed_assets.update((scene_path,"Prefabs/ReferenceHouse.prefab","Editor/ReferenceHouseImporter.cs",
                           "Editor/ReferenceHouseLightingBake.cs","Editor/ReferenceHouseGeometryPatch.cs"))
    allowed_assets.update(key+".meta" for key in tuple(allowed_assets))
    pending=[]
    unexpected=[]
    for key, fingerprint in incoming.items():
        if current.get(key)==fingerprint or key=="README.txt":
            continue
        if key not in allowed_assets and not key.startswith("Scenes/ReferenceHouse/"):
            unexpected.append(key)
            continue
        if current.get(key)!=old.get(key):
            raise RuntimeError(f"File changed in the original project during this update: {key}")
        if key in protected:
            raise RuntimeError(f"Protected geometry or material would be overwritten: {key}")
        pending.append(key)
    if unexpected:
        raise RuntimeError("Unreviewed changes outside the requested patch: "+str(unexpected[:25]))

    # The original scene is replaced only after all referenced resources and
    # metadata exist. Keep the previous contents of every overwritten resource.
    scratch=project / "Tools/UnityBuild/Temp/GeometryPublish"
    scratch.mkdir(parents=True,exist_ok=True)
    for key in sorted(pending,key=lambda p:(3 if p==scene_path else (2 if p=="Prefabs/ReferenceHouse.prefab" else (0 if p.endswith(".meta") else 1)),p)):
        dest=live/key
        if dest.exists():
            saved=backup/"overwritten"/key
            if not saved.exists():
                saved.parent.mkdir(parents=True,exist_ok=True)
                shutil.copy2(dest,saved)
        dest.parent.mkdir(parents=True,exist_ok=True)
        with tempfile.NamedTemporaryFile(dir=scratch,delete=False) as tmp:
            temp_path=Path(tmp.name)
        try:
            shutil.copyfile(staged/key,temp_path)
            os.replace(temp_path,dest)
        finally:
            temp_path.unlink(missing_ok=True)
    for key in pending:
        if digest(live/key)!=incoming[key]:
            raise RuntimeError(f"Published asset hash differs: {key}")
    for key in (scene_path,"Prefabs/ReferenceHouse.prefab","Settings/ReferenceHouse_BakedLighting.lighting","Scenes/ReferenceHouse/LightingData.asset"):
        if digest(live/key)!=digest(staged/key):
            raise RuntimeError(f"Published scene dependency differs: {key}")
    for info in reload["atlases"]:
        for channel in ("color","direction"):
            key=Path(info[channel]).relative_to("Assets/ReferenceHouse").as_posix()
            for name in (key,key+".meta"):
                if digest(live/name)!=digest(staged/name):
                    raise RuntimeError(f"Lightmap texture or GUID differs: {name}")
    check_protected()
    result={"scene":str(live/scene_path),"gpu":bake["gpu"],"lightmaps":len(reload["atlases"]),
            "lightmappedRenderers":reload["assignedRenderers"],"probeCount":reload["probeCount"],
            "protectedFiles":len(protected),"authoredTransformOverrides":len(authored),"copiedFiles":len(pending),
            "meshCount":geom["meshesAfter"],"preservedPrefabIdentities":len(preserved),"files":pending}
    (reports/"Unity_GeometryPublish.json").write_text(json.dumps(result,indent=2))
    print(json.dumps({k:v for k,v in result.items() if k!="files"}))


if __name__=="__main__":
    snapshot() if sys.argv[1:]==["--record-baseline"] else publish()
