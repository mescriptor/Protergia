using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ReferenceHouseTools
{
    // One authored door design, with the existing scene instance overrides and
    // mesh GUIDs left intact. The wider exterior opening receives plaster infill.
    public static class ReferenceHouseDoorConsistency
    {
        const string Root = "Assets/ReferenceHouse";
        const string Master = "Upper central hall door facing the section";
        static readonly string[] Doors = {
            Master, "Living room rear doorway", "Kitchen rear hall door", "Right side entrance"
        };
        static readonly string[] Parts = {
            "architrave", "architrave.001", "architrave head", "door leaf",
            "lock plate", "lever", "inner lock plate", "inner lever", "front spindle", "back spindle"
        };

        [Serializable] class Report
        {
            public string master;
            public int doors;
            public Vector3 canonicalLeafSize;
            public string[] meshPaths, materialPaths, addedObjects;
            public bool preservedTransforms, identicalLeafDimensions;
        }

        struct DoorFrame
        {
            public Vector3 Origin;
            public Quaternion Rotation;
            public Vector3 ToCanonical(Vector3 p) => Quaternion.Inverse(Rotation) * (p - Origin);
            public Vector3 FromCanonical(Vector3 p) => Origin + Rotation * p;
        }

        [MenuItem("Tools/Reference House/Unify All Architectural Doors")]
        public static void ApplyToPrefab()
        {
            var house = PrefabUtility.LoadPrefabContents(Root + "/Prefabs/ReferenceHouse.prefab");
            var changed = new HashSet<string>();
            var added = new List<string>();
            try
            {
                var objects = house.GetComponentsInChildren<MeshFilter>(true).ToDictionary(o => o.name);
                var before = objects.ToDictionary(k => k.Key, k => k.Value.transform.localToWorldMatrix);
                var frames = Doors.ToDictionary(d => d, d => Frame(objects, d));
                var masterFrame = frames[Master];
                var masterParts = Parts.ToDictionary(p => p, p => UnityEngine.Object.Instantiate(objects[Master + " / " + p].sharedMesh));
                var wood = DoorMaterial("Door___uniform_natural_oak", "Natural_oak_doors_and_stair_treads");
                wood.name = "Door / uniform natural oak";
                var tint = new Color32(156, 126, 90, 255);
                wood.SetColor("_BaseColor", tint);
                wood.SetColor("_Color", tint);
                wood.SetFloat("_Smoothness", .38f);
                wood.SetFloat("_BumpScale", .10f);
                wood.SetTextureScale("_BaseMap", new Vector2(1.2f, 1.2f));
                EditorUtility.SetDirty(wood);
                var metal = DoorMaterial("Door___uniform_satin_hardware", "Anthracite_aluminium");
                metal.name = "Door / uniform satin hardware";
                metal.SetColor("_BaseColor", new Color32(57, 55, 49, 255));
                metal.SetColor("_Color", new Color32(57, 55, 49, 255));
                metal.SetFloat("_Metallic", .65f);
                metal.SetFloat("_Smoothness", .42f);
                EditorUtility.SetDirty(metal);

                var masterToWorld = Parts.ToDictionary(p => p, p => objects[Master + " / " + p].transform.localToWorldMatrix);
                var standardBounds = CanonicalBounds(objects[Master + " / door leaf"], masterFrame);
                var masterLeft = CanonicalBounds(objects[Master + " / architrave"], masterFrame);
                var masterRight = CanonicalBounds(objects[Master + " / architrave.001"], masterFrame);
                var masterHead = CanonicalBounds(objects[Master + " / architrave head"], masterFrame);

                foreach (var door in Doors)
                {
                    var frame = frames[door];
                    var oldLeft = CanonicalBounds(objects[door + " / architrave"], frame);
                    var oldRight = CanonicalBounds(objects[door + " / architrave.001"], frame);
                    var oldHead = CanonicalBounds(objects[door + " / architrave head"], frame);
                    foreach (var part in Parts)
                    {
                        var filter = objects[door + " / " + part];
                        var replacement = UnityEngine.Object.Instantiate(masterParts[part]);
                        var sourceToCanonical = Matrix4x4.TRS(masterFrame.Origin, masterFrame.Rotation, Vector3.one).inverse * masterToWorld[part];
                        var canonicalToTarget = filter.transform.worldToLocalMatrix * Matrix4x4.TRS(frame.Origin, frame.Rotation, Vector3.one);
                        var matrix = canonicalToTarget * sourceToCanonical;
                        replacement.vertices = replacement.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                        replacement.normals = replacement.normals.Select(n => matrix.inverse.transpose.MultiplyVector(n).normalized).ToArray();
                        // Identical relative UVs keep grain direction and repeat phase
                        // the same on every leaf, independent of room position.
                        var uv = replacement.uv;
                        var vertices = replacement.vertices;
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            Vector3 p = frame.ToCanonical(filter.transform.TransformPoint(vertices[i]));
                            uv[i] = new Vector2(p.x * 8f, p.y * 8f);
                        }
                        replacement.uv = uv;
                        replacement.RecalculateBounds();
                        replacement.RecalculateTangents();
                        replacement.name = filter.sharedMesh.name;
                        EditorUtility.CopySerialized(replacement, filter.sharedMesh);
                        UnityEngine.Object.DestroyImmediate(replacement);
                        EditorUtility.SetDirty(filter.sharedMesh);
                        changed.Add(AssetDatabase.GetAssetPath(filter.sharedMesh));
                        var renderer = filter.GetComponent<MeshRenderer>();
                        renderer.sharedMaterials = Enumerable.Repeat(part.Contains("architrave") || part == "door leaf" ? wood : metal, filter.sharedMesh.subMeshCount).ToArray();
                        renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                        var collider = filter.GetComponent<MeshCollider>();
                        if (collider) { collider.sharedMesh = null; collider.sharedMesh = filter.sharedMesh; }
                    }

                    // Only the entrance was deliberately larger. Close that spare
                    // reveal with matching plaster, behind the standard oak frame.
                    if (door == "Right side entrance")
                    {
                        var parent = objects[door + " / door leaf"].transform.parent;
                        float back = oldLeft.max.z, front = oldLeft.min.z;
                        AddInfill(parent, door + " / standard door left reveal", frame,
                            new Vector3(oldLeft.min.x, 0, front), new Vector3(masterLeft.min.x, oldHead.max.y, back), changed, added);
                        AddInfill(parent, door + " / standard door right reveal", frame,
                            new Vector3(masterRight.max.x, 0, front), new Vector3(oldRight.max.x, oldHead.max.y, back), changed, added);
                        AddInfill(parent, door + " / standard door upper reveal", frame,
                            new Vector3(masterLeft.min.x, masterHead.max.y, front), new Vector3(masterRight.max.x, oldHead.max.y, back), changed, added);
                    }
                }
                foreach (var mesh in masterParts.Values) UnityEngine.Object.DestroyImmediate(mesh);
                foreach (var pair in before)
                    if (objects[pair.Key].transform.localToWorldMatrix != pair.Value)
                        throw new InvalidOperationException("Door update changed a transform: " + pair.Key);
                foreach (var door in Doors)
                    if ((CanonicalBounds(objects[door + " / door leaf"], frames[door]).size - standardBounds.size).sqrMagnitude > 1e-10f)
                        throw new InvalidOperationException("Door dimensions diverged: " + door);
                PrefabUtility.SaveAsPrefabAsset(house, Root + "/Prefabs/ReferenceHouse.prefab", out bool saved);
                if (!saved) throw new IOException("Could not save the standardized doors.");
                AssetDatabase.SaveAssets();
                var report = new Report {
                    master = Master, doors = Doors.Length, canonicalLeafSize = standardBounds.size,
                    meshPaths = changed.OrderBy(p => p).ToArray(),
                    materialPaths = new[] { AssetDatabase.GetAssetPath(wood), AssetDatabase.GetAssetPath(metal) },
                    addedObjects = added.ToArray(), preservedTransforms = true, identicalLeafDimensions = true
                };
                string json = JsonUtility.ToJson(report, true);
                File.WriteAllText("C:/Protergia/Blender/ReferenceHouse/Unity_DoorsV8.json", json);
                Debug.Log("HOUSE_DOORS_STANDARDIZED " + json);
            }
            finally { PrefabUtility.UnloadPrefabContents(house); }
        }

        static Material DoorMaterial(string name, string original)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/" + original + ".mat"));
                AssetDatabase.CreateAsset(material, path);
            }
            return material;
        }

        static DoorFrame Frame(Dictionary<string, MeshFilter> objects, string door)
        {
            var leaf = objects[door + " / door leaf"].GetComponent<Renderer>().bounds;
            var left = objects[door + " / architrave"].GetComponent<Renderer>().bounds;
            var right = objects[door + " / architrave.001"].GetComponent<Renderer>().bounds;
            return new DoorFrame {
                Origin = new Vector3(leaf.center.x, Mathf.Min(left.min.y, right.min.y), leaf.center.z),
                Rotation = door == "Right side entrance" ? Quaternion.Euler(0, -90, 0) : Quaternion.identity
            };
        }

        static Bounds CanonicalBounds(MeshFilter filter, DoorFrame frame)
        {
            var vertices = filter.sharedMesh.vertices;
            var bounds = new Bounds(frame.ToCanonical(filter.transform.TransformPoint(vertices[0])), Vector3.zero);
            foreach (var vertex in vertices) bounds.Encapsulate(frame.ToCanonical(filter.transform.TransformPoint(vertex)));
            return bounds;
        }

        static void AddInfill(Transform parent, string name, DoorFrame frame, Vector3 min, Vector3 max, HashSet<string> paths, List<string> added)
        {
            // No-op on subsequent applications: the already standardized frame
            // no longer has excess reveal, and the saved infill remains in place.
            if (max.x - min.x < .00001f || max.y - min.y < .00001f || max.z - min.z < .00001f) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = frame.FromCanonical((min + max) * .5f);
            go.transform.rotation = frame.Rotation;
            var size = max - min;
            var mesh = UnityEngine.Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = name;
            mesh.vertices = mesh.vertices.Select(v => Vector3.Scale(v, size)).ToArray();
            mesh.RecalculateBounds();
            string path = Root + "/Meshes/DoorInfill_" + name.Replace(" / ", "_").Replace(" ", "_") + ".asset";
            AssetDatabase.CreateAsset(mesh, path);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Warm_ivory_mineral_plaster.mat");
            renderer.receiveShadows = true;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.ContributeGI);
            paths.Add(path);
            added.Add(name);
        }
    }
}
