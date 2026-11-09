using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    public static class ReferenceHouseLightingBake
    {
        const string Root = "Assets/ReferenceHouse";
        const string ScenePath = Root + "/Scenes/ReferenceHouse.unity";
        const string SettingsPath = Root + "/Settings/ReferenceHouse_BakedLighting.lighting";
        const string ReportDirectory = "C:/Protergia/Blender/ReferenceHouse";

        public static void SmokeTest()
        {
            try
            {
                const string folder = "Assets/HouseGpuSmoke";
                Directory.CreateDirectory(folder);
                AssetDatabase.Refresh();
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "GPU test receiver";
                block.transform.localScale = new Vector3(4, .2f, 4);
                Mesh mesh = UnityEngine.Object.Instantiate(block.GetComponent<MeshFilter>().sharedMesh);
                Unwrapping.GenerateSecondaryUVSet(mesh);
                AssetDatabase.CreateAsset(mesh, folder + "/receiver.asset");
                block.GetComponent<MeshFilter>().sharedMesh = mesh;
                GameObjectUtility.SetStaticEditorFlags(block, StaticEditorFlags.ContributeGI);
                block.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.Lightmaps;
                Light source = new GameObject("GPU test light").AddComponent<Light>();
                source.type = LightType.Directional;
                source.lightmapBakeType = LightmapBakeType.Baked;
                source.transform.rotation = Quaternion.Euler(45, -30, 0);
                source.intensity = 2;
                EditorSceneManager.SaveScene(scene, folder + "/GpuSmoke.unity");
                Configure(scene, folder + "/GpuSmoke.lighting", true);
                BakeAndVerify(scene, "Smoke");
            }
            catch (Exception error) { Fail(error); }
        }

        [MenuItem("Reference House/Bake lighting with GPU")]
        public static void BakeHouse()
        {
            RunHouseBake(false);
        }

        [MenuItem("Reference House/Apply reference look and bake with GPU")]
        public static void ApplyLookAndBakeHouse()
        {
            RunHouseBake(true);
        }

        static void RunHouseBake(bool applyReferenceLook)
        {
            try
            {
                EnsureSavedScenes();
                Scene scene = EditorSceneManager.OpenScene(ScenePath);
                if (applyReferenceLook) ReferenceHouseDaylight.Apply();
                LightingSettings savedSettings = Lightmapping.GetLightingSettingsForScene(scene);
                if (applyReferenceLook || savedSettings == null)
                {
                    Configure(scene, SettingsPath, false);
                    PrepareReceivers(scene);
                    PrepareLights(scene);
                    PrepareProbes();
                }
                else
                {
                    savedSettings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
                    savedSettings.bakedGI = true;
                    EditorUtility.SetDirty(savedSettings);
                    AssetDatabase.SaveAssets();
                }
                BakeAndVerify(scene, "House");
            }
            catch (Exception error) { Fail(error); }
        }

        static void EnsureSavedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the open modified scene before starting the bake.");
        }

        static void Configure(Scene scene, string path, bool smoke)
        {
            LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (settings == null)
            {
                settings = new LightingSettings();
                AssetDatabase.CreateAsset(settings, path);
            }
            settings.name = smoke ? "GPU smoke test" : "Reference House | Progressive GPU";
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.directionalityMode = LightmapsMode.CombinedDirectional;
            settings.lightmapResolution = smoke ? 4 : 20;
            settings.lightmapMaxSize = smoke ? 256 : 1024;
            settings.lightmapPadding = 4;
            settings.directSampleCount = smoke ? 16 : 64;
            settings.indirectSampleCount = smoke ? 32 : 512;
            settings.environmentSampleCount = smoke ? 16 : 256;
            settings.minBounces = 2;
            settings.maxBounces = smoke ? 2 : 4;
            settings.ao = !smoke;
            settings.aoMaxDistance = .8f;
            settings.aoExponentIndirect = .7f;
            settings.aoExponentDirect = 0;
            settings.indirectScale = 1;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            Lightmapping.SetLightingSettingsForScene(scene, settings);
            EditorUtility.SetDirty(settings);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
        }

        internal static void PrepareReceivers(Scene scene)
        {
            int charts = 0;
            int receivers = 0;
            foreach (MeshRenderer renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.gameObject.scene != scene) continue;
                bool eligible=Eligible(renderer);
                if(!eligible)
                {
                    // A resized architectural part can fall below the original
                    // area cutoff while retaining a previous lightmap assignment.
                    string group=renderer.transform.parent ? renderer.transform.parent.name : "";
                    bool keepReceiver=renderer.enabled && renderer.gameObject.activeInHierarchy &&
                        (GameObjectUtility.GetStaticEditorFlags(renderer.gameObject)&StaticEditorFlags.ContributeGI)!=0 &&
                        renderer.receiveGI==ReceiveGI.Lightmaps && !group.StartsWith("17_") && !group.StartsWith("18_") && !group.StartsWith("08_") && !group.StartsWith("22_") &&
                        renderer.GetComponent<MeshFilter>() && renderer.GetComponent<MeshFilter>().sharedMesh;
                    if(!keepReceiver)continue;
                }
                Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (!mesh.HasVertexAttribute(VertexAttribute.TexCoord1))
                {
                    UnwrapParam.SetDefaults(out UnwrapParam options);
                    options.packMargin = 8f / 1024f;
                    if (!Unwrapping.GenerateSecondaryUVSet(mesh, options))
                        throw new Exception("Could not create lightmap UVs for " + renderer.name);
                    EditorUtility.SetDirty(mesh);
                    charts++;
                }
                var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags | StaticEditorFlags.ContributeGI);
                renderer.receiveGI = ReceiveGI.Lightmaps;
                renderer.scaleInLightmap = 1;
                EditorUtility.SetDirty(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer.gameObject);
                receivers++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"HOUSE_GPU_RECEIVERS count={receivers} new_UV2={charts}");
        }

        static bool Eligible(MeshRenderer renderer)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) return false;
            string group = renderer.transform.parent == null ? "" : renderer.transform.parent.name;
            bool solarModule=group.StartsWith("08_") && !renderer.name.StartsWith("PV / plaster ",StringComparison.OrdinalIgnoreCase);
            if (group.StartsWith("17_") || group.StartsWith("18_") || solarModule || group.StartsWith("22_") || renderer.name.StartsWith("Presentation")) return false;
            if (renderer.sharedMaterials.All(m => m == null || m.renderQueue >= (int)RenderQueue.Transparent)) return false;
            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || filter.sharedMesh.vertexCount < 3) return false;
            Vector3 size = renderer.bounds.size;
            float largestFace = Mathf.Max(size.x * size.y, size.y * size.z, size.z * size.x);
            return largestFace >= .045f;
        }

        static void PrepareLights(Scene scene)
        {
            foreach (Light source in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!source.enabled || source.gameObject.scene != scene || source.lightmapBakeType != LightmapBakeType.Realtime) continue;
                source.lightmapBakeType = source.type == LightType.Spot ? LightmapBakeType.Baked : LightmapBakeType.Mixed;
                EditorUtility.SetDirty(source);
            }
        }

        static void PrepareProbes()
        {
            if (UnityEngine.Object.FindAnyObjectByType<LightProbeGroup>()) return;
            LightProbeGroup group = new GameObject("Light probes | rooms, garden and driveway").AddComponent<LightProbeGroup>();
            var points = new List<Vector3>();
            foreach (float x in new[]{-5.8f,-3.2f,-.4f,2.8f,5.2f,7.4f})
                foreach (float z in new[]{-1.1f,.75f,2.6f,4.7f,6.5f})
                    foreach (float y in new[]{-.95f,1.1f,2.75f,4.35f,6.0f}) points.Add(new Vector3(x,y,z));
            group.probePositions = points.ToArray();
        }

        static void BakeAndVerify(Scene scene, string label)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("HOUSE_GPU_BAKE_START " + label + " lightmapper=" + Lightmapping.lightingSettings.lightmapper);
            if (!Lightmapping.Bake()) throw new Exception("Unity rejected the light bake.");
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            string gpu = ReadGpuName();
            if (label == "House" && Environment.GetCommandLineArgs().Contains("-houseRequireRTX4060") && !gpu.Contains("RTX 4060"))
                throw new Exception("Production lightmapping did not select the requested RTX 4060: " + gpu);
            var maps = LightmapSettings.lightmaps;
            if (maps == null || maps.Length == 0 || maps.Any(m => !m.lightmapColor)) throw new Exception("Bake produced no valid color lightmaps.");
            int assigned = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Count(r=>r.lightmapIndex>=0 && r.lightmapIndex<maps.Length);
            if (assigned == 0) throw new Exception("No renderer has a baked lightmap assignment.");
            if (label == "House" && assigned < 100) throw new Exception("Too few architecture and furniture receivers were baked.");
            Debug.Log($"HOUSE_GPU_BAKE_COMPLETE label={label} gpu={gpu} maps={maps.Length} receivers={assigned}");
            var result = new BakeReport{scene=scene.path,backend=Lightmapping.lightingSettings.lightmapper.ToString(),gpu=gpu, assignedRenderers=assigned,lightmaps=maps.Select(m=>AssetDatabase.GetAssetPath(m.lightmapColor)).ToArray(), lightingDataPath=AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset)};
            File.WriteAllText(Path.Combine(ReportDirectory,"Unity_GPU_"+label+".json"), JsonUtility.ToJson(result,true));
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // Run in a fresh editor process. Confirm serialized lighting is restored
        // before rendering the production camera with the saved scene.
        public static void VerifySavedHouse()
        {
            try
            {
                EnsureSavedScenes();
                Scene scene = EditorSceneManager.OpenScene(ScenePath);
                LightingSettings settings = Lightmapping.lightingSettings;
                if (settings.lightmapper != LightingSettings.Lightmapper.ProgressiveGPU)
                    throw new Exception("The saved lighting settings have a different lightmapper.");
                if (!Lightmapping.lightingDataAsset)
                    throw new Exception("The saved scene has no LightingData asset.");
                var maps = LightmapSettings.lightmaps;
                if (maps == null || maps.Length == 0) throw new Exception("No saved lightmaps.");
                var receivers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .Where(r => r.gameObject.scene == scene && r.lightmapIndex >= 0 && r.lightmapIndex < maps.Length).ToArray();
                var groups = receivers.GroupBy(r => r.transform.parent.name).OrderBy(g => g.Key)
                    .Select(g => new GroupCount{group=g.Key,count=g.Count()}).ToArray();
                if (!groups.Any(g => g.group.StartsWith("02_")) || !groups.Any(g => g.group.StartsWith("03_")) || !groups.Any(g => g.group.StartsWith("19_")))
                    throw new Exception("The expected architecture and furniture receivers did not reload.");
                var mapDetails = new List<MapInfo>();
                foreach (LightmapData map in maps)
                {
                    if (!map.lightmapColor || !map.lightmapDir) throw new Exception("Missing saved color/direction texture.");
                    var detail = new MapInfo{color=AssetDatabase.GetAssetPath(map.lightmapColor), direction=AssetDatabase.GetAssetPath(map.lightmapDir),width=map.lightmapColor.width,height=map.lightmapColor.height};
                    MeasureAtlas(map.lightmapColor, detail);
                    mapDetails.Add(detail);
                }
                var report = new ReloadReport{scene=scene.path,backend=settings.lightmapper.ToString(),settings=AssetDatabase.GetAssetPath(settings),lightingData=AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset),assignedRenderers=receivers.Length,groups=groups,atlases=mapDetails.ToArray(),probeCount=LightmapSettings.lightProbes!=null?LightmapSettings.lightProbes.count:0};
                File.WriteAllText(Path.Combine(ReportDirectory,"Unity_GPU_Reload.json"),JsonUtility.ToJson(report,true));
                Debug.Log($"HOUSE_GPU_PERSISTENCE_VERIFIED atlases={maps.Length} receivers={receivers.Length} probes={report.probeCount}");
                ReferenceHouseImporter.CaptureExisting();
            }
            catch (Exception error) { Fail(error); }
        }

        static string ReadGpuName()
        {
            MethodInfo method = typeof(Lightmapping).GetMethod("GetLightmapBakeGPUDeviceName", BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static);
            return method != null && method.GetParameters().Length == 0 ? Convert.ToString(method.Invoke(null,null)) : "See Unity GPU backend log";
        }

        static void MeasureAtlas(Texture2D atlas, MapInfo detail)
        {
            var previous = RenderTexture.active;
            RenderTexture reduced = RenderTexture.GetTemporary(64,64,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            Texture2D sample = new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
            try
            {
                Graphics.Blit(atlas,reduced);
                RenderTexture.active = reduced;
                sample.ReadPixels(new Rect(0,0,64,64),0,0);
                foreach (Color c in sample.GetPixels())
                {
                    float energy = c.maxColorComponent;
                    if (energy>.001f) detail.litSamples++;
                    detail.peakSample = Mathf.Max(detail.peakSample,energy);
                }
                if (detail.litSamples==0) throw new Exception("Saved lightmap has no measurable illumination: "+detail.color);
            }
            finally
            {
                RenderTexture.active=previous;
                RenderTexture.ReleaseTemporary(reduced);
                UnityEngine.Object.DestroyImmediate(sample);
            }
        }

        [Serializable] class BakeReport { public string scene,backend,gpu,lightingDataPath; public int assignedRenderers; public string[] lightmaps; }
        [Serializable] class GroupCount { public string group; public int count; }
        [Serializable] class MapInfo { public string color,direction; public int width,height,litSamples; public float peakSample; }
        [Serializable] class ReloadReport { public string scene,backend,settings,lightingData; public int assignedRenderers,probeCount; public GroupCount[] groups; public MapInfo[] atlases; }
        static void Fail(Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
