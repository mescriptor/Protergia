using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    public static class ReferenceHouseTrees
    {
        const string Root = "Assets/ReferenceHouse/Vegetation";
        const string ScenePath = "Assets/ReferenceHouse/Scenes/ReferenceHouse.unity";
        const string Output = "Blender/MediterraneanTrees";
        const string GroupName = "Trees | ready to place";
        [Serializable] class Palette { public Entry[] materials; }
        [Serializable] class Entry { public string name; public float[] linearColor; }
        [Serializable] class TreeInfo { public string name; public Vector3 position, size; public int lodCount, trianglesLOD0; }
        [Serializable] class Report { public string scene; public bool valid; public TreeInfo[] trees; public int lightmapsBefore, lightmapsAfter; }
        static Camera previewCamera;
        static int frames;

        [MenuItem("Reference House/Add Blender olive and cypress samples")]
        public static void Install()
        {
            try
            {
                for (int i=0;i<SceneManager.sceneCount;i++)
                    if (SceneManager.GetSceneAt(i).isDirty)
                        throw new InvalidOperationException("Save the current scene before adding the trees.");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var palette=JsonUtility.FromJson<Palette>(File.ReadAllText(Root+"/tree_materials.json"));
                var materials=new Dictionary<string,Material>();
                foreach(var entry in palette.materials)
                {
                    string path=Root+"/Materials/"+entry.name+".mat";
                    var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(!mat) {mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
                    mat.name=entry.name;
                    var c=entry.linearColor;
                    mat.SetColor("_BaseColor",new Color(c[0],c[1],c[2],1).gamma);
                    mat.SetFloat("_Smoothness",.15f);
                    mat.SetFloat("_Metallic",0);
                    // Each leaf has a green front and explicit silver back geometry.
                    mat.SetFloat("_Cull",(float)CullMode.Back);
                    EditorUtility.SetDirty(mat);materials.Add(entry.name,mat);
                }
                var prefabs=new List<GameObject>();
                foreach(string name in new[]{"Olive","ItalianCypress"})
                {
                    string modelPath=Root+"/Models/"+name+".fbx";
                    var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);
                    importer.importCameras=false;importer.importLights=false;importer.importAnimation=false;
                    importer.importNormals=ModelImporterNormals.Import;
                    importer.meshCompression=ModelImporterMeshCompression.Off;
                    importer.isReadable=false;importer.globalScale=1;importer.useFileScale=true;
                    importer.SaveAndReimport();
                    var model=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                    var tree=new GameObject(name);
                    var geometry=(GameObject)PrefabUtility.InstantiatePrefab(model);
                    geometry.transform.SetParent(tree.transform,false);
                    foreach(var r in geometry.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        r.sharedMaterials=r.sharedMaterials.Select(m=>materials[m.name]).ToArray();
                        r.shadowCastingMode=ShadowCastingMode.On;r.receiveShadows=true;
                        r.lightProbeUsage=LightProbeUsage.BlendProbes;
                        r.reflectionProbeUsage=ReflectionProbeUsage.BlendProbes;
                        GameObjectUtility.SetStaticEditorFlags(r.gameObject,0);
                    }
                    var lod=tree.AddComponent<LODGroup>();
                    var lods=new LOD[3];
                    for(int i=0;i<3;i++)
                    {
                        var renderers=geometry.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.EndsWith("_LOD"+i)).ToArray();
                        if(renderers.Length!=1)throw new Exception(name+" missing LOD "+i);
                        lods[i]=new LOD(new[]{.35f,.14f,.025f}[i],renderers);
                    }
                    lod.SetLODs(lods);lod.RecalculateBounds();
                    var b=lods[0].renderers[0].bounds;
                    if(b.size.y<3 || b.size.y>7 || b.min.y<-.02 || b.min.y>.02)
                        throw new Exception("Unexpected metre scale or base pivot: "+name+" "+b);
                    var prefab=PrefabUtility.SaveAsPrefabAsset(tree,Root+"/Prefabs/"+name+".prefab");
                    prefabs.Add(prefab);UnityEngine.Object.DestroyImmediate(tree);
                }
                var scene=EditorSceneManager.OpenScene(ScenePath);
                int mapsBefore=LightmapSettings.lightmaps.Length;
                var group=GameObject.Find(GroupName);
                if(!group)group=new GameObject(GroupName);
                var info=new List<TreeInfo>();
                for(int i=0;i<prefabs.Count;i++)
                {
                    var found=group.transform.Find(prefabs[i].name);
                    var tree=found?found.gameObject:(GameObject)PrefabUtility.InstantiatePrefab(prefabs[i],scene);
                    if(!found)
                    {
                        tree.transform.SetParent(group.transform,false);
                        tree.transform.position=new Vector3(i==0?-13:-8,-1.70f,-4);
                    }
                    var lod=tree.GetComponent<LODGroup>();
                    var r=lod.GetLODs()[0].renderers[0];
                    info.Add(new TreeInfo{name=tree.name,position=tree.transform.position,size=r.bounds.size,
                        lodCount=lod.lodCount,trianglesLOD0=r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3});
                }
                EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
                // Validate the saved scene, including retained lighting and prefab links.
                scene=EditorSceneManager.OpenScene(ScenePath);
                group=GameObject.Find(GroupName);
                if(!group || group.transform.childCount!=2)throw new Exception("Saved tree samples missing.");
                foreach(Transform tree in group.transform)
                {
                    if(!PrefabUtility.IsPartOfPrefabInstance(tree.gameObject))throw new Exception("Missing prefab connection.");
                    foreach(var r in tree.GetComponentsInChildren<Renderer>())
                        if(r.sharedMaterials.Any(m=>!m || !m.shader || m.shader.name.Contains("Error")))
                            throw new Exception("Missing or invalid foliage material.");
                }
                if(LightmapSettings.lightmaps.Length!=mapsBefore)throw new Exception("Lightmap count changed.");
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output+"/Unity_Trees_Verified.json",JsonUtility.ToJson(new Report{
                    scene=ScenePath,valid=true,trees=info.ToArray(),lightmapsBefore=mapsBefore,lightmapsAfter=LightmapSettings.lightmaps.Length},true));
                Selection.activeGameObject=group;
                SetupPreview(prefabs);
            }
            catch(Exception e){Debug.LogException(e);if(Application.isBatchMode)EditorApplication.Exit(1);}
        }

        static void SetupPreview(List<GameObject> prefabs)
        {
            // Studio objects exist only after the authored scene was saved and verified.
            foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.cullingMask &= ~(1<<28);
            foreach(var v in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)) v.enabled=false;
            foreach(var p in UnityEngine.Object.FindObjectsByType<ReferenceHousePlanarReflection>(FindObjectsSortMode.None)) p.enabled=false;
            for(int i=0;i<prefabs.Count;i++)
            {
                var tree=UnityEngine.Object.Instantiate(prefabs[i]);tree.transform.position=new Vector3(i==0?-2.2f:2.3f,0,0);
                foreach(var t in tree.GetComponentsInChildren<Transform>(true))t.gameObject.layer=28;
                tree.GetComponent<LODGroup>().ForceLOD(0);
            }
            var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.name="Temporary preview ground";
            ground.layer=28;ground.transform.localScale=Vector3.one*20;
            var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.SetColor("_BaseColor",new Color(.53f,.52f,.47f));mat.SetFloat("_Smoothness",0);
            ground.GetComponent<Renderer>().sharedMaterial=mat;
            var key=new GameObject("Temporary preview sun").AddComponent<Light>();key.type=LightType.Directional;
            key.transform.rotation=Quaternion.Euler(48,-35,0);key.intensity=2;key.color=new Color(1,.96f,.88f);
            key.shadows=LightShadows.Soft;key.shadowBias=.02f;key.shadowNormalBias=.07f;key.cullingMask=1<<28;
            RenderSettings.sun=key;RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.50f,.56f,.65f);
            RenderSettings.ambientEquatorColor=new Color(.36f,.38f,.35f);
            RenderSettings.ambientGroundColor=new Color(.25f,.23f,.19f);
            previewCamera=new GameObject("Temporary tree preview camera").AddComponent<Camera>();
            previewCamera.transform.position=new Vector3(7,6.7f,-18);
            previewCamera.transform.LookAt(new Vector3(-.2f,2.8f,0));
            previewCamera.orthographic=true;previewCamera.orthographicSize=4.1f;
            previewCamera.nearClipPlane=.1f;previewCamera.farClipPlane=100;
            previewCamera.cullingMask=1<<28;previewCamera.clearFlags=CameraClearFlags.SolidColor;
            previewCamera.backgroundColor=new Color(.59f,.62f,.65f);
            previewCamera.GetUniversalAdditionalCameraData().renderShadows=true;
            frames=0;EditorApplication.update+=Capture;
        }

        static void Capture()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if(++frames<80 || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
            EditorApplication.update-=Capture;
            try
            {
                ShaderUtil.allowAsyncCompilation=false;
                var rt=new RenderTexture(1800,1350,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);rt.Create();
                var request=new UniversalRenderPipeline.SingleCameraRequest{destination=rt};
                RenderPipeline.SubmitRenderRequest(previewCamera,request);
                RenderTexture.active=rt;
                var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false,false);
                image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();
                File.WriteAllBytes(Output+"/Trees_Unity.png",image.EncodeToPNG());
                RenderTexture.active=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(image);
                Debug.Log("MEDITERRANEAN_TREES_SAVED_AND_VERIFIED");
                if(Application.isBatchMode)EditorApplication.Exit(0);
                else
                {
                    EditorSceneManager.OpenScene(ScenePath);
                    Selection.activeGameObject=GameObject.Find(GroupName);
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
            }
            catch(Exception e)
            {
                Debug.LogException(e);
                if(Application.isBatchMode)EditorApplication.Exit(1);
                else EditorSceneManager.OpenScene(ScenePath);
            }
        }
    }
}
