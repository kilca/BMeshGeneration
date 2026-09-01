// ============================================================================
// Creature Eye Attacher
// ============================================================================
// Everything related to placing eyes on a generated creature: picking which
// Node to anchor them to, resolving the eye prefab, and attaching a random
// number of eyes (mirrored pairs, optionally plus one centered eye) to that
// anchor. The eye instances carry no Node component, so BMeshGenerator/
// Node.UpdateChilds() (which filter explicitly on GetComponent<Node>() !=
// null) ignore them -- purely cosmetic children, no interference with mesh
// generation.
using System.Collections.Generic;
using UnityEngine;

public static class CreatureEyeAttacher
{
    // ------ Tuning ------

    // Weighted so 2 (the "normal" case) is most common, but not the only option.
    private static readonly int[] EyeCountOptions = { 1, 2, 2, 2, 4, 6 };

    // How often ChooseEyeAnchor picks the predictable "closest leaf to root"
    // spot vs. a random larger leaf elsewhere on the body.
    public static float PredictableAnchorChance = 0.8f;

    // Eye size, as a multiplier of the anchor node's own cap radius (not the
    // whole creature's size) -- this anchor-relative range is what keeps eyes
    // visually glued to the surface they sit on rather than floating off a
    // small node. The actual size rolled for a given eye (see AttachEyes) is
    // randomized within [MinEyeSizeMultiplier, MaxEyeSizeMultiplier].
    public static float MinEyeSizeMultiplier = 1.4f;
    public static float MaxEyeSizeMultiplier = 2.0f;

    // How far sideways a mirrored pair sits, as a multiplier of the anchor's
    // cap radius -- nudged slightly past the radius itself (1) so eyes don't
    // visually sink into the mesh.
    public static float LateralOffsetMultiplier = 1.1f;

    // How far apart multiple stacked pairs (the 4/6-eye case) sit, as a
    // multiplier of the eye's own resolved size.
    public static float StackSpacingMultiplier = 1.1f;

    // The whole creature's size also nudges the eye scale roll upward, before
    // it gets clamped back into the anchor-relative range above -- so a tiny
    // anchor on a huge creature still trends toward the larger end of that
    // range instead of always the smaller one.
    public static float CreatureExtentInfluence = 0.05f;
    public static float MinRandomScaleFactor = 3f;
    public static float MaxRandomScaleFactor = 5f;

    // The real mesh cap ring for a leaf node (see Node.Generate1Node) has
    // radius nodeSize * CapRadiusFraction and sits ForwardOffsetFraction of
    // the segment length past the node's own position. These two must stay in
    // sync with that formula -- not tuning knobs, just named literals so the
    // math below doesn't read as unexplained numbers.
    private const float CapRadiusFraction = 0.5f;
    private const float ForwardOffsetFraction = 0.2f;

    // Degenerate-direction guards -- also not meaningfully tunable, just named.
    private const float DegenerateDirectionThresholdSq = 0.01f;
    private const float UpAxisAlignmentThreshold = 0.95f;

    // ------ Anchor selection ------

    // Eyes only make sense on a true tip (a capped tube end) -- a node with
    // attachments is a multi-child junction handled by convex hull in
    // BMeshGenerator, whose local surface doesn't match a simple size-based
    // offset at all. So the anchor must always be a leaf.
    public static Node ChooseEyeAnchor(GameObject body)
    {
        List<Node> leaves = CollectLeaves(body);
        if (Random.value < PredictableAnchorChance)
        {
            return ClosestLeafToRoot(leaves, body);
        }

        // A tiny sub-appendage tip leaves too little real surface for a
        // creature-proportional eye to sit on convincingly -- bias the
        // "surprise" pick toward the larger half of the available leaves
        // instead of a uniform pick across all of them.
        leaves.Sort((a, b) => b.size.CompareTo(a.size));
        int pool = Mathf.Max(1, leaves.Count / 2);
        return leaves[Random.Range(0, pool)];
    }

    public static GameObject ResolveEyePrefab(GameObject overridePrefab)
    {
        return overridePrefab != null ? overridePrefab : Resources.Load<GameObject>("Eye");
    }

    // Any Node with no Node-typed child -- a limb tip, a tentacle tip, whatever
    // ended up terminal for this random topology. These are the only nodes with a
    // predictable capped-tube surface (see Node.Generate1Node's leaf-node branch).
    private static List<Node> CollectLeaves(GameObject body)
    {
        List<Node> leaves = new();
        foreach (Node node in body.GetComponentsInChildren<Node>())
        {
            bool hasNodeChild = false;
            foreach (Transform child in node.transform)
            {
                if (child.GetComponent<Node>() != null)
                {
                    hasNodeChild = true;
                    break;
                }
            }
            if (!hasNodeChild)
            {
                leaves.Add(node);
            }
        }
        return leaves;
    }

    // Fewest hops up to the root node -- a stand-in for "the tip of the main body"
    // now that the root itself is (almost) never a leaf (the root always grows at
    // least one attachment).
    private static Node ClosestLeafToRoot(List<Node> leaves, GameObject body)
    {
        Node closest = leaves[0];
        int closestDepth = int.MaxValue;
        foreach (Node leaf in leaves)
        {
            int depth = 0;
            Transform t = leaf.transform;
            while (t.gameObject != body && t.parent != null)
            {
                depth++;
                t = t.parent;
            }
            if (depth < closestDepth)
            {
                closestDepth = depth;
                closest = leaf;
            }
        }
        return closest;
    }

    // ------ Attachment ------

    // eyeCount overrides the usual random 1/2/4/6 roll -- used by
    // NodeEditController's "add eye at selected node" (E) shortcut, which
    // should place exactly one eye at that specific spot rather than a random
    // batch meant for whole-creature generation.
    public static void AttachEyes(GameObject anchor, Vector3 lookDirection, GameObject eyePrefab, float creatureExtent, int? eyeCount = null)
    {
        if (eyePrefab == null)
        {
            return; // optional decoration, skip silently if no prefab is available
        }

        float nodeSize = anchor.GetComponent<Node>().size;
        float segmentLength = lookDirection.magnitude;
        Vector3 dir = lookDirection.normalized;

        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < DegenerateDirectionThresholdSq)
        {
            side = Vector3.right;
        }
        side.Normalize();

        // Perpendicular to both dir and side -- used to stack multiple eye
        // pairs at different heights (4/6-eye case) instead of overlapping them.
        Vector3 stackAxis = Vector3.Cross(side, dir).normalized;

        // Quaternion.LookRotation degenerates when its "up" hint is parallel to the
        // forward direction -- pick a hint that never lines up with dir.
        Vector3 upHint = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > UpAxisAlignmentThreshold ? Vector3.forward : Vector3.up;

        Vector3 forwardOffset = dir * (segmentLength * ForwardOffsetFraction);

        float capRadius = nodeSize * CapRadiusFraction;
        float lateralOffset = capRadius * LateralOffsetMultiplier;

        float desiredScale = Mathf.Max(nodeSize, creatureExtent * CreatureExtentInfluence) * Random.Range(MinRandomScaleFactor, MaxRandomScaleFactor);
        float eyeScale = Mathf.Clamp(desiredScale, capRadius * MinEyeSizeMultiplier, capRadius * MaxEyeSizeMultiplier);
        float stackSpacing = eyeScale * StackSpacingMultiplier;

        int resolvedEyeCount = eyeCount ?? EyeCountOptions[Random.Range(0, EyeCountOptions.Length)];
        int pairCount = resolvedEyeCount / 2;
        bool centerEye = resolvedEyeCount % 2 == 1;

        for (int p = 0; p < pairCount; p++)
        {
            float stackOffset = pairCount <= 1 ? 0f : Mathf.Lerp(-0.5f, 0.5f, p / (float)(pairCount - 1)) * stackSpacing;
            Vector3 verticalOffset = stackAxis * stackOffset + forwardOffset;
            SpawnEye(anchor.transform, side * lateralOffset + verticalOffset, dir, upHint, eyeScale, eyePrefab);
            SpawnEye(anchor.transform, -side * lateralOffset + verticalOffset, dir, upHint, eyeScale, eyePrefab);
        }

        if (centerEye)
        {
            SpawnEye(anchor.transform, forwardOffset, dir, upHint, eyeScale, eyePrefab);
        }
    }

    private static void SpawnEye(Transform parent, Vector3 localPosition, Vector3 dir, Vector3 upHint, float eyeScale, GameObject eyePrefab)
    {
        GameObject eye = Object.Instantiate(eyePrefab, parent);
        eye.transform.localPosition = localPosition;
        eye.transform.rotation = Quaternion.LookRotation(dir, upHint);
        eye.transform.localScale = Vector3.one * eyeScale;
        eye.AddComponent<CreatureEye>();

        // Capture the eye's offset relative to its Node now, while the Node
        // hierarchy is still the only thing driving it (see FollowTransform) --
        // BMeshBoneExtensions.CreateSkeleton() later points this at the matching
        // bone via SetSource, without disturbing the captured offset.
        eye.AddComponent<FollowTransform>().CaptureOffset(parent);
    }

    // Shared with NodeEditController's "add eye at selected node" shortcut, so
    // both places estimate creature size the same way.
    public static float EstimateExtent(Transform referencePoint, GameObject searchRoot)
    {
        float maxDistance = 0f;
        foreach (Node node in searchRoot.GetComponentsInChildren<Node>())
        {
            float distance = Vector3.Distance(node.transform.position, referencePoint.position);
            if (distance > maxDistance)
            {
                maxDistance = distance;
            }
        }
        return maxDistance;
    }
}
