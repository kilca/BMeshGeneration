// ============================================================================
// Creature Skeleton Import/Export
// ============================================================================
// Round-trips the Node hierarchy that CreatureGenerator/ProcGen build (name,
// local position, size) as JSON, independent of BMesh's mesh/bone export.
// Unlike the mesh+bones export (MeshExportData), this is meant to be reloaded
// back into Unity as an editable Node tree -- to save a creature you like, or
// hand-tweak one and reuse it -- rather than to hand off to other 3D software.
//
// The node tree is stored FLAT (an array + parentIndex per entry, same shape as
// BoneData) rather than nested. A generated body chains every segment under the
// previous one, so a nested [Serializable] layout blows past Unity's
// serialization depth limit (~7) on anything but the simplest creature and
// JsonUtility.ToJson throws. The flat form has no depth at all.
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]
public class CreatureNodeData
{
    public string name;
    public Vector3 localPosition;
    public float size;
    public int parentIndex; // index into CreatureData.nodes; -1 for a root node
}

[System.Serializable]
public class CreatureData
{
    public string creatureName;
    public CreatureNodeData[] nodes = System.Array.Empty<CreatureNodeData>();
}

public static class CreatureIO
{
    // ------ Capture ------

    // Flat capture of every Node under creatureRoot. GetComponentsInChildren is
    // depth-first (ancestor before descendant), so each node's parent is already
    // indexed by the time we reach it.
    public static CreatureData CaptureCreature(GameObject creatureRoot, string creatureName)
    {
        List<CreatureNodeData> flat = new List<CreatureNodeData>();
        Dictionary<Node, int> indexOf = new Dictionary<Node, int>();

        foreach (Node node in creatureRoot.GetComponentsInChildren<Node>())
        {
            Node parentNode = node.transform.parent != null ? node.transform.parent.GetComponent<Node>() : null;
            int parentIndex = parentNode != null && indexOf.TryGetValue(parentNode, out int pi) ? pi : -1;

            indexOf[node] = flat.Count;
            flat.Add(NodeToData(node, parentIndex));
        }

        return new CreatureData { creatureName = creatureName, nodes = flat.ToArray() };
    }

    // Flat capture of a single subtree, `root` at index 0 (parentIndex -1) --
    // used by NodeEditController's per-subtree delete/undo.
    public static CreatureNodeData[] CaptureSubtree(Node root)
    {
        List<CreatureNodeData> flat = new List<CreatureNodeData>();
        CaptureSubtreeRecursive(root, -1, flat);
        return flat.ToArray();
    }

    private static void CaptureSubtreeRecursive(Node node, int parentIndex, List<CreatureNodeData> flat)
    {
        int myIndex = flat.Count;
        flat.Add(NodeToData(node, parentIndex));

        foreach (Transform child in node.transform)
        {
            Node childNode = child.GetComponent<Node>();
            if (childNode != null)
            {
                CaptureSubtreeRecursive(childNode, myIndex, flat);
            }
        }
    }

    private static CreatureNodeData NodeToData(Node node, int parentIndex)
    {
        return new CreatureNodeData
        {
            name = node.gameObject.name,
            localPosition = node.transform.localPosition,
            size = node.size,
            parentIndex = parentIndex,
        };
    }

    // ------ Build ------

    // Instantiates nodePrefab (or a bare GameObject+Node if none is given) for
    // every recorded node, restoring hierarchy, local position and size under
    // `parent`. Returns every root node created (parentIndex -1).
    public static List<GameObject> BuildCreature(CreatureData data, Transform parent, GameObject nodePrefab)
    {
        return BuildFromFlat(data != null ? data.nodes : null, parent, nodePrefab);
    }

    // Single-subtree variant (see CaptureSubtree) -- returns the root GameObject.
    public static GameObject BuildSubtree(CreatureNodeData[] nodes, Transform parent, GameObject nodePrefab)
    {
        List<GameObject> roots = BuildFromFlat(nodes, parent, nodePrefab);
        return roots.Count > 0 ? roots[0] : null;
    }

    private static List<GameObject> BuildFromFlat(CreatureNodeData[] nodes, Transform parent, GameObject nodePrefab)
    {
        List<GameObject> roots = new List<GameObject>();
        if (nodes == null || nodes.Length == 0)
        {
            return roots;
        }

        // Pass 1: instantiate every node (no hierarchy yet).
        GameObject[] created = new GameObject[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            CreatureNodeData d = nodes[i];
            GameObject go = nodePrefab != null ? Object.Instantiate(nodePrefab) : new GameObject(d.name, typeof(Node));
            go.name = d.name;

            Node node = go.GetComponent<Node>();
            if (node == null)
            {
                node = go.AddComponent<Node>();
            }
            node.size = d.size;
            created[i] = go;
        }

        // Pass 2: parent, then set local position (order matters -- SetParent
        // with worldPositionStays:false would otherwise leave a stale local pos).
        for (int i = 0; i < nodes.Length; i++)
        {
            CreatureNodeData d = nodes[i];
            Transform t = created[i].transform;

            if (d.parentIndex >= 0 && d.parentIndex < created.Length)
            {
                t.SetParent(created[d.parentIndex].transform, false);
            }
            else
            {
                t.SetParent(parent, false);
                roots.Add(created[i]);
            }
            t.localPosition = d.localPosition;
        }

        return roots;
    }

    // ------ Serialize ------

    public static string ExportToString(GameObject creatureRoot, string creatureName)
    {
        CreatureData data = CaptureCreature(creatureRoot, creatureName);
        try
        {
            return JsonUtility.ToJson(data, true);
        }
        catch (System.Exception e)
        {
            throw new System.Exception($"Could not serialize creature '{creatureName}': {e.Message}", e);
        }
    }

    public static void ExportToFile(GameObject creatureRoot, string creatureName, string filePath)
    {
        File.WriteAllText(filePath, ExportToString(creatureRoot, creatureName));
        Debug.Log($"Creature '{creatureName}' exported to: {filePath}");
    }

    public static List<GameObject> ImportFromString(string json, Transform parent, GameObject nodePrefab, out string creatureName)
    {
        CreatureData data;
        try
        {
            data = JsonUtility.FromJson<CreatureData>(json);
        }
        catch (System.Exception e)
        {
            throw new System.Exception($"Could not parse creature JSON: {e.Message}", e);
        }

        if (data == null || data.nodes == null || data.nodes.Length == 0)
        {
            creatureName = null;
            return new List<GameObject>();
        }

        List<GameObject> roots = BuildCreature(data, parent, nodePrefab);
        creatureName = data.creatureName;
        return roots;
    }

    public static List<GameObject> ImportFromFile(string filePath, Transform parent, GameObject nodePrefab)
    {
        return ImportFromFile(filePath, parent, nodePrefab, out _);
    }

    public static List<GameObject> ImportFromFile(string filePath, Transform parent, GameObject nodePrefab, out string creatureName)
    {
        List<GameObject> roots = ImportFromString(File.ReadAllText(filePath), parent, nodePrefab, out creatureName);
        Debug.Log($"Creature '{creatureName}' imported from: {filePath}");
        return roots;
    }
}
