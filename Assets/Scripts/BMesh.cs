using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GK;
using Torec;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class BMesh : MonoBehaviour
{

    private List<Node> nodes = new();

    public enum ShowMode {Gizmo,Mesh,Vertices,Wireframe}

    public ShowMode showMode;

    public bool updateRealTime = false;

    private List<Vector3> vertices;
    List<int> triangles;

    [Header("Best : 2,2 / Subd does not app on realTime")]

    [Range(0,4)]
    public int subdivideIter;

    [Range(0, 4)]
    public int smoothIter;

    [Header("References")]

    public Material normalMaterial;
    public Material wireframeMaterial;

    public void GenerateNodes()
    {
        nodes = new List<Node>(GetComponentsInChildren<Node>());
        foreach (Node n in nodes)
        {
            n.UpdateChilds();
            n.Generate();
        }
    }

    public void Generate()
    {
        GenerateNodes();

        // Use the utility class to generate mesh data
        BMeshGenerator.MeshData meshData = BMeshGenerator.GenerateMeshData(nodes, transform);
        vertices = meshData.vertices;
        triangles = meshData.triangles;

        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter.sharedMesh == null)
        {
            meshFilter.sharedMesh = new Mesh();
        }
        Mesh mesh = meshFilter.sharedMesh;
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles.ToArray(), 0);
        BakeRestPositionUVs(mesh, vertices);

        // Catmull-Clark subdivision doesn't seem to work
        if (!updateRealTime)
        {
            MeshHelper.Subdivide(mesh, subdivideIter);
            MeshUtils.SmoothMesh(mesh, smoothIter);
        }

        mesh.Optimize();
        mesh.RecalculateNormals();
    }

    // Bakes each vertex's own (pre-subdivision, pre-skinning) local position
    // into spare UV channels, split across two Vector2 channels since the
    // legacy uv2/uv3 accessors used here (and by MeshHelper.Subdivide, which
    // already knows to interpolate them alongside the real vertices when the
    // mesh is subdivided) only carry 2 components each. Skinning
    // (BMeshBoneExtensions.CreateSkeleton) only touches vertex
    // positions/bone weights, never UVs, so this rest-pose position survives
    // untouched into the animated skinned mesh -- letting
    // Custom/TriplanarCreature sample from a position that doesn't slide
    // around as the creature animates, instead of the live (moving) worldPos.
    private static void BakeRestPositionUVs(Mesh mesh, List<Vector3> verts)
    {
        Vector2[] uv2 = new Vector2[verts.Count];
        Vector2[] uv3 = new Vector2[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            uv2[i] = new Vector2(verts[i].x, verts[i].y);
            uv3[i] = new Vector2(verts[i].z, 0f);
        }
        mesh.uv2 = uv2;
        mesh.uv3 = uv3;
    }

    void Update()
    {
        if (updateRealTime)
            Generate();

        MeshRenderer meshR = GetComponent<MeshRenderer>();
        switch (showMode)
        {
            case ShowMode.Gizmo:
                meshR.enabled = false;
                break;
            case ShowMode.Mesh:
                meshR.enabled = true;
                meshR.material = normalMaterial;
                break;
            case ShowMode.Vertices:
                meshR.enabled = false;
                break;
            case ShowMode.Wireframe:
                meshR.enabled = true;
                meshR.material = wireframeMaterial;
                break;
        }

    }
}
