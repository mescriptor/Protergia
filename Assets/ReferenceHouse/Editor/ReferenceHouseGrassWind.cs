using System.Collections.Generic;
using UnityEngine;

namespace ReferenceHouseTools
{
    internal static class ReferenceHouseGrassWind
    {
        // The Blender export splits vertices at face/UV seams. Join triangles
        // by position so each blade gets its own root, even on sloping ground.
        internal static void SetBladeWeights(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var parent = new int[vertices.Length];
            var firstAt = new Dictionary<Vector3, int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!firstAt.TryGetValue(vertices[i], out int first))
                {
                    first = i;
                    firstAt.Add(vertices[i], first);
                }
                parent[i] = first;
            }

            int Root(int i)
            {
                while (i != parent[i])
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int root = Root(triangles[i]);
                parent[Root(triangles[i + 1])] = root;
                parent[Root(triangles[i + 2])] = root;
            }

            var heights = new Dictionary<int, Vector2>();
            for (int i = 0; i < vertices.Length; i++)
            {
                int root = Root(i);
                float y = vertices[i].y;
                heights[root] = heights.TryGetValue(root, out var span)
                    ? new Vector2(Mathf.Min(span.x, y), Mathf.Max(span.y, y))
                    : new Vector2(y, y);
            }

            var colors = new Color32[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var span = heights[Root(i)];
                float weight = Mathf.InverseLerp(span.x, span.y, vertices[i].y);
                colors[i] = new Color32((byte)Mathf.RoundToInt(weight * 255f), 255, 255, 255);
            }
            mesh.colors32 = colors;
        }
    }
}
