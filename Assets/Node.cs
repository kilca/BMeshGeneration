using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[ExecuteInEditMode]
public class Node : MonoBehaviour
{
    public List<Node> childNodes = new List<Node>();

    [Range(0,4)]
    public float size = 1;

    public List<Vector3> vpos = new List<Vector3>();

    private BMesh bmesh;

    public int ind = -1;

    //------ Editor

    public bool HasParent()
    {
        return (transform.parent.GetComponent<Node>() != null);
    }

    void Update()
    {
        bmesh = GetComponentInParent<BMesh>();
    }

    void DrawSphere()
    {
        if (childNodes.Count == 1)
            Gizmos.color = Color.blue;
        else if (childNodes.Count == 0)
            Gizmos.color = Color.yellow;
        else//si > 1
            Gizmos.color = Color.red;

        Gizmos.DrawSphere(this.transform.position, size);
    }

    void DrawVerts()
    {
        Gizmos.color = Color.green;
        int i = 0;
        foreach (Vector3 v in vpos)
        {
            Handles.Label(v, ""+i);
            i++;
            //Gizmos.DrawWireSphere(v, 0.5f);
        }
    }

    void DrawLines()
    {
        Gizmos.color = Color.white;
        foreach (Transform t in transform)
        {
            if (t.GetComponent<Node>() == null)
                continue;
            Gizmos.DrawLine(transform.position, t.position);
        }
    }

    void OnDrawGizmos()
    {
        if (bmesh.showMode == BMesh.ShowMode.Gizmo)
        {
            DrawSphere();
            DrawLines();
        }
        //DrawVerts();
    }

    //------ Generation

    public bool isMultiple()
    {
        UpdateChilds();
        return childNodes.Count > 1;
    }

    public int[] GetTriangles()
    {
        int[] triangles = {
            /*
            0, 2, 1, //face front
			0, 3, 2,
            */
            3, 7, 6, //face top
			3, 6, 2,

            2, 6, 5, //face right
			2, 5, 1,

            0, 4, 7, //face left
			0, 7, 3,
            /*
            5, 4, 7, //face back
			5, 7, 6,
            */
            1, 5, 4, //face bottom
			1, 4, 0
        };

        return triangles;

    }

    public void Generate1Node(Transform t)
    {
        Vector3 diff;
        if (childNodes.Count > 0)
        {
            diff = t.position - transform.position;
        }
        else if (HasParent())
        {
            diff = transform.position - transform.parent.position;
        }
        else
        {
            diff = transform.position;
        }

        Vector3 dir = diff;
        Vector3 left = Vector3.Cross(dir, Vector3.up).normalized;
        Vector3 top = Vector3.Cross(dir, left).normalized;

        Vector3 v1 = Vector3.ProjectOnPlane(left, diff) * size + transform.position;
        Vector3 v2 = Vector3.ProjectOnPlane(-left, diff) * size + transform.position;
        Vector3 v3 = Vector3.ProjectOnPlane(top, diff) * size + transform.position;
        Vector3 v4 = Vector3.ProjectOnPlane(-top, diff) * size + transform.position;

        if (!isMultiple())
        {
            vpos.Add(v1);
            vpos.Add(v3);
            vpos.Add(v2);
            vpos.Add(v4);
        }
        //4 2 3 1
        vpos.Add(v1 + (diff * 0.5f));
        vpos.Add(v3 + (diff * 0.5f));
        vpos.Add(v2 + (diff * 0.5f));
        vpos.Add(v4 + (diff * 0.5f));


    }

    public void Generer()
    {
        vpos = new List<Vector3>();
        if (childNodes.Count > 0)
        {
            foreach (Node n in childNodes)
            {
                Generate1Node(n.transform);
            }
        }
        else if (childNodes.Count == 0)
        {
            Generate1Node(transform);
        }
    }

    public void UpdateChilds()
    {
        childNodes = new List<Node>();
        foreach (Transform t in transform)
        {
            Node n = t.GetComponent<Node>();
            if (n != null)
            {
                childNodes.Add(n);
            }
        }

    }

}
