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

    private List<Node> nodes = new List<Node>();

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

    private void invertTriangles(List<int> triangles)
    {
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int temp = triangles[i + 2];
            triangles[i + 2] = triangles[i];
            triangles[i] = temp;
        }
    }
    /*
    void OnDrawGizmos()
    {

        Gizmos.color = Color.green;
        int i = 0;
        foreach (Vector3 v in vertices)
        {
            Handles.Label(v, ""+i);
            i++;
            //Gizmos.DrawWireSphere(v, 0.5f);
        }
    }
    */

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
            if (!n.isMultiple())
                continue;

            List<int> vind = new List<int>();
            List<Vector3> points = new List<Vector3>();

            var verts = new List<Vector3>();
            var tris = new List<int>();
            var normals = new List<Vector3>();

            var calc = new ConvexHullCalculator();

            Dictionary<Vector3, int> map = new Dictionary<Vector3, int>();

            int subCount = 0;
            foreach (Transform t in n.transform)//for each child
            {
                Node nc = t.GetComponent<Node>();
                if (nc != null)
                {
                    var points2 = new List<Vector3>();
                    var verts2 = new List<Vector3>();
                    var tris2 = new List<int>();
                    var norm2 = new List<Vector3>();
                    Dictionary<Vector3, int> map2 = new Dictionary<Vector3, int>();
                    for (int j = 0; j < 4; j++)
                    {
                        int val1 = n.vind + j + 4 * subCount;
                        int val2 = nc.vind + j;
                        points2.Add(vertices[val1]);//add current vertices
                        //if (!map2.ContainsKey(vertices[val1]))
                            map2.Add(vertices[val1], val1);

                        points2.Add(vertices[val2]);//add next vertices
                        //if (!map2.ContainsKey(vertices[val2]))
                            map2.Add(vertices[val2], val2);
                    }

                    var calc2 = new ConvexHullCalculator();
                    calc2.GenerateHull(points2, false, ref verts2, ref tris2, ref norm2);
                    foreach (int j in tris2)
                    {
                        //Debug.Log(j);
                        //if (!map2.ContainsKey(verts2[j]))
                            triangles.Add(map2[verts2[j]]);
                    }
                    subCount++;
                }
            }

            //We add vertices of parent
            if (n.HasParent())
            {
                Node np = n.transform.parent.GetComponent<Node>();
                int ind = np.GetChildIndice(n.transform);
                //Debug.Log(ind);
                if (np.isMultiple())
                {
                    for (int j = 0; j < 4; j++)
                        vind.Add(np.vind + j + 4 * ind);
                }
                else
                {
                    //Debug.Log("is multiple");
                    for (int j = 0; j < 4; j++)
                        vind.Add(np.vind + j + 4);
                }
            }

            //We add previous and current vertices
            for (int i = n.vind; i < n.vind + n.vpos.Count; i++)
            {
                if (i < 0 || i > vertices.Count)
                    continue;
                vind.Add(i);
            }
            //???Can be one for
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

    public void Generate()
    {
        GenerateNodes();
        GenerateMesh();
        GenerateMultipleMesh();

        Mesh mesh = GetComponent<MeshFilter>().mesh;
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles.ToArray(),0);

        //Catumull subdivision doesn't seem to work
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
