using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    // Lighting-only revision: no model, material, texture or camera edits.
    public static class ReferenceHouseLightingReview
    {
        const string ScenePath = "Assets/ReferenceHouse/Scenes/ReferenceHouse.unity";
        const string Reports = "C:/Protergia/Blender/ReferenceHouse";

        [MenuItem("Reference House/Refine lighting only and bake (V9)")]
        public static void ApplyAndBake()
        {
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty)
                        throw new InvalidOperationException("Save the open scene before applying lighting.");
                var scene = EditorSceneManager.OpenScene(ScenePath);
                ReferenceHouseLightingV9.Apply();
                EditorSceneManager.MarkSceneDirty(scene);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene);
                ReferenceHouseLightingBake.BakeHouse();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
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
                File.WriteAllText(Path.Combine(Reports, "Unity_LightingV9_Verified.json"), JsonUtility.ToJson(new Report {
                    valid = true, meshRenderers = renderers.Length,
                    cameraPosition = camera.transform.position,
                    cameraRotation = camera.transform.rotation,
                    focalLength = camera.focalLength, lensShift = camera.lensShift,
                    lightmaps = LightmapSettings.lightmaps.Length,
                    directSamples = settings.directSampleCount,
                    indirectSamples = settings.indirectSampleCount,
                    bounces = settings.maxBounces
                }, true));
                ReferenceHouseLightingBake.VerifySavedHouse();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        [Serializable]
        class Report
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
