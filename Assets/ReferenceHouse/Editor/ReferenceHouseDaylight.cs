using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    public static class ReferenceHouseDaylight
    {
        const string HouseScene = "Assets/ReferenceHouse/Scenes/ReferenceHouse.unity";

        [MenuItem("Reference House/Apply reference daylight and surfaces")]
        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != HouseScene)
                throw new InvalidOperationException("Open the ReferenceHouse scene before applying its lighting.");

            ReferenceHouseMaterialLook.Apply();
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.gameObject.scene == scene)
                    SetLight(light);
            }

            Transform rig = RenderSettings.sun.transform.parent;
            AddWindowLight(rig, "Living room window | baked daylight", new Vector3(-4.19f, 1.98f, 1.52f), .85f);
            AddWindowLight(rig, "Bedroom window | baked daylight", new Vector3(-4.19f, 5.24f, 1.60f), 1.0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.48f, .50f, .52f);
            RenderSettings.ambientEquatorColor = new Color(.40f, .38f, .35f);
            RenderSettings.ambientGroundColor = new Color(.22f, .20f, .18f);
            RenderSettings.ambientIntensity = .73f;
            RenderSettings.reflectionIntensity = .55f;
            SetBackdrop();
            SetColorGrading();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static void SetLight(Light light)
        {
            string name = light.name;
            if (name == "Sun | warm afternoon")
            {
                // Light the right elevation from the side. The floor plates shade the rooms.
                light.transform.rotation = Quaternion.Euler(51f, -126f, 0f);
                light.color = new Color(1f, .975f, .94f);
                light.intensity = 1.02f;
                light.bounceIntensity = 1.12f;
                light.shadowAngle = 3.2f;
                light.shadowStrength = .90f;
                RenderSettings.sun = light;
            }
            else if (name == "Open sky | soft frontal fill")
            {
                light.color = new Color(.89f, .93f, 1f);
                light.intensity = .24f;
                light.bounceIntensity = .55f;
            }
            else if (name == "Front daylight bounce")
            {
                light.intensity = 2.4f;
                light.color = new Color(.94f, .96f, 1f);
            }
            else if (name.EndsWith("soft fill", StringComparison.Ordinal))
            {
                light.intensity = .16f;
                light.color = new Color(1f, .90f, .76f);
                light.shadowRadius = .3f;
            }
            else if (name == "Basement daylight bounce")
            {
                light.intensity = .95f;
                light.color = new Color(.95f, .96f, 1f);
            }
            else if (name.StartsWith("Warm recessed light", StringComparison.Ordinal))
            {
                SetPractical(light, light.transform.position.x > 3f ? 1.45f : 1.95f, 100f);
            }
            else if (name.StartsWith("Dining pendant light", StringComparison.Ordinal))
            {
                SetPractical(light, 1.18f, 88f);
            }
            else if (name.StartsWith("Basement utility light", StringComparison.Ordinal))
            {
                SetPractical(light, .78f, 108f);
                light.color = new Color(1f, .89f, .75f);
            }
            EditorUtility.SetDirty(light);
        }

        static void SetPractical(Light light, float intensity, float angle)
        {
            light.intensity = intensity;
            light.color = new Color(1f, .82f, .64f);
            light.spotAngle = angle;
            light.innerSpotAngle = angle * .48f;
            light.shadowRadius = .13f;
            light.bounceIntensity = 1.05f;
        }

        static void AddWindowLight(Transform rig, string name, Vector3 position, float intensity)
        {
            Transform existing = rig.Find(name);
            Light light;
            if (existing == null)
            {
                var obj = new GameObject(name);
                obj.transform.SetParent(rig, false);
                light = obj.AddComponent<Light>();
            }
            else
            {
                light = existing.GetComponent<Light>();
            }
            light.type = LightType.Rectangle;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.shadows = LightShadows.Soft;
            light.areaSize = new Vector2(1.75f, 1.90f);
            light.intensity = intensity;
            light.color = new Color(.96f, .96f, 1f);
            light.range = 7f;
            light.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.right, Vector3.up));
            EditorUtility.SetDirty(light);
        }

        static void SetBackdrop()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ReferenceHouse/Materials/PresentationGround.mat");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/ReferenceHouse/Settings/ReferenceBackdrop.shader");
            if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("The daylight backdrop shader must compile before rendering.");
            mat.shader = shader;
            mat.SetColor("_Floor", new Color(.72f, .72f, .69f));
            mat.SetColor("_Horizon", new Color(.85f, .86f, .85f));
            mat.SetColor("_Sky", new Color(.59f, .73f, .88f));
            EditorUtility.SetDirty(mat);
        }

        static void SetColorGrading()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/ReferenceHouse/Settings/ReferenceHouse_Volume.asset");
            if (profile.TryGet(out ColorAdjustments color))
            {
                color.postExposure.Override(-.08f);
                color.contrast.Override(8f);
                color.saturation.Override(1f);
                EditorUtility.SetDirty(color);
            }
            if (profile.TryGet(out WhiteBalance balance))
            {
                balance.temperature.Override(8f);
                EditorUtility.SetDirty(balance);
            }
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.intensity.Override(.07f);
                bloom.threshold.Override(1.35f);
                EditorUtility.SetDirty(bloom);
            }
            EditorUtility.SetDirty(profile);
        }
    }
}
