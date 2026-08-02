// ============================================================================
// Creature Skeleton Import/Export
// ============================================================================
// Round-trips the Node hierarchy that CreatureGenerator/ProcGen build (name,
// local position, size) as JSON, independent of BMesh's mesh/bone export.
// Unlike the mesh+bones export (MeshExportData), this is meant to be reloaded
// back into Unity as an editable Node tree -- to save a creature you like, or
// hand-tweak one and reuse it -- rather than to hand off to other 3D software.
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]
public class CreatureNodeData
{
    public string name;
    public Vector3 localPosition;
    public float size;
    public List<CreatureNodeData> children = new List<CreatureNodeData>();
}

[System.Serializable]
public class CreatureData
{
    public string creatureName;
    public List<CreatureNodeData> roots = new List<CreatureNodeData>();
}

public static class CreatureIO
{
    public static CreatureNodeData CaptureHierarchy(Node node)
    {
        CreatureNodeData data = new CreatureNodeData
        {
            name = node.gameObject.name,
            localPosition = node.transform.localPosition,
            size = node.size,
        };

        foreach (Transform child in node.transform)
        {
            Node childNode = child.GetComponent<Node>();
            if (childNode != null)
            {
                data.children.Add(CaptureHierarchy(childNode));
            }
        }

        return data;
    }

    // Captures every root-level Node (a Node with no Node parent) found under creatureRoot.
    public static CreatureData CaptureCreature(GameObject creatureRoot, string creatureName)
    {
        CreatureData data = new CreatureData { creatureName = creatureName };

        foreach (Node node in creatureRoot.GetComponentsInChildren<Node>())
        {
            Transform parent = node.transform.parent;
            bool isRoot = parent == null || parent.GetComponent<Node>() == null;
            if (isRoot)
            {
                data.roots.Add(CaptureHierarchy(node));
            }
        }

        return data;
    }

    public static void ExportToFile(GameObject creatureRoot, string creatureName, string filePath)
    {
        CreatureData data = CaptureCreature(creatureRoot, creatureName);
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(filePath, json);
        Debug.Log($"Creature '{creatureName}' exported to: {filePath}");
    }

    // Instantiates nodePrefab (or a bare GameObject+Node if none is given) for every
    // recorded node, restoring local position, size and hierarchy under `parent`.
    public static GameObject BuildHierarchy(CreatureNodeData data, Transform parent, GameObject nodePrefab)
    {
        GameObject go = nodePrefab != null ? Object.Instantiate(nodePrefab) : new GameObject(data.name, typeof(Node));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = data.localPosition;
        go.name = data.name;

        Node node = go.GetComponent<Node>();
        if (node == null)
        {
            node = go.AddComponent<Node>();
        }
        node.size = data.size;

        foreach (CreatureNodeData childData in data.children)
        {
            BuildHierarchy(childData, go.transform, nodePrefab);
        }

        return go;
    }

    public static List<GameObject> BuildCreature(CreatureData data, Transform parent, GameObject nodePrefab)
    {
        List<GameObject> roots = new List<GameObject>();
        foreach (CreatureNodeData rootData in data.roots)
        {
            roots.Add(BuildHierarchy(rootData, parent, nodePrefab));
        }
        return roots;
    }

    public static List<GameObject> ImportFromFile(string filePath, Transform parent, GameObject nodePrefab)
    {
        return ImportFromFile(filePath, parent, nodePrefab, out _);
    }

    public static List<GameObject> ImportFromFile(string filePath, Transform parent, GameObject nodePrefab, out string creatureName)
    {
        string json = File.ReadAllText(filePath);
        CreatureData data = JsonUtility.FromJson<CreatureData>(json);
        List<GameObject> roots = BuildCreature(data, parent, nodePrefab);
        creatureName = data.creatureName;
        Debug.Log($"Creature '{data.creatureName}' imported from: {filePath}");
        return roots;
    }
}
