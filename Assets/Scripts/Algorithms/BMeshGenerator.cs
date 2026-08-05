using System.Collections.Generic;
using GK;
using UnityEngine;

public static class BMeshGenerator
{
    private const int VERTICES_PER_NODE = 4;
    private const int BASE_VERTEX_OFFSET = 4;

    public struct MeshData
    {
        public List<Vector3> vertices;
        public List<int> triangles;

        public MeshData(List<Vector3> verts, List<int> tris)
        {
            vertices = verts;
            triangles = tris;
        }
    }

    public static MeshData GenerateMeshData(List<Node> nodes, Transform meshTransform)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        GenerateBasicMesh(nodes, meshTransform, vertices, triangles);
        GenerateMultipleMesh(nodes, vertices, triangles);

        return new MeshData(vertices, triangles);
    }

    private static void GenerateBasicMesh(List<Node> nodes, Transform meshTransform, List<Vector3> vertices, List<int> triangles)
    {
        foreach (Node n in nodes)
        {
            n.vind = vertices.Count;

            foreach (Vector3 v in n.vpos)
            {
                vertices.Add(meshTransform.InverseTransformVector(v));
            }

            if (n.isMultiple())
            {
                continue;
            }

            // Faces of himself
            foreach (int t in n.GetTriangles())
            {
                triangles.Add(t + n.vind);
            }

            if (!n.isChildMultiple() && n.transform.childCount != 0)
            {
                // Faces from parents nodes vertices to himself
                foreach (int t in n.GetTriangles())
                {
                    triangles.Add((t + n.vind) + BASE_VERTEX_OFFSET);
                }
            }

            if (n.transform.childCount == 0) // If top child
            {
                // Bottom cap face, closing off the "between nodes" vertex ring (vind+4..+7)
                triangles.Add(n.vind + 5);
                triangles.Add(n.vind + 6);
                triangles.Add(n.vind + 7);
                triangles.Add(n.vind + 5);
                triangles.Add(n.vind + 7);
                triangles.Add(n.vind + 4);
            }

            Node np = n.transform.parent.GetComponent<Node>();
            if (np == null && !n.isMultiple()) // If top parent
            {
                // Top cap face, closing off the "self" vertex ring (vind+0..+3)
                triangles.Add(n.vind + 1);
                triangles.Add(n.vind + 0);
                triangles.Add(n.vind + 3);
                triangles.Add(n.vind + 1);
                triangles.Add(n.vind + 3);
                triangles.Add(n.vind + 2);
            }
        }
    }

    private static void GenerateMultipleMesh(List<Node> nodes, List<Vector3> vertices, List<int> triangles)
    {
        foreach (Node n in nodes)
        {
            if (!n.isMultiple())
                continue;

            List<int> vind = new List<int>();
            List<Vector3> points = new List<Vector3>();
            Dictionary<Vector3, int> map = new Dictionary<Vector3, int>();

            int subCount = 0;
            foreach (Transform t in n.transform) // For each child
            {
                Node nc = t.GetComponent<Node>();
                if (nc != null)
                {
                    var points2 = new List<Vector3>();
                    Dictionary<Vector3, int> map2 = new Dictionary<Vector3, int>();

                    for (int j = 0; j < VERTICES_PER_NODE; j++)
                    {
                        int val1 = n.vind + j + VERTICES_PER_NODE * subCount;
                        int val2 = nc.vind + j;
                        points2.Add(vertices[val1]); // Add current vertices
                        map2[vertices[val1]] = val1; // indexer, not Add: two indices can coincide at the same position (e.g. right after a node was interactively added/edited), which would otherwise throw

                        points2.Add(vertices[val2]); // Add next vertices
                        map2[vertices[val2]] = val2;
                    }

                    TryAddHullTriangles(points2, map2, triangles);
                    subCount++;
                }
            }

            // We add vertices of parent
            if (n.HasParent())
            {
                Node np = n.transform.parent.GetComponent<Node>();
                int ind = np.GetChildIndice(n.transform);

                if (np.isMultiple())
                {
                    for (int j = 0; j < VERTICES_PER_NODE; j++)
                        vind.Add(np.vind + j + VERTICES_PER_NODE * ind);
                }
                else
                {
                    for (int j = 0; j < VERTICES_PER_NODE; j++)
                        vind.Add(np.vind + j + BASE_VERTEX_OFFSET);
                }
            }

            // We add previous and current vertices
            for (int i = n.vind; i < n.vind + n.vpos.Count; i++)
            {
                if (i < 0 || i >= vertices.Count)
                    continue;
                vind.Add(i);
            }

            foreach (int i in vind)
            {
                map[vertices[i]] = i; // indexer, not Add: see the map2 comment above
                points.Add(vertices[i]);
            }

            TryAddHullTriangles(points, map, triangles);
        }
    }

    // Shared by both hull sites above: ConvexHullCalculator throws for
    // degenerate (e.g. coplanar) point sets, which can happen right after a
    // node is interactively added/moved into a flat arrangement -- skip just
    // that junction's triangles rather than crashing the whole mesh generation.
    private static void TryAddHullTriangles(List<Vector3> points, Dictionary<Vector3, int> map, List<int> triangles)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        var normals = new List<Vector3>();

        try
        {
            var calc = new ConvexHullCalculator();
            calc.GenerateHull(points, false, ref verts, ref tris, ref normals);
            foreach (int i in tris)
            {
                triangles.Add(map[verts[i]]);
            }
        }
        catch (System.ArgumentException e)
        {
            Debug.LogWarning($"BMeshGenerator: skipped a degenerate junction hull ({e.Message})");
        }
    }

    public static void InvertTriangles(List<int> triangles)
    {
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int temp = triangles[i + 2];
            triangles[i + 2] = triangles[i];
            triangles[i] = temp;
        }
    }

}