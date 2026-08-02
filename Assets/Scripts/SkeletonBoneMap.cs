using System.Collections.Generic;
using UnityEngine;

// Attached to a skeleton root by BMeshBoneExtensions.CreateSkeleton(): records
// the Node <-> bone Transform pairing built there, so other code (e.g.
// NodeEditController's node markers, which need to visually track the
// animated pose rather than a Node's own static transform) can look up which
// bone a given Node maps to, without re-deriving the nested bone hierarchy's
// construction order itself.
public class SkeletonBoneMap : MonoBehaviour
{
    private readonly Dictionary<Node, Transform> map = new Dictionary<Node, Transform>();

    public void Set(IReadOnlyList<Node> nodes, IReadOnlyList<Transform> bones)
    {
        map.Clear();
        for (int i = 0; i < nodes.Count && i < bones.Count; i++)
        {
            map[nodes[i]] = bones[i];
        }
    }

    public Transform GetBone(Node node)
    {
        return map.TryGetValue(node, out Transform bone) ? bone : null;
    }
}
