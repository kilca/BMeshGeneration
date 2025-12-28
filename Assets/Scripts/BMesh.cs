using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using GK;
using Torec;

[CustomEditor(typeof (BMesh))]
public class BMeshEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        BMesh bm = (BMesh)target;
        
        if (GUILayout.Button("Generate"))
        {
            bm.Generate();
        }
        
    }
}

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
            n.Generer();
        }
    }

    public void Generate()
    {
        GenerateNodes();

        // Use the utility class to generate mesh data
        BMeshGenerator.MeshData meshData = BMeshGenerator.GenerateMeshData(nodes, transform);
        vertices = meshData.vertices;
        triangles = meshData.triangles;

        Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles.ToArray(), 0);

        // Catumull subdivision doesn't seem to work
        if (!updateRealTime)
        {
            MeshHelper.Subdivide(mesh, subdivideIter);
            MeshUtils.SmoothMesh(mesh, smoothIter);
        }

        mesh.Optimize();
        mesh.RecalculateNormals();
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
