using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    // Material authoring is independent of this pass. The caller saves and bakes the scene.
    public static class ReferenceHouseLightingV9
    {
        const string Root = "Assets/ReferenceHouse";
        const string ScenePath = Root + "/Scenes/ReferenceHouse.unity";

        public static void Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                throw new InvalidOperationException("Open ReferenceHouse before applying its V9 daylight.");

            int count = 0;
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.gameObject.scene != scene || !SetLight(light)) continue;
                EditorUtility.SetDirty(light);
                EditorUtility.SetDirty(light.transform);
                count++;
            }

            // The sky supplies cool shadow detail; warm sunlight supplies the key.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.43f, .48f, .57f);
            RenderSettings.ambientEquatorColor = new Color(.34f, .35f, .36f);
            RenderSettings.ambientGroundColor = new Color(.18f, .17f, .145f);
            RenderSettings.ambientIntensity = .62f;
            RenderSettings.reflectionIntensity = .75f;

            ConfigureBake(scene);
            ConfigureContactShadows();
            ConfigureGrade();
            AssetDatabase.SaveAssets();
            Debug.Log($"HOUSE_V9_LIGHTING_READY lights={count} sun=1.48 indirectSamples={Lightmapping.lightingSettings.indirectSampleCount} bounces=6");
        }

        static bool SetLight(Light light)
        {
            string name = light.name;
            if (name == "Sun | warm afternoon")
            {
                // Front-right sunlight reaches the open section and furniture,
                // while the floor plates still shade the deeper room surfaces.
                light.transform.rotation = Quaternion.Euler(46f, -52f, 0f);
                light.color = new Color(1f, .963f, .895f);
                light.intensity = 1.48f;
                light.bounceIntensity = 1.12f;
                light.shadowAngle = 2.2f;
                light.shadowStrength = .95f;
                light.shadowBias = .025f;
                light.shadowNormalBias = .15f;
                light.shadows = LightShadows.Soft;
                light.lightmapBakeType = LightmapBakeType.Mixed;
                RenderSettings.sun = light;
            }
            else if (name == "Open sky | soft frontal fill")
            {
                light.color = new Color(.83f, .91f, 1f);
                light.intensity = .085f;
                light.bounceIntensity = .5f;
            }
            else if (name == "Front daylight bounce")
            {
                light.color = new Color(.87f, .93f, 1f);
                light.intensity = 1.55f;
                light.bounceIntensity = 1f;
            }
            else if (name == "Living room window | baked daylight" || name == "Bedroom window | baked daylight")
            {
                light.color = new Color(.92f, .96f, 1f);
                light.intensity = name.StartsWith("Living", StringComparison.Ordinal) ? 1.60f : 1.70f;
                light.bounceIntensity = 1.10f;
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.shadows = LightShadows.Soft;
            }
            else if (name == "Open section | living daylight" || name == "Open section | upper daylight")
            {
                light.color = new Color(.92f, .96f, 1f);
                light.intensity = name.Contains("living") ? .60f : .50f;
                light.bounceIntensity = 1f;
                light.lightmapBakeType = LightmapBakeType.Baked;
            }
            else if (name.EndsWith("soft fill", StringComparison.Ordinal))
            {
                light.color = new Color(.98f, .95f, .89f);
                light.intensity = .09f;
                light.shadowRadius = .35f;
            }
            else if (name.StartsWith("Warm recessed light", StringComparison.Ordinal))
            {
                SetPractical(light, light.transform.position.x > 3f ? 1.35f : 1.80f, 96f);
            }
            else if (name.StartsWith("Dining pendant light", StringComparison.Ordinal))
            {
                SetPractical(light, 1.15f, 84f);
            }
            else if (name.StartsWith("Basement utility light", StringComparison.Ordinal))
            {
                light.intensity = .68f;
                light.color = new Color(1f, .89f, .76f);
                light.bounceIntensity = 1f;
            }
            else if (name == "Basement daylight bounce")
            {
                light.intensity = .78f;
                light.color = new Color(.90f, .95f, 1f);
            }
            else return false;
            return true;
        }

        static void SetPractical(Light light, float intensity, float cone)
        {
            light.intensity = intensity;
            light.color = new Color(1f, .855f, .70f);
            light.lightmapBakeType = LightmapBakeType.Mixed;
            light.spotAngle = cone;
            light.innerSpotAngle = cone * .52f;
            light.shadows = LightShadows.Soft;
            light.shadowRadius = .11f;
            light.shadowBias = .025f;
            light.shadowNormalBias = .12f;
            light.bounceIntensity = 1f;
        }

        static void ConfigureBake(Scene scene)
        {
            var settings = Lightmapping.GetLightingSettingsForScene(scene);
            if (!settings)
                throw new InvalidOperationException("The saved ReferenceHouse lighting settings are missing.");
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.directionalityMode = LightmapsMode.CombinedDirectional;
            settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
            settings.lightmapResolution = 26f;
            settings.lightmapMaxSize = 2048;
            settings.lightmapPadding = 4;
            settings.directSampleCount = 96;
            settings.indirectSampleCount = 768;
            settings.environmentSampleCount = 384;
            settings.minBounces = 2;
            settings.maxBounces = 6;
            settings.indirectScale = 1f;
            settings.ao = true;
            // Local junctions need grounding; broad dark halos hide the material detail.
            settings.aoMaxDistance = .45f;
            settings.aoExponentIndirect = .55f;
            settings.aoExponentDirect = 0f;
            settings.filteringMode = LightingSettings.FilterMode.Auto;
            EditorUtility.SetDirty(settings);
        }

        static void ConfigureContactShadows()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Settings/ReferenceHouse_Renderer.asset");
            if (!renderer) throw new InvalidOperationException("The house renderer profile is missing.");
            foreach (var feature in renderer.rendererFeatures)
            {
                if (!feature || feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                var data = new SerializedObject(feature);
                SetFloat(data, "m_Settings.Intensity", .48f);
                SetFloat(data, "m_Settings.Radius", .22f);
                SetFloat(data, "m_Settings.DirectLightingStrength", .18f);
                var downsample = data.FindProperty("m_Settings.Downsample");
                if (downsample != null) downsample.boolValue = false;
                data.ApplyModifiedPropertiesWithoutUndo();
                feature.SetActive(true);
                EditorUtility.SetDirty(feature);
            }
            EditorUtility.SetDirty(renderer);
        }

        static void SetFloat(SerializedObject obj, string name, float value)
        {
            var property = obj.FindProperty(name);
            if (property == null) throw new InvalidOperationException("Missing render property: " + name);
            property.floatValue = value;
        }

        static void ConfigureGrade()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/Settings/ReferenceHouse_Volume.asset");
            if (!profile) throw new InvalidOperationException("The house grading profile is missing.");
            if (profile.TryGet(out ColorAdjustments grade))
            {
                grade.postExposure.Override(.04f);
                grade.contrast.Override(12f);
                grade.saturation.Override(-2f);
                EditorUtility.SetDirty(grade);
            }
            if (profile.TryGet(out WhiteBalance white))
            {
                white.temperature.Override(3f);
                white.tint.Override(0f);
                EditorUtility.SetDirty(white);
            }
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.intensity.Override(.065f);
                bloom.threshold.Override(1.35f);
                EditorUtility.SetDirty(bloom);
            }
            EditorUtility.SetDirty(profile);
        }
    }
}
