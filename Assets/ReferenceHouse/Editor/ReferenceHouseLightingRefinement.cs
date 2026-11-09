using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    // Apply after the base reference palette. The caller owns saving and baking.
    public static class ReferenceHouseLightingRefinement
    {
        const string Root = "Assets/ReferenceHouse";
        const string ScenePath = Root + "/Scenes/ReferenceHouse.unity";

        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                throw new InvalidOperationException("Open the ReferenceHouse scene before refining its lighting.");

            RefineFloor();
            RefineFloorProbe(scene);
            AddSectionDaylight(scene);
            int changedLights = 0;
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.gameObject.scene != scene || !RefineLight(light)) continue;
                EditorUtility.SetDirty(light);
                changedLights++;
            }

            // Keep shaded rooms dimensional while the windows provide stronger daylight.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.49f, .51f, .54f);
            RenderSettings.ambientEquatorColor = new Color(.40f, .38f, .35f);
            RenderSettings.ambientGroundColor = new Color(.22f, .20f, .18f);
            RenderSettings.ambientIntensity = .73f;
            RenderSettings.reflectionIntensity = .68f;
            RefineGrading();
            Debug.Log($"HOUSE_LIGHTING_REFINEMENT lights={changedLights} floorSmoothness=0.89 floorNormal=0.025 probeResolution=512");
        }

        static void RefineFloor()
        {
            for (int tone = 0; tone < 5; tone++)
            {
                string path = $"{Root}/Materials/Ground_polished_travertine_{tone:00}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material || !material.HasProperty("_Smoothness"))
                    throw new InvalidOperationException("Missing polished ground-floor material: " + path);
                material.SetFloat("_Smoothness", .89f);
                material.SetFloat("_Metallic", 0f);
                // A sealed stone finish needs a visible dielectric reflection at
                // the low presentation angle, while retaining its mineral colour.
                material.SetFloat("_WorkflowMode", 0f);
                material.EnableKeyword("_SPECULAR_SETUP");
                // Colour properties are authored in sRGB; encode a .10 linear F0.
                material.SetColor("_SpecColor", new Color(.10f, .10f, .10f, 1).gamma);
                material.SetFloat("_BumpScale", .025f);
                material.SetFloat("_SpecularHighlights", 1f);
                material.SetFloat("_EnvironmentReflections", 1f);
                material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                material.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                EditorUtility.SetDirty(material);
            }
        }

        static void RefineFloorProbe(Scene scene)
        {
            ReflectionProbe floor = null;
            foreach (var probe in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
                if (probe.gameObject.scene == scene && probe.name == "Reflection probe | living room and kitchen")
                    floor = probe;
            if (!floor)
                throw new InvalidOperationException("The living room and kitchen reflection probe is missing.");
            floor.mode = ReflectionProbeMode.Baked;
            floor.resolution = 512;
            floor.intensity = 1.1f;
            floor.boxProjection = true;
            floor.importance = 2;
            floor.blendDistance = .3f;
            floor.size = new Vector3(8.7f, 3.2f, 6.5f);
            floor.center = new Vector3(0, .2f, -.6f);
            EditorUtility.SetDirty(floor);
        }

        static void AddSectionDaylight(Scene scene)
        {
            Transform rig = RenderSettings.sun.transform.parent;
            foreach (bool upper in new[] {false, true})
            {
                string name = upper ? "Open section | upper daylight" : "Open section | living daylight";
                Transform existing = rig.Find(name);
                var light = existing ? existing.GetComponent<Light>() : new GameObject(name).AddComponent<Light>();
                light.transform.SetParent(rig, false);
                light.transform.SetPositionAndRotation(new Vector3(0, upper ? 5.25f : 2.10f, -2.3f), Quaternion.identity);
                light.type = LightType.Rectangle;
                light.areaSize = new Vector2(7.6f, 2.0f);
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.intensity = upper ? .7f : .9f;
                light.color = new Color(1, .97f, .91f);
                light.shadows = LightShadows.Soft;
                light.bounceIntensity = 1;
                EditorUtility.SetDirty(light);
            }
        }

        static bool RefineLight(Light light)
        {
            string name = light.name;
            if (name == "Sun | warm afternoon")
            {
                // Preserve the established afternoon direction and its exterior shadows.
                light.color = new Color(1f, .963f, .90f);
                light.intensity = 1.16f;
                light.bounceIntensity = 1.15f;
                light.shadowAngle = 3.8f;
                light.shadowStrength = .90f;
                light.lightmapBakeType = LightmapBakeType.Mixed;
                RenderSettings.sun = light;
            }
            else if (name == "Open sky | soft frontal fill")
            {
                light.color = new Color(.89f, .94f, 1f);
                light.intensity = .20f;
                light.bounceIntensity = .55f;
            }
            else if (name == "Front daylight bounce")
            {
                light.color = new Color(.95f, .97f, 1f);
                light.intensity = 2.8f;
                light.bounceIntensity = 1f;
            }
            else if (name == "Living room window | baked daylight" || name == "Bedroom window | baked daylight")
            {
                light.color = new Color(1f, .975f, .925f);
                light.intensity = name.StartsWith("Living", StringComparison.Ordinal) ? 1.25f : 1.35f;
                light.bounceIntensity = 1.1f;
                light.lightmapBakeType = LightmapBakeType.Baked;
            }
            else if (name.EndsWith("soft fill", StringComparison.Ordinal))
            {
                light.color = new Color(1f, .91f, .80f);
                light.intensity = .18f;
                light.shadowRadius = .3f;
            }
            else if (name.StartsWith("Warm recessed light", StringComparison.Ordinal))
            {
                RefinePractical(light, light.transform.position.x > 3f ? 1.65f : 2.20f, 100f);
            }
            else if (name.StartsWith("Dining pendant light", StringComparison.Ordinal))
            {
                RefinePractical(light, 1.40f, 88f);
            }
            else if (name.StartsWith("Basement utility light", StringComparison.Ordinal))
            {
                light.intensity = .86f;
                light.color = new Color(1f, .90f, .77f);
            }
            else return false;
            return true;
        }

        static void RefinePractical(Light light, float intensity, float cone)
        {
            // Mixed lights retain baked bounce and add the direct specular response
            // that fully baked spots cannot provide on the polished stone.
            light.lightmapBakeType = LightmapBakeType.Mixed;
            light.intensity = intensity;
            light.color = new Color(1f, .84f, .68f);
            light.spotAngle = cone;
            light.innerSpotAngle = cone * .48f;
            light.shadows = LightShadows.Soft;
            light.shadowRadius = .16f;
            light.bounceIntensity = 1.05f;
        }

        static void RefineGrading()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "/Settings/ReferenceHouse_Volume.asset");
            if (!profile) throw new InvalidOperationException("The reference house volume profile is missing.");
            if (profile.TryGet(out ColorAdjustments color))
            {
                color.postExposure.Override(.02f);
                color.contrast.Override(9f);
                color.saturation.Override(1f);
                EditorUtility.SetDirty(color);
            }
            if (profile.TryGet(out WhiteBalance balance))
            {
                balance.temperature.Override(6f);
                EditorUtility.SetDirty(balance);
            }
            if (profile.TryGet(out Bloom bloom))
            {
                bloom.intensity.Override(.09f);
                bloom.threshold.Override(1.25f);
                EditorUtility.SetDirty(bloom);
            }
            EditorUtility.SetDirty(profile);
        }
    }
}
