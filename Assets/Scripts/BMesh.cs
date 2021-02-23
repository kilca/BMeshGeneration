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
        /*
        if (GUILayout.Button("Generate"))
        {
           
        }
        */
    }
}

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class BMesh : MonoBehaviour
{

    private List<Node> nodes = new List<Node>();

    public enum ShowMode {Gizmo,Mesh,Vertices,Wireframe}

    public ShowMode showMode;

    [Header("References")]

    public Material normalMaterial;
    public Material wireframeMaterial;

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

            if (n.transform.childCount == 0)//if top child
            {
                triangles.Add(n.vind + 5);
                triangles.Add(n.vind + 6);
                triangles.Add(n.vind + 7);
                triangles.Add(n.vind + 5);
                triangles.Add(n.vind + 7);
                triangles.Add(n.vind + 4);
            }

            Node np = n.transform.parent.GetComponent<Node>();
            if (np == null && !n.isMultiple())//if top parent
            {
                triangles.Add(n.vind + 1);
                triangles.Add(n.vind + 0);
                triangles.Add(n.vind + 3);
                triangles.Add(n.vind + 1);
                triangles.Add(n.vind + 3);
                triangles.Add(n.vind + 2);
            }


        }


    }


    void GenerateMultipleMesh()
    {
        //indice of vertices near multiple

        foreach (Node n in nodes)
        {
            List<int> vind = new List<int>();
            List<Vector3> points = new List<Vector3>();

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var normals = new List<Vector3>();

            var calc = new ConvexHullCalculator();

            Dictionary<Vector3, int> map = new Dictionary<Vector3, int>(); 

            if (!n.isMultiple())
                continue;

            //We add previous and current vertices
            for(int i = n.vind - 4; i < n.vind + n.vpos.Count; i++)
            {
                if (i < 0 || i > vertices.Count)
                    continue;
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
            foreach (int i in vind)
            {
                map.Add(vertices[i], i);
                points.Add(vertices[i]);
            }

            
            calc.GenerateHull(points, false, ref verts, ref tris, ref normals);
            /*
            List<int> pointsToVerts = new List<int>();
            foreach (Vector3 v in verts)
            {
                pointsToVerts.Add(vertices.IndexOf(v));
            }
            */
            foreach (int i in tris)
            {
                triangles.Add(map[verts[i]]);
                //triangles.Add(pointsToVerts[i]);
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
