using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hotspot = ReferenceHouseTools.ReferenceHouseDiagram.Hotspot;

namespace ReferenceHouseTools
{
    /// <summary>Calibrates highlights from equipment silhouettes into the existing native-pixel diagram.</summary>
    public static class ReferenceHouseObjectHighlightSetup
    {
        [MenuItem("Reference House/Energy Corner/Refresh object highlights")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before refreshing object highlights.");
            const string scenePath = "Assets/ReferenceHouse/Scenes/ReferenceHouse.unity";
            if (SceneManager.GetActiveScene().path != scenePath)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).isDirty)
                        throw new InvalidOperationException("Save the open scene before refreshing ReferenceHouse highlights.");
                EditorSceneManager.OpenScene(scenePath);
            }
            var diagram = UnityEngine.Object.FindObjectsByType<ReferenceHouseDiagram>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Single(value => !value.cardBackground &&
                    value.gameObject.scene == SceneManager.GetActiveScene());
            Populate(diagram, Camera.main);
            EditorSceneManager.MarkSceneDirty(diagram.gameObject.scene);
            EditorSceneManager.SaveScene(diagram.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("OBJECT_HIGHLIGHTS_CALIBRATED " + diagram.equipmentOutlines.Length + " silhouettes; no player build.");
        }

        public static void Populate(ReferenceHouseDiagram diagram, Camera camera)
        {
            if (!diagram || !camera) throw new InvalidOperationException("The house diagram and its camera are required.");
            var meshes = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).Where(value => value.gameObject.scene == diagram.gameObject.scene &&
                    value.sharedMesh && value.gameObject.activeInHierarchy).ToArray();
            var shapes = new List<ReferenceHouseDiagram.EquipmentOutline>();
            float previousAspect = camera.aspect;
            try
            {
                camera.aspect = (float)ReferenceHouseLedWall.Width / ReferenceHouseLedWall.Height;
                shapes.Add(Project(camera, Hotspot.Meter, Exact(meshes, "Kitchen entry / smart meter envelope")));
                shapes.Add(Project(camera, Hotspot.HeatPump, Exact(meshes, "Heat pump / main envelope")));
                shapes.Add(Project(camera, Hotspot.Charger, Exact(meshes, "Carport / EV charger envelope")));
                shapes.Add(Project(camera, Hotspot.Battery, Exact(meshes,
                    "Basement / battery upper volume", "Basement / battery lower volume")));
                var modules = meshes.Where(value => value.name.StartsWith("PV module ", StringComparison.Ordinal) &&
                    (value.name.Contains(" frame ") || value.name.EndsWith(" laminate", StringComparison.Ordinal)))
                    .GroupBy(value => value.name.Substring(0, 12)).OrderBy(group => group.Key).ToArray();
                if (modules.Length != 10) throw new InvalidOperationException("Expected ten photovoltaic module silhouettes.");
                foreach (var module in modules) shapes.Add(Project(camera, Hotspot.Photovoltaics, module));
            }
            finally { camera.aspect = previousAspect; }
            Undo.RecordObject(diagram, "Calibrate equipment highlights");
            diagram.equipmentOutlines = shapes.ToArray();
            diagram.SetVerticesDirty();
            EditorUtility.SetDirty(diagram);
        }

        static MeshFilter[] Exact(MeshFilter[] meshes, params string[] names)
        {
            return names.Select(name =>
            {
                var matches = meshes.Where(value => value.name == name).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Expected one equipment mesh named " + name);
                return matches[0];
            }).ToArray();
        }

        static ReferenceHouseDiagram.EquipmentOutline Project(Camera camera, Hotspot product, IEnumerable<MeshFilter> meshes)
        {
            var points = new List<Vector2>();
            foreach (var filter in meshes)
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    var point = camera.WorldToViewportPoint(filter.transform.TransformPoint(vertex));
                    if (point.z > 0) points.Add(new Vector2(point.x * ReferenceHouseLedWall.Width,
                        (1f - point.y) * ReferenceHouseLedWall.Height));
                }
            var hull = ConvexHull(points);
            if (hull.Length < 3) throw new InvalidOperationException("Equipment has no visible silhouette: " + product);
            return new ReferenceHouseDiagram.EquipmentOutline { product = product, points = hull };
        }

        static Vector2[] ConvexHull(List<Vector2> points)
        {
            var sorted = points.Distinct().OrderBy(point => point.x).ThenBy(point => point.y).ToArray();
            if (sorted.Length < 3) return sorted;
            var hull = new List<Vector2>();
            foreach (var point in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lower = hull.Count;
            for (int i = sorted.Length - 2; i >= 0; i--)
            {
                var point = sorted[i];
                while (hull.Count > lower && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull.ToArray();
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
