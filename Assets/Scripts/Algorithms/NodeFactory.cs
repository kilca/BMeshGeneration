// ============================================================================
// Node Factory
// ============================================================================
// Shared "give me a Node GameObject" fallback, used by anything that needs to
// spawn nodes (CreatureGenerator's procedural builder, NodeEditController's
// manual "add node" action): assigned prefab -> "Node" prefab in Resources ->
// a bare GameObject with a Node component. Extracted out of CreatureGenerator
// so both places stay in sync instead of duplicating the fallback chain.
using UnityEngine;

public static class NodeFactory
{
    public static GameObject Create(GameObject nodePrefabOverride = null)
    {
        if (nodePrefabOverride != null)
        {
            return Object.Instantiate(nodePrefabOverride);
        }

        GameObject resourcePrefab = Resources.Load<GameObject>("Node");
        if (resourcePrefab != null)
        {
            return Object.Instantiate(resourcePrefab);
        }

        return new GameObject("Node", typeof(Node));
    }
}
