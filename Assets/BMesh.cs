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

    private List<Node> nodes = new List<Node>();

    public enum ShowMode {Gizmo,Mesh}

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
        int i = 0;
        foreach (Node n in nodes)
        {
            foreach(Vector3 v in n.vpos)
            {
                vertices.Add(transform.InverseTransformVector(v));
            }
            foreach(int t in n.GetTriangles())
            {
                //Debug.Log("A :"+(t + (8 * i)));
                triangles.Add(t + (8 * i));
            }
             
            if (n.HasParent())
            {
                foreach (int t in n.GetTriangles())
                {
                    //Debug.Log("B:"+ ((t + (8 * i)) - 4));
                    triangles.Add((t + (8 * i))-4);
                }
            }
            i++;

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
        }

    }
}
