using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using GK;

[CustomEditor(typeof (BMesh))]
public class BMeshEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        BMesh bm = (BMesh)target;

        if (GUILayout.Button("Generate"))
        {
           
        }
    }
}

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class BMesh : MonoBehaviour
{

    public List<Node> nodes = new List<Node>();

    public enum ShowMode {Gizmo,Mesh,Vertices}

    public ShowMode showMode;

    private List<Vector3> vertices;
    List<int> triangles;

    public void GenerateNodes()
    {
        nodes = new List<Node>(GetComponentsInChildren<Node>());
        foreach (Node n in nodes)
        {
            n.UpdateChilds();
            n.Generer();
        }
    }

    private void invertTriangles(List<int> triangles)
    {
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int temp = triangles[i + 2];
            triangles[i + 2] = triangles[i];
            triangles[i] = temp;
        }
    }

    void GenerateMesh()
    {

        vertices = new List<Vector3>();
        triangles = new List<int>();
        foreach (Node n in nodes)
        {

            n.vind = vertices.Count;

            foreach (Vector3 v in n.vpos)
            {
                vertices.Add(transform.InverseTransformVector(v));
            }

            if (n.isMultiple())
            {
                continue;
            }

            //faces of himself
            foreach (int t in n.GetTriangles())
            {
                //Debug.Log(n.gameObject.name);
                triangles.Add(t + n.vind);
            }

            if (!n.isChildMultiple() && n.transform.childCount != 0)
            {
                //faces from parents nodes vertices to himself
                foreach (int t in n.GetTriangles())
                {
                    triangles.Add((t + n.vind) + 4);
                }
            }

        }


    }

    void GenerateMultipleMesh()
    {
        //indice of vertices near multiple
        List<int> vind = new List<int>();
        List<Vector3> points = new List<Vector3>();

        var verts = new List<Vector3>();
        var tris = new List<int>();
        var normals = new List<Vector3>();

        foreach (Node n in nodes)
        {
            var calc = new ConvexHullCalculator();
            if (!n.isMultiple())
                continue;

            //We add previous and current vertices
            for(int i = n.vind - 4; i < n.vind + n.vpos.Count; i++)
            {
                vind.Add(i);
            }

            //we add firsts child vertices
            foreach (Transform t in n.transform)
            {
                Node nc = t.GetComponent<Node>();
                if (nc != null)
                {
                    for(int i = nc.vind; i < nc.vind + 4; i++)
                    {
                        vind.Add(i);
                    }
                }
            }

            foreach(int i in vind)
            {
                points.Add(vertices[i]);
            }

            
            calc.GenerateHull(points, false, ref verts, ref tris, ref normals);

            List<int> pointsToVerts = new List<int>();
            foreach (Vector3 v in verts)
            {
                pointsToVerts.Add(vertices.IndexOf(v));
            }

            foreach (int i in tris)
            {
                triangles.Add(pointsToVerts[i]);
            }
            
        }
    }

    void Update()
    {
        GenerateNodes();
        GenerateMesh();
        GenerateMultipleMesh();

        Mesh mesh = GetComponent<MeshFilter>().mesh;
        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.Optimize();
        mesh.RecalculateNormals();


        switch (showMode)
        {
            case ShowMode.Gizmo:
                GetComponent<MeshRenderer>().enabled = false;
                break;
            case ShowMode.Mesh:
                GetComponent<MeshRenderer>().enabled = true;
                break;
            case ShowMode.Vertices:
                GetComponent<MeshRenderer>().enabled = false;
                break;
        }

    }
}
