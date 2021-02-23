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

    //vertex indice of the end of the last node
    public int vind = 0;

    //------ Editor

    public bool isChildMultiple()
    {
        if (transform.childCount == 0)
        {
            return true;
        }
        Node n = transform.GetChild(0).GetComponent<Node>();
        if (n == null)
            return true;

        return n.isMultiple();
    }

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
        }else if (bmesh.showMode == BMesh.ShowMode.Vertices)
        {
            DrawVerts();
        }
        //DrawVerts();
    }

    void OnDrawGizmosSelected()
    {
        if (Selection.activeTransform.gameObject != this.gameObject)
        {
            return;
        }
        Gizmos.color = new Color(1, 1, 0, 0.75F);
        foreach (Vector3 v in vpos)
        {
            Gizmos.DrawSphere(v, 0.1f);
        }
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

        //vertices between two nodes
        if (childNodes.Count == 0)//if end node
        {
            v1 = Vector3.ProjectOnPlane(left, diff) * size * 0.5f + transform.position;
            v2 = Vector3.ProjectOnPlane(-left, diff) * size * 0.5f + transform.position;
            v3 = Vector3.ProjectOnPlane(top, diff) * size * 0.5f + transform.position;
            v4 = Vector3.ProjectOnPlane(-top, diff) * size * 0.5f + transform.position;

            vpos.Add(v1 + (diff * 0.2f));
            vpos.Add(v3 + (diff * 0.2f));
            vpos.Add(v2 + (diff * 0.2f));
            vpos.Add(v4 + (diff * 0.2f));
        }else
        {
            vpos.Add(v1 + (diff * 0.5f));
            vpos.Add(v3 + (diff * 0.5f));
            vpos.Add(v2 + (diff * 0.5f));
            vpos.Add(v4 + (diff * 0.5f));
        }


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
