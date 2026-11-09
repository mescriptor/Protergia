using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    public static class ReferenceHouseReferenceV8
    {
        const string Root = "Assets/ReferenceHouse";
        const string ScenePath = Root + "/Scenes/ReferenceHouse.unity";
        const string Reports = "C:/Protergia/Blender/ReferenceHouse";

        [MenuItem("Reference House/Refine doors, polished floor and neighborhood")]
        public static void ApplyAndBake()
        {
            try
            {
                for (int i=0; i<SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene first.");
                var scene = EditorSceneManager.OpenScene(ScenePath);
                ReferenceHouseDoorConsistency.ApplyToPrefab();
                ReferenceHouseLightingRefinement.Apply();
                ApplyBackdrop();
                ReferenceHousePlanarSetup.Apply();
                ReferenceHouseLightingBake.PrepareReceivers(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene);
                ReferenceHouseLightingBake.BakeHouse();
            }
            catch (Exception e) { Debug.LogException(e); if(Application.isBatchMode) EditorApplication.Exit(1); }
        }

        static void ApplyBackdrop()
        {
            string path = Root + "/Textures/Neighborhood_Backdrop.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/PresentationGround.mat");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Settings/ReferenceBackdrop.shader");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new Exception("Backdrop shader did not compile.");
            mat.shader = shader;
            mat.SetTexture("_BackdropTex", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            mat.SetFloat("_UseNeighborhood", 1);
            mat.SetFloat("_BackdropExposure", 1.0f);
            EditorUtility.SetDirty(mat);
            // The camera-facing plate supplies context, not six repeated probe faces.
            var backdrop = GameObject.Find("Presentation | curved neutral backdrop");
            backdrop.layer = 30;
            foreach (var probe in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            {
                probe.cullingMask &= ~(1<<30);
                EditorUtility.SetDirty(probe);
            }
        }

        public static void VerifyAndRender()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath);
                var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                if (renderers.Any(r => r.sharedMaterials.Any(m => !m || !m.shader || ShaderUtil.ShaderHasError(m.shader))))
                    throw new Exception("A renderer has a missing material or shader error.");
                var floor = renderers.Where(r=>r.sharedMaterials.Any(m=>m.name.StartsWith("Ground_polished_travertine"))).ToArray();
                if(floor.Length!=5 || floor.SelectMany(r=>r.sharedMaterials).Distinct().Count()!=5 || floor.Any(r=>r.sharedMaterials.Any(m=>m.GetFloat("_Smoothness")<.88f)))
                    throw new Exception("Polished floor validation failed.");
                var doorLeaves = renderers.Where(r=>r.name.EndsWith(" / door leaf")).ToArray();
                if(doorLeaves.Length!=4 || doorLeaves.Select(r=>r.sharedMaterial).Distinct().Count()!=1)
                    throw new Exception("The four doors must share one material.");
                var probe=GameObject.Find("Reflection probe | living room and kitchen").GetComponent<ReflectionProbe>();
                if(probe.resolution!=512 || !probe.bakedTexture) throw new Exception("Floor reflection probe is not baked.");
                var backdrop=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/PresentationGround.mat");
                if(!backdrop.GetTexture("_BackdropTex") || backdrop.GetFloat("_UseNeighborhood")!=1) throw new Exception("Missing neighborhood plate.");
                File.WriteAllText(Path.Combine(Reports,"Unity_ReferenceV8_Verified.json"), JsonUtility.ToJson(new Report {
                    valid=true, doors=doorLeaves.Length, polishedFloorRenderers=floor.Length,
                    reflectionResolution=probe.resolution, meshRenderers=renderers.Length,
                    lightmaps=LightmapSettings.lightmaps.Length,
                    mixedPracticals=UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l=>l.type==LightType.Spot && l.lightmapBakeType==LightmapBakeType.Mixed)
                },true));
                ReferenceHouseLightingBake.VerifySavedHouse();
            }
            catch(Exception e) { Debug.LogException(e); if(Application.isBatchMode) EditorApplication.Exit(1); }
        }

        public static void RefineAndBake()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath);
                ReferenceHouseLightingRefinement.Apply();
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene);
                ReferenceHouseLightingBake.BakeHouse();
            }
            catch(Exception e) { Debug.LogException(e); if(Application.isBatchMode) EditorApplication.Exit(1); }
        }

        public static void ApplyPlanarAndRender()
        {
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath);
                ReferenceHousePlanarSetup.Apply();
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene);
                VerifyAndRender();
            }
            catch(Exception e) { Debug.LogException(e); if(Application.isBatchMode) EditorApplication.Exit(1); }
        }

        public static void CaptureFloorDetail()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                var diagnostic = new System.Text.StringBuilder();
                foreach(var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if(r.bounds.min.y < .5f || r.bounds.max.y > .8f) continue;
                    var probes = new System.Collections.Generic.List<ReflectionProbeBlendInfo>();
                    r.GetClosestReflectionProbes(probes);
                    diagnostic.AppendLine(r.name+" bounds="+r.bounds+" materials="+string.Join(",",r.sharedMaterials.Select(m=>m.name))+" probes="+string.Join(",",probes.Select(p=>p.probe.name+":"+p.weight)));
                }
                File.WriteAllText(Path.Combine(Reports,"Unity_FloorDiagnostics.txt"),diagnostic.ToString());
                var camera = UnityEngine.Object.Instantiate(Camera.main);
                camera.name = "Polished stone inspection";
                camera.transform.position = new Vector3(7.6f, 5.8f, -13.4f);
                camera.transform.LookAt(new Vector3(-.2f, 1.65f, .6f));
                camera.usePhysicalProperties = false;
                camera.lensShift = Vector2.zero;
                camera.fieldOfView = 37;
                ReferenceHouseImporter.CaptureTemporary(camera, Path.Combine(Reports,"Preview_Unity_Floor_v8.png"));
            }
            catch(Exception e) { Debug.LogException(e); if(Application.isBatchMode) EditorApplication.Exit(1); }
        }
        [Serializable] class Report { public bool valid; public int doors,polishedFloorRenderers,reflectionResolution,meshRenderers,lightmaps,mixedPracticals; }
    }
}
