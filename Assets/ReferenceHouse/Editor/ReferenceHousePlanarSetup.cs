using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ReferenceHouseTools
{
    public static class ReferenceHousePlanarSetup
    {
        const string Root = "Assets/ReferenceHouse";
        const string OverlayRoot = "Presentation | polished floor reflections";
        public static void Apply()
        {
            if (SceneManager.GetActiveScene().path != Root + "/Scenes/ReferenceHouse.unity")
                throw new InvalidOperationException("Open ReferenceHouse before preparing its planar floor reflection.");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Settings/ReferenceHousePlanarFloor.shader");
            if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Planar floor shader is unavailable.");
            string materialPath = Root + "/Materials/PolishedFloor_PlanarReflection.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
            material.shader = shader;
            material.SetFloat("_BaseOpacity", .13f);
            material.SetFloat("_GrazingOpacity", .30f);
            material.SetFloat("_ReflectionMip", 1.5f);
            EditorUtility.SetDirty(material);
            var holder = GameObject.Find(OverlayRoot);
            if (!holder) holder = new GameObject(OverlayRoot);
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            holder.transform.localScale = Vector3.one;
            holder.layer = 29;
            var reflection = holder.GetComponent<ReferenceHousePlanarReflection>();
            if (!reflection) reflection = holder.AddComponent<ReferenceHousePlanarReflection>();
            reflection.floorHeight = .65f;
            reflection.resolution = 1536;
            EditorUtility.SetDirty(reflection);
            var floors = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.gameObject.scene == SceneManager.GetActiveScene() && r.name.StartsWith("Ground porcelain floor / tone ", StringComparison.Ordinal)).ToArray();
            if (floors.Length != 5) throw new InvalidOperationException("Expected five polished floor meshes.");
            int triangleCount = 0;
            foreach (var floor in floors)
            {
                string tone = floor.name.Substring(floor.name.Length - 1);
                string name = "Presentation | polished floor reflection tone " + tone;
                Transform existing = holder.transform.Find(name);
                var overlay = existing ? existing.gameObject : new GameObject(name);
                overlay.transform.SetParent(holder.transform, false);
                overlay.transform.localPosition = Vector3.zero;
                overlay.transform.localRotation = Quaternion.identity;
                overlay.transform.localScale = Vector3.one;
                overlay.layer = 29;
                var source = floor.GetComponent<MeshFilter>();
                var positions = source.sharedMesh.vertices.Select(source.transform.TransformPoint).ToArray();
                var indices = source.sharedMesh.triangles;
                float top = floor.bounds.max.y;
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                for (int t = 0; t < indices.Length; t += 3)
                {
                    Vector3 a = positions[indices[t]], b = positions[indices[t+1]], c = positions[indices[t+2]];
                    if (a.y < top-.001f || b.y < top-.001f || c.y < top-.001f) continue;
                    Vector3 normal = Vector3.Cross(b-a, c-a).normalized;
                    if (normal.y < .995f) continue;
                    int start = vertices.Count;
                    vertices.Add(a + Vector3.up * .0025f);
                    vertices.Add(b + Vector3.up * .0025f);
                    vertices.Add(c + Vector3.up * .0025f);
                    triangles.Add(start); triangles.Add(start+1); triangles.Add(start+2);
                }
                if (triangles.Count == 0) throw new InvalidOperationException("No upward floor faces found for " + floor.name);
                string meshPath = Root + "/Meshes/PlanarFloor_Tone_" + tone + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (!mesh) { mesh = new Mesh { name = "Polished floor reflection tone " + tone }; AssetDatabase.CreateAsset(mesh, meshPath); }
                mesh.Clear();
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds(); mesh.RecalculateNormals();
                EditorUtility.SetDirty(mesh);
                var filter = overlay.GetComponent<MeshFilter>();
                if (!filter) filter = overlay.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;
                var renderer = overlay.GetComponent<MeshRenderer>();
                if (!renderer) renderer = overlay.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                GameObjectUtility.SetStaticEditorFlags(overlay, 0);
                triangleCount += triangles.Count / 3;
            }
            foreach (var probe in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None))
            { probe.cullingMask &= ~(1 << 29); EditorUtility.SetDirty(probe); }
            Debug.Log($"HOUSE_PLANAR_FLOOR_READY overlays={floors.Length} triangles={triangleCount} height={reflection.floorHeight}");
        }
    }
}
