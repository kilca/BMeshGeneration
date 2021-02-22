using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

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

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
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
                Debug.Log(n.gameObject.name);
                //faces from parents nodes vertices to himself
                foreach (int t in n.GetTriangles())
                {
                    //Debug.Log((t + n.vind) + 4);
                    triangles.Add((t + n.vind) + 4);
                }
            }

        }
        Debug.Log("taille vertices : "+vertices.Count);
        //invertTriangles(triangles);
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.Optimize();
        mesh.RecalculateNormals();

    }

    void Update()
    {
        GenerateNodes();
        GenerateMesh();

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
