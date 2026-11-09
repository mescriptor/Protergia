using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    // V9 provides an idempotent base. V10 separates daylight from the room depths
    // and changes only the shared material on the three front charcoal bands.
    public static class ReferenceHouseContrastV10
    {
        const string Root = "Assets/ReferenceHouse";
        const string ScenePath = Root + "/Scenes/ReferenceHouse.unity";
        const string Reports = "C:/Protergia/Blender/ReferenceHouse";

        [MenuItem("Reference House/Refine reference contrast and bake (V10)")]
        public static void ApplyAndBake()
        {
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty)
                        throw new InvalidOperationException("Save the open scene before applying contrast.");
                var scene = EditorSceneManager.OpenScene(ScenePath);
                ReferenceHouseLightingV9.Apply();

                RenderSettings.ambientSkyColor = new Color(.38f, .43f, .50f);
                RenderSettings.ambientEquatorColor = new Color(.27f, .28f, .30f);
                RenderSettings.ambientGroundColor = new Color(.15f, .145f, .125f);
                foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light.gameObject.scene != scene) continue;
                    switch (light.name)
                    {
                        case "Sun | warm afternoon": light.intensity = 1.36f; break;
                        case "Front daylight bounce":
                            light.intensity = .70f;
                            light.shadows = LightShadows.Soft;
                            light.shadowRadius = .65f;
                            light.shadowBias = .025f;
                            light.shadowNormalBias = .12f;
                            light.bounceIntensity = .75f;
                            break;
                        case "Open sky | soft frontal fill": light.intensity = .04f; break;
                        case "Open section | living daylight": light.intensity = .35f; break;
                        case "Open section | upper daylight": light.intensity = .30f; break;
                        case "Basement daylight bounce":
                            light.intensity = .38f;
                            light.shadows = LightShadows.Soft;
                            light.shadowRadius = .35f;
                            light.shadowBias = .025f;
                            light.shadowNormalBias = .12f;
                            break;
                        default:
                            if (light.name.EndsWith("soft fill", StringComparison.Ordinal))
                                light.intensity = .04f;
                            else if (light.name.StartsWith("Basement utility light", StringComparison.Ordinal))
                                light.intensity = .52f;
                            break;
                    }
                    if (light.name == "Front daylight bounce" || light.name == "Basement daylight bounce")
                    {
                        // Broad, weak fill shadows need less atlas space than the
                        // practical lights. Keep those sharper shadows at full size.
                        var shadowData = new SerializedObject(light.GetUniversalAdditionalLightData());
                        shadowData.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = 1;
                        shadowData.ApplyModifiedPropertiesWithoutUndo();
                    }
                    EditorUtility.SetDirty(light);
                }

                var settings = Lightmapping.lightingSettings;
                settings.directSampleCount = 128;
                settings.indirectSampleCount = 1024;
                settings.environmentSampleCount = 512;
                EditorUtility.SetDirty(settings);

                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Settings/ReferenceHouse_Renderer.asset");
                var ao = renderer.rendererFeatures.First(f => f && f.GetType().Name == "ScreenSpaceAmbientOcclusion");
                var data = new SerializedObject(ao);
                data.FindProperty("m_Settings.Intensity").floatValue = .57f;
                data.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(ao);

                var frame = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Charcoal_architectural_frame.mat");
                if (!frame) throw new InvalidOperationException("Missing front-band material.");
                var frameColor = new Color(.39f, .395f, .385f, 1f);
                frame.SetColor("_BaseColor", frameColor);
                frame.SetColor("_Color", frameColor);
                EditorUtility.SetDirty(frame);

                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene);
                Debug.Log("HOUSE_V10_CONTRAST_READY frontFill=0.70 sectionFill=0.35/0.30 basementFill=0.38");
                ReferenceHouseLightingBake.BakeHouse();
            }
            catch (Exception error) { Fail(error); }
        }

        public static void VerifyAndRender()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath);
                var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
                if (renderers.Any(r => r.sharedMaterials.Any(m => !m || !m.shader || ShaderUtil.ShaderHasError(m.shader))))
                    throw new InvalidOperationException("Missing material or shader error.");
                var camera = Camera.main;
                if (!camera) throw new InvalidOperationException("Missing reference camera.");
                var settings = Lightmapping.lightingSettings;
                var front = GameObject.Find("Front daylight bounce").GetComponent<Light>();
                if (Mathf.Abs(front.intensity - .70f) > .001f || front.shadows == LightShadows.None)
                    throw new InvalidOperationException("V10 lighting did not persist.");
                File.WriteAllText(Path.Combine(Reports, "Unity_ContrastV10_Verified.json"), JsonUtility.ToJson(new Report {
                    valid = true, meshRenderers = renderers.Length,
                    cameraPosition = camera.transform.position, cameraRotation = camera.transform.rotation,
                    focalLength = camera.focalLength, lensShift = camera.lensShift,
                    lightmaps = LightmapSettings.lightmaps.Length,
                    directSamples = settings.directSampleCount, indirectSamples = settings.indirectSampleCount,
                    bounces = settings.maxBounces
                }, true));
                ReferenceHouseLightingBake.VerifySavedHouse();
            }
            catch (Exception error) { Fail(error); }
        }

        static void Fail(Exception error)
        {
            Debug.LogException(error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        [Serializable] class Report
        {
            public bool valid;
            public int meshRenderers, lightmaps, directSamples, indirectSamples, bounces;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public float focalLength;
            public Vector2 lensShift;
        }
    }
}
