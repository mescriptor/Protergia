using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    // Patch an existing authored prefab by Blender object name. Preserve native
    // mesh GUIDs, prefab Transform file IDs and local pivots, including objects
    // whose positions have been overridden in the delivered Unity scene.
    public static class ReferenceHouseGeometryPatch
    {
        const string Root = "Assets/ReferenceHouse";
        const string PrefabPath = Root + "/Prefabs/ReferenceHouse.prefab";
        const string ScenePath = Root + "/Scenes/ReferenceHouse.unity";
        const string ReportDir = "C:/Protergia/Blender/ReferenceHouse";

        [Serializable] public class Change { public HouseMesh mesh; public bool geometry, materials; }
        [Serializable] public class Delta
        {
            public string exportDirectory;
            public Change[] changes;
            public string[] remove;
            public HouseMaterial[] newMaterials;
            public string[] polishedMaterials;
        }
        [Serializable] class SourceDelta { public int version; public RevisedMesh[] meshes; public HouseMaterial[] materials; public string[] removed; }
        [Serializable] class RevisedMesh : HouseMesh { }

        public static void ApplyAndBake()
        {
            try
            {
                if (!Application.isBatchMode) throw new InvalidOperationException("Use the isolated house import workspace.");
                string[] args = Environment.GetCommandLineArgs();
                int arg = Array.IndexOf(args, "-houseGeometryDelta");
                if (arg < 0 || arg + 1 >= args.Length) throw new ArgumentException("Missing -houseGeometryDelta manifest.");
                string file=Path.GetFullPath(args[arg+1]);
                var source=JsonUtility.FromJson<SourceDelta>(File.ReadAllText(file));
                if (source == null || source.version!=1 || source.meshes==null) throw new InvalidDataException("Invalid geometry patch.");
                var definitions=source.materials ?? Array.Empty<HouseMaterial>();
                string baselineDirectory=Path.Combine(ReportDir,"UnityExport");
                var baseline=JsonUtility.FromJson<HouseManifest>(File.ReadAllText(Path.Combine(baselineDirectory,"scene.json"))).meshes.ToDictionary(m=>m.name);
                string deltaDirectory=Path.GetDirectoryName(file);
                var updates=new List<Change>();
                foreach(var mesh in source.meshes)
                {
                    bool added=!baseline.TryGetValue(mesh.name,out var original);
                    bool geometry=added || (Vector(original.position)-Vector(mesh.position)).sqrMagnitude>1e-12f;
                    if(!geometry && !string.IsNullOrEmpty(mesh.file))
                    {
                        string oldFile=Path.Combine(baselineDirectory,original.file);
                        string newFile=Path.Combine(deltaDirectory,mesh.file);
                        geometry=!File.ReadAllBytes(oldFile).SequenceEqual(File.ReadAllBytes(newFile));
                    }
                    bool materials=added || !original.materials.SequenceEqual(mesh.materials);
                    if(geometry || materials)updates.Add(new Change{mesh=mesh,geometry=geometry,materials=materials});
                }
                var delta=new Delta{
                    exportDirectory=deltaDirectory, remove=source.removed,
                    changes=updates.ToArray(),
                    newMaterials=definitions.Where(m=>m.name.StartsWith("Ground polished travertine ") || !AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+ReferenceHouseImporter.Safe(m.name)+".mat")).ToArray(),
                    polishedMaterials=definitions.Where(m=>m.name.StartsWith("Ground polished travertine ")).Select(m=>m.name).ToArray()
                };
                Apply(delta);
                Scene scene = EditorSceneManager.OpenScene(ScenePath);
                PrepareFloorProbe();
                ReferenceHouseLightingBake.PrepareReceivers(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                WriteWorldBounds(scene,delta);
                ReferenceHouseLightingBake.BakeHouse();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static void Apply(Delta delta)
        {
            var previous = Identities();
            var changed = new HashSet<string>();
            var removed = new HashSet<string>(delta.remove ?? Array.Empty<string>());
            foreach (Change change in delta.changes)
            {
                if (change.mesh == null || !changed.Add(change.mesh.name) || removed.Contains(change.mesh.name))
                    throw new InvalidDataException("Duplicate or incompatible mesh operation.");
                Protect(change.mesh.collection,change.mesh.name);
            }
            var polished = new HashSet<string>(delta.polishedMaterials ?? Array.Empty<string>());
            foreach (HouseMaterial surface in delta.newMaterials ?? Array.Empty<HouseMaterial>())
                CreateMaterial(surface, polished.Contains(surface.name));

            GameObject house = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var existing = house.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.name, f => f.gameObject);
                foreach (string name in removed)
                {
                    if (!existing.TryGetValue(name, out GameObject obj)) continue;
                    Protect(obj.transform.parent.name,obj.name);
                    UnityEngine.Object.DestroyImmediate(obj);
                    existing.Remove(name);
                }
                foreach (Change change in delta.changes)
                {
                    HouseMesh entry = change.mesh;
                    bool added = !existing.TryGetValue(entry.name, out GameObject obj);
                    if (added)
                    {
                        if (!change.geometry || !change.materials) throw new InvalidDataException("Incomplete added mesh: " + entry.name);
                        Transform group = house.transform.Find(entry.collection);
                        if (!group)
                        {
                            group = new GameObject(entry.collection).transform;
                            group.SetParent(house.transform, false);
                        }
                        obj = new GameObject(entry.name);
                        obj.transform.SetParent(group, false);
                        obj.transform.localPosition = Vector(entry.position);
                        obj.AddComponent<MeshFilter>();
                        var renderer = obj.AddComponent<MeshRenderer>();
                        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                        GameObjectUtility.SetStaticEditorFlags(obj, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic);
                        existing.Add(entry.name, obj);
                    }
                    if (obj.transform.parent.name != entry.collection)
                        throw new InvalidDataException("Collection mismatch for " + entry.name);
                    if (change.geometry) SetGeometry(house, obj, entry, delta.exportDirectory);
                    if (change.materials)
                        obj.GetComponent<MeshRenderer>().sharedMaterials = entry.materials.Select(MaterialByName).ToArray();
                    var filter = obj.GetComponent<MeshFilter>();
                    if (filter.sharedMesh.subMeshCount != obj.GetComponent<MeshRenderer>().sharedMaterials.Length)
                        throw new InvalidDataException("Material slots differ from submesh count: " + entry.name);
                }
                PrefabUtility.SaveAsPrefabAsset(house, PrefabPath, out bool saved);
                if (!saved) throw new IOException("Could not save the patched house prefab.");
            }
            finally { PrefabUtility.UnloadPrefabContents(house); }
            AssetDatabase.SaveAssets();
            var current = Identities();
            foreach (var item in previous)
                if (!removed.Contains(item.Key) && (!current.TryGetValue(item.Key,out Identity now) || item.Value.transformId != now.transformId || item.Value.meshGuid != now.meshGuid))
                    throw new InvalidDataException("Prefab transform ID or mesh GUID changed for " + item.Key);
            var report = new PatchReport{changed=changed.OrderBy(n=>n).ToArray(), removed=removed.OrderBy(n=>n).ToArray(), meshesBefore=previous.Count, meshesAfter=current.Count, preservedExistingIds=previous.Keys.Count(n=>!removed.Contains(n)), meshPaths=changed.Select(n=>AssetDatabase.GUIDToAssetPath(current[n].meshGuid)).Distinct().OrderBy(n=>n).ToArray(), materialPaths=delta.newMaterials.Select(m=>Root+"/Materials/"+ReferenceHouseImporter.Safe(m.name)+".mat").ToArray()};
            File.WriteAllText(Path.Combine(ReportDir,"Unity_GeometryPatch.json"), JsonUtility.ToJson(report,true));
            Debug.Log("HOUSE_GEOMETRY_PATCH_COMPLETE " + JsonUtility.ToJson(report));
        }

        static void SetGeometry(GameObject house, GameObject obj, HouseMesh entry, string directory)
        {
            MeshFilter filter = obj.GetComponent<MeshFilter>();
            Mesh previous = filter.sharedMesh;
            Mesh incoming = ReferenceHouseImporter.ReadMesh(Path.Combine(directory, entry.file),entry.name);
            // Exported vertices are relative to the exported center in house
            // space. Restore world shape around the original prefab pivot so
            // authored transform overrides still produce their intended offset.
            Matrix4x4 toLocal = obj.transform.worldToLocalMatrix * house.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = toLocal.inverse.transpose;
            Vector3 center = Vector(entry.position);
            Vector3[] positions = incoming.vertices;
            Vector3[] normals = incoming.normals;
            for (int i=0; i<positions.Length; i++)
            {
                positions[i] = toLocal.MultiplyPoint3x4(positions[i] + center);
                normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            }
            incoming.vertices=positions;
            incoming.normals=normals;
            incoming.RecalculateBounds();
            incoming.RecalculateTangents();
            if (previous)
            {
                incoming.name=previous.name;
                EditorUtility.CopySerialized(incoming,previous);
                UnityEngine.Object.DestroyImmediate(incoming);
                EditorUtility.SetDirty(previous);
            }
            else
            {
                string assetPath=Root+"/Meshes/Geometry_"+ReferenceHouseImporter.Safe(entry.name)+"_"+Suffix(entry.name)+".asset";
                var prior=AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                if (prior)
                {
                    EditorUtility.CopySerialized(incoming,prior);
                    UnityEngine.Object.DestroyImmediate(incoming);
                    EditorUtility.SetDirty(prior);
                    filter.sharedMesh=prior;
                }
                else
                {
                    AssetDatabase.CreateAsset(incoming,assetPath);
                    filter.sharedMesh=incoming;
                }
            }
            var collider=obj.GetComponent<MeshCollider>();
            if (collider) { collider.sharedMesh=null; collider.sharedMesh=filter.sharedMesh; }
        }

        static Material MaterialByName(string name)
        {
            string path=Root+"/Materials/"+ReferenceHouseImporter.Safe(name)+".mat";
            return AssetDatabase.LoadAssetAtPath<Material>(path) ?? throw new InvalidDataException("Missing assigned material: "+name);
        }

        static void CreateMaterial(HouseMaterial source,bool polished)
        {
            string path=Root+"/Materials/"+ReferenceHouseImporter.Safe(source.name)+".mat";
            var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                string tone=source.name.Substring(source.name.Length-2);
                mat=polished ? new Material(MaterialByName("Travertine porcelain "+tone)) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.name=source.name;
                AssetDatabase.CreateAsset(mat,path);
            }
            if (!polished && source.baseColor != null && source.baseColor.Length >= 3)
                mat.SetColor("_BaseColor",new Color(source.baseColor[0],source.baseColor[1],source.baseColor[2],1).gamma);
            mat.SetFloat("_Smoothness", polished ? .76f : Mathf.Clamp01(1-source.roughness));
            mat.SetFloat("_Metallic",polished ? 0 : Mathf.Clamp01(source.metallic));
            mat.SetFloat("_EnvironmentReflections",1);
            mat.SetFloat("_SpecularHighlights",1);
            mat.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (polished) mat.SetFloat("_BumpScale",.09f);
            if (!polished && source.textureScale != null && source.textureScale.Length>=2)
                mat.SetTextureScale("_BaseMap",new Vector2(source.textureScale[0],source.textureScale[1]));
            mat.enableInstancing=true;
            EditorUtility.SetDirty(mat);
        }

        static void Protect(string collection,string objectName)
        {
            string[] untouched={"14_","15_","17_","18_"};
            bool furniture=collection.StartsWith("19_") && !objectName.StartsWith("Basement storage detail / ");
            if (furniture || untouched.Any(collection.StartsWith))
                throw new InvalidDataException("This furniture, vehicle or planting collection is protected: "+collection);
        }

        const string GroundProbeName="Reflection probe | living room and kitchen";
        static void PrepareFloorProbe()
        {
            GameObject probeObject=GameObject.Find(GroundProbeName);
            if(!probeObject)probeObject=new GameObject(GroundProbeName);
            var probe=probeObject.GetComponent<ReflectionProbe>();
            if(!probe)probe=probeObject.AddComponent<ReflectionProbe>();
            probe.transform.position=new Vector3(0,1.85f,1.25f);
            probe.mode=ReflectionProbeMode.Baked;
            probe.size=new Vector3(8.7f,3.2f,4.6f);
            probe.center=new Vector3(0,.2f,0);
            probe.boxProjection=true;
            probe.resolution=256;
            probe.importance=2;
            probe.blendDistance=.3f;
            probe.intensity=.85f;
            probe.nearClipPlane=.08f;
            probe.farClipPlane=65;
            probe.clearFlags=ReflectionProbeClearFlags.Skybox;
            EditorUtility.SetDirty(probe);
        }

        static Dictionary<string,Identity> Identities()
        {
            var result=new Dictionary<string,Identity>();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(filter.transform,out string _,out long id) || id==0)
                    throw new InvalidDataException("Missing prefab Transform file ID: "+filter.name);
                var meshPath=AssetDatabase.GetAssetPath(filter.sharedMesh);
                result.Add(filter.name,new Identity{transformId=id,meshGuid=AssetDatabase.AssetPathToGUID(meshPath)});
            }
            return result;
        }

        static string Suffix(string name)
        {
            using(var algorithm=SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(name))).Replace("-","").Substring(0,8);
        }
        static Vector3 Vector(float[] values) => new Vector3(values[0],values[1],values[2]);
        public static void VerifyPatchedHouse()
        {
            try
            {
                Scene scene=EditorSceneManager.OpenScene(ScenePath);
                var house=scene.GetRootGameObjects().First(o=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(o)==PrefabPath);
                var filters=house.GetComponentsInChildren<MeshFilter>();
                var renderers=house.GetComponentsInChildren<MeshRenderer>();
                var polishedUsers=renderers.Where(r=>r.sharedMaterials.Any(m=>m && AssetDatabase.GetAssetPath(m).StartsWith(Root+"/Materials/Ground_polished_travertine_"))).ToArray();
                if(polishedUsers.Length!=5 || polishedUsers.Any(r=>r.transform.parent.name!="02_GROUND_FLOOR" || !r.name.StartsWith("Ground porcelain floor / tone ")))
                    throw new Exception("Polished finish is not confined to the five intended ground-floor meshes.");
                if(polishedUsers.Any(r=>r.reflectionProbeUsage==ReflectionProbeUsage.Off || r.sharedMaterials.Any(m=>m.GetFloat("_EnvironmentReflections")==0 || Mathf.Abs(m.GetFloat("_Smoothness")-.76f)>.001f)))
                    throw new Exception("The polished floor did not retain the intended reflections and smoothness.");
                var groundProbe=GameObject.Find(GroundProbeName).GetComponent<ReflectionProbe>();
                if(!groundProbe.boxProjection || !groundProbe.bakedTexture)
                    throw new Exception("The ground-floor baked reflection probe did not reload.");
                VerifyDrivewaySupport(filters);
                var pvWalls=renderers.Where(r=>r.transform.parent.name=="08_PHOTOVOLTAIC_PANELS" && r.name.StartsWith("PV / plaster ",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(pvWalls.Length==0 || pvWalls.Any(r=>r.lightmapIndex<0 || r.lightmapIndex>=LightmapSettings.lightmaps.Length))
                    throw new Exception("PV masonry did not retain valid lighting assignments.");
                var materials=renderers.SelectMany(r=>r.sharedMaterials).Distinct().ToArray();
                if(materials.Any(m=>m==null || m.shader==null || !m.shader.isSupported))
                    throw new Exception("Missing or unsupported assigned house material.");
                var report=new SceneReport{unityVersion=Application.unityVersion,scene=scene.path,prefab=PrefabPath,camera=Camera.main.name,meshCount=filters.Length,materialCount=materials.Length,triangleCount=filters.Sum(f=>f.sharedMesh.triangles.Length/3),lights=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Length,missingMaterials=0,errors=Array.Empty<string>()};
                File.WriteAllText(Path.Combine(ReportDir,"Unity_ImportReport.json"),JsonUtility.ToJson(report,true));
                Debug.Log($"HOUSE_PATCH_VERIFIED meshes={filters.Length} floorMeshes={polishedUsers.Length} photovoltaicMasonry={pvWalls.Length} floorProbe={AssetDatabase.GetAssetPath(groundProbe.bakedTexture)}");
                ReferenceHouseLightingBake.VerifySavedHouse();
            }
            catch(Exception error){Debug.LogException(error);if(Application.isBatchMode)EditorApplication.Exit(1);}
        }
        static void VerifyDrivewaySupport(MeshFilter[] filters)
        {
            var foundation=filters.Single(f=>f.name=="Side driveway foundation");
            var vertices=foundation.sharedMesh.vertices.Select(p=>foundation.transform.TransformPoint(p)).ToArray();
            int[] indices=foundation.sharedMesh.triangles;
            var measurements=new List<SupportPoint>();
            foreach(float x in new[]{4.505f,4.51f,4.525f,4.54f,4.545f,8.05f})
                foreach(float z in new[]{-.5f,.25f,.62f,1.25f,1.8f,3.25f,5.5f})
                {
                    float highest=float.NegativeInfinity;
                    for(int i=0;i<indices.Length;i+=3)
                    {
                        Vector3 a=vertices[indices[i]],b=vertices[indices[i+1]],c=vertices[indices[i+2]];
                        float denom=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);
                        if(Mathf.Abs(denom)<1e-8f)continue;
                        float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/denom;
                        float v=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/denom;
                        float w=1-u-v;
                        if(u>=-1e-5f && v>=-1e-5f && w>=-1e-5f)highest=Mathf.Max(highest,u*a.y+v*b.y+w*c.y);
                    }
                    if(float.IsInfinity(highest)||float.IsNaN(highest)||Mathf.Abs(highest-.4f)>.012f)
                        throw new Exception($"No driveway support at x={x:F3}, z={z:F3}: top={highest}");
                    measurements.Add(new SupportPoint{x=x,z=z,top=highest});
                }
            File.WriteAllText(Path.Combine(ReportDir,"Unity_DrivewaySupport.json"),JsonUtility.ToJson(new SupportReport{samples=measurements.ToArray()},true));
        }
        public static void CaptureRightJunctions()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                var qa=UnityEngine.Object.Instantiate(Camera.main.gameObject);
                qa.name="Temporary review camera";
                qa.tag="Untagged";
                var listener=qa.GetComponent<AudioListener>();
                if(listener)UnityEngine.Object.DestroyImmediate(listener);
                var camera=qa.GetComponent<Camera>();
                camera.enabled=false;
                camera.usePhysicalProperties=false;
                camera.lensShift=Vector2.zero;
                camera.fieldOfView=34;
                camera.nearClipPlane=.45f;
                camera.farClipPlane=90;
                qa.transform.position=new Vector3(13.2f,3f,-4.2f);
                qa.transform.LookAt(new Vector3(5.85f,1.75f,1.5f));
                ReferenceHouseImporter.CaptureTemporary(camera,Path.Combine(ReportDir,"Preview_Unity_Right_Junctions_v7.png"));
            }
            catch(Exception error){Debug.LogException(error);if(Application.isBatchMode)EditorApplication.Exit(1);}
        }
        static void WriteWorldBounds(Scene scene,Delta delta)
        {
            var names=new HashSet<string>(delta.changes.Select(c=>c.mesh.name));
            var boxes=new List<Box>();
            foreach(var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if(r.gameObject.scene!=scene || !names.Contains(r.name))continue;
                Bounds b=r.bounds;
                boxes.Add(new Box{name=r.name,min=new[]{b.min.x,b.min.y,b.min.z},max=new[]{b.max.x,b.max.y,b.max.z},materialPaths=r.sharedMaterials.Select(AssetDatabase.GetAssetPath).ToArray()});
            }
            File.WriteAllText(Path.Combine(ReportDir,"Unity_GeometryBounds.json"),JsonUtility.ToJson(new Boxes{objects=boxes.ToArray()},true));
        }
        class Identity { public long transformId; public string meshGuid; }
        [Serializable] class PatchReport { public int meshesBefore, meshesAfter, preservedExistingIds; public string[] changed,removed,meshPaths,materialPaths; }
        [Serializable] class Box { public string name; public float[] min,max; public string[] materialPaths; }
        [Serializable] class Boxes { public Box[] objects; }
        [Serializable] class SceneReport { public string unityVersion,scene,prefab,camera; public int meshCount,materialCount,triangleCount,lights,missingMaterials; public string[] errors; }
        [Serializable] class SupportPoint { public float x,z,top; }
        [Serializable] class SupportReport { public SupportPoint[] samples; }
    }
}
