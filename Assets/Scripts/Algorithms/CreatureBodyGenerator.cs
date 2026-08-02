using System.Collections.Generic;
using UnityEngine;

// Procedural creature body-shape grammar: builds a Node hierarchy (consumed by
// BMesh) from a small recursive grammar -- there is no catalog of creature
// "types". Every call rolls its own topology, so the result can be anything
// from a biped to a spider to something with no real-world equivalent.
// Extracted out of CreatureGenerator so that MonoBehaviour stays a thin
// orchestrator (Generate/Clear/AddSkeleton/...) instead of also containing
// this whole procedural geometry pass.
//
// The core insight: a leg, an arm, a tentacle and a body segment are all the same
// thing -- a chain of Node segments growing from a parent node. What differs between
// creatures is only how many chains grow from a given point and how they're arranged.
// That reduces to 3 primitives (see Arrangement): a single continuation, a mirrored
// left/right pair, or a ring of N branches evenly spaced around an axis. GenerateRandomPart
// builds a random tree of these; BuildPart walks it once, generically, for any result.
public static class CreatureBodyGenerator
{
    // ------ Tuning ------
    // Every range below is (min, max) fed to Random.Range -- exposed as public
    // static fields (rather than inline literals) so the runtime panel can
    // expose them as sliders (see CreatureGeneratorUI's Shape/Size/Branching
    // sections) without needing a rebuild.

    // How many segments a chain has, and how long each one is (scaled by the
    // current taper -- see PartSpec.scale usage below).
    public static Vector2 SegmentCountRange = new Vector2(1, 4); // rounded, Random.Range upper bound exclusive
    public static Vector2 SegmentLengthRange = new Vector2(0.6f, 1.6f);

    // How much a segment's direction can wander from its growth bias -- higher
    // spread means wigglier/less straight chains. Root chains default
    // straighter than limbs.
    public static float RootDirectionSpread = 0.3f;
    public static float LimbDirectionSpread = 0.5f;

    // Segment thickness at the start of a chain, and how much it tapers by
    // the end (EndSize = StartSize * Random.Range(TaperRange)).
    public static Vector2 RootStartSizeRange = new Vector2(0.45f, 0.75f);
    public static Vector2 LimbStartSizeRange = new Vector2(0.15f, 0.5f);
    public static Vector2 TaperRange = new Vector2(0.5f, 0.95f);

    // Per-segment random direction wobble, independent of DirectionSpread
    // (that biases the whole chain's direction; this jitters each segment).
    public static Vector2 JitterRange = new Vector2(0.05f, 0.15f);

    // How many attachments (limbs/continuations) the root rolls.
    public static Vector2 RootAttachmentCountRange = new Vector2(1, 3);

    // Attachment arrangement odds: ContinueChance elongates the chain,
    // RootRingChance (root only) fans out a ring of appendages, and whatever
    // remains becomes a mirrored pair.
    public static float ContinueChance = 0.45f;
    public static float RootRingChance = 0.15f;

    // How much smaller a new appendage/continuation is than its parent chain.
    public static Vector2 ChildScaleRange = new Vector2(0.55f, 0.85f);
    public static Vector2 ContinueScaleRange = new Vector2(0.85f, 1f);

    // How many branches a RadialRing attachment fans out into.
    public static Vector2 RadialRingCountRange = new Vector2(5, 10);

    enum Arrangement { Continue, BilateralPair, RadialRing }

    // A reusable recipe: a chain of segments, plus what grows from its tip.
    // Directions use local axes X = side, Y = up, Z = forward.
    class PartSpec
    {
        public string name = "part";
        public Vector3[] segmentDirections;
        public float[] segmentLengths;
        public float startSize;
        public float endSize;
        public float jitter;
        public List<Attachment> attachments = new List<Attachment>();
    }

    class Attachment
    {
        public Arrangement arrangement;
        public PartSpec spec;
        public int count = 8; // only used by RadialRing
    }

    // Builds one random creature body under `parent` and returns its root
    // GameObject. `nodePrefab` is optional (see NodeFactory.Create) -- passed
    // through rather than read from a field since this is a static, stateless
    // generator.
    public static GameObject Generate(GameObject parent, int complexity, GameObject nodePrefab)
    {
        Vector3[] rootBiasOptions = { Vector3.up, Vector3.forward, (Vector3.up + Vector3.forward).normalized };
        Vector3 rootBias = rootBiasOptions[Random.Range(0, rootBiasOptions.Length)];

        PartSpec root = GenerateRandomPart(rootBias, complexity, 1f, isRoot: true);

        // Index-based rather than "always child 0" so this stays correct even
        // if something else left other children on this GameObject.
        int childIndexBeforeBuild = parent.transform.childCount;
        BuildPart(parent, root, 1f, Quaternion.identity, nodePrefab);
        return parent.transform.GetChild(childIndexBeforeBuild).gameObject;
    }

    // Rerolls everything growing from one random existing node in `body`
    // (destroys its current Node-typed children/subtree and regrows a fresh
    // random subtree in their place) -- lets a body you like keep most of its
    // shape while one limb gets a new look, instead of rerolling everything.
    public static void MutateRandomPart(GameObject body, int complexity, GameObject nodePrefab)
    {
        List<Node> candidates = new List<Node>(body.GetComponentsInChildren<Node>());
        candidates.RemoveAll(n => n.gameObject == body); // rerolling the root via itself is just "regenerate everything"
        if (candidates.Count == 0)
        {
            return;
        }

        Node target = candidates[Random.Range(0, candidates.Count)];

        List<Transform> existingChildren = new List<Transform>();
        foreach (Transform child in target.transform)
        {
            if (child.GetComponent<Node>() != null)
            {
                existingChildren.Add(child);
            }
        }
        foreach (Transform child in existingChildren)
        {
            Object.DestroyImmediate(child.gameObject);
        }

        Vector3 growthBias = target.transform.parent != null
            ? target.transform.position - target.transform.parent.position
            : Vector3.up;
        if (growthBias.sqrMagnitude < 0.01f)
        {
            growthBias = Vector3.up;
        }
        growthBias.Normalize();

        PartSpec spec = GenerateRandomPart(growthBias, Mathf.Max(complexity - 1, 1), Mathf.Max(target.size, 0.3f));
        BuildPart(target.gameObject, spec, Random.value < 0.5f ? 1f : -1f, Quaternion.identity, nodePrefab);
    }

    // ------ Random creature grammar ------
    //
    // Rolls `segmentCount` segments growing roughly along `growthBias`, then rolls
    // 0+ attachments from the tip: Continue (elongates the chain), BilateralPair (a
    // mirrored pair of appendages) or, root only, RadialRing (a fan of appendages --
    // kept root-only so it stays a body-level trait instead of recursively exploding
    // into rings-of-rings). `scale` shrinks going outward so nested appendages taper
    // off instead of growing forever; `depth` bounds recursion.
    static PartSpec GenerateRandomPart(Vector3 growthBias, int depth, float scale, bool isRoot = false)
    {
        int segmentCount = Random.Range(Mathf.RoundToInt(SegmentCountRange.x), Mathf.RoundToInt(SegmentCountRange.y));
        Vector3[] dirs = new Vector3[segmentCount];
        float[] lengths = new float[segmentCount];
        float directionSpread = isRoot ? RootDirectionSpread : LimbDirectionSpread;
        for (int i = 0; i < segmentCount; i++)
        {
            dirs[i] = BiasedDirection(growthBias, directionSpread);
            lengths[i] = Random.Range(SegmentLengthRange.x, SegmentLengthRange.y) * scale;
        }

        Vector2 startSizeRange = isRoot ? RootStartSizeRange : LimbStartSizeRange;
        float startSize = Random.Range(startSizeRange.x, startSizeRange.y) * scale;
        float endSize = startSize * Random.Range(TaperRange.x, TaperRange.y);

        PartSpec spec = new PartSpec
        {
            name = isRoot ? "body" : "limb",
            segmentDirections = dirs,
            segmentLengths = lengths,
            startSize = startSize,
            endSize = endSize,
            jitter = Random.Range(JitterRange.x, JitterRange.y),
        };

        if (depth <= 0)
        {
            return spec;
        }

        // Attachment count used to be a fixed 0-2 range regardless of how much
        // depth budget was left, so `complexity` only changed how deep the
        // (increasingly tiny, tapered-down) nesting could go -- which barely
        // read as a different-looking creature at a glance. Tying the range to
        // the remaining depth means a higher complexity also makes every level
        // bushier, not just the deepest ones, which is a far more visible effect.
        int attachmentCount = isRoot
            ? Random.Range(Mathf.RoundToInt(RootAttachmentCountRange.x), Mathf.RoundToInt(RootAttachmentCountRange.y))
            : Random.Range(0, Mathf.Clamp(depth, 1, 3) + 1);
        for (int a = 0; a < attachmentCount; a++)
        {
            float roll = Random.value;
            float childScale = scale * Random.Range(ChildScaleRange.x, ChildScaleRange.y);

            if (roll < ContinueChance)
            {
                float continueScale = scale * Random.Range(ContinueScaleRange.x, ContinueScaleRange.y);
                spec.attachments.Add(new Attachment
                {
                    arrangement = Arrangement.Continue,
                    spec = GenerateRandomPart(growthBias, depth - 1, continueScale)
                });
            }
            else if (roll < 1f - RootRingChance || !isRoot)
            {
                Vector3 appendageBias = AppendageBias(growthBias);
                spec.attachments.Add(new Attachment
                {
                    arrangement = Arrangement.BilateralPair,
                    spec = GenerateRandomPart(appendageBias, depth - 1, childScale)
                });
            }
            else
            {
                Vector3 appendageBias = AppendageBias(growthBias);
                spec.attachments.Add(new Attachment
                {
                    arrangement = Arrangement.RadialRing,
                    spec = GenerateRandomPart(appendageBias, depth - 1, childScale),
                    count = Random.Range(Mathf.RoundToInt(RadialRingCountRange.x), Mathf.RoundToInt(RadialRingCountRange.y)),
                });
            }
        }

        return spec;
    }

    static Vector3 BiasedDirection(Vector3 bias, float spread)
    {
        Vector3 randomDir = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
        return (bias + randomDir * spread).normalized;
    }

    // A limb hangs off roughly sideways-and-down from wherever its parent is growing,
    // regardless of whether that parent chain is a vertical torso or a horizontal spine.
    static Vector3 AppendageBias(Vector3 parentGrowth)
    {
        Vector3 outward = Vector3.Cross(parentGrowth, Vector3.up);
        if (outward.sqrMagnitude < 0.01f)
        {
            outward = Vector3.right;
        }
        outward.Normalize();

        return (outward + Vector3.down * Random.Range(0.3f, 1f)).normalized;
    }

    // ------ Generic recursive builder ------
    //
    // Spawns spec's own chain of segments, then recurses into its attachments from
    // the chain's tip. `sideSign` (+1/-1) and `rotation` are threaded through the
    // recursion rather than baked into the spec, so the same PartSpec instance
    // can be mirrored or fanned out radially wherever it's attached.
    static GameObject BuildPart(GameObject parent, PartSpec spec, float sideSign, Quaternion rotation, GameObject nodePrefab)
    {
        GameObject current = parent;
        int segmentCount = spec.segmentDirections.Length;
        for (int i = 0; i < segmentCount; i++)
        {
            float t = segmentCount <= 1 ? 0f : (float)i / (segmentCount - 1);
            float size = Mathf.Lerp(spec.startSize, spec.endSize, t);

            Vector3 dir = spec.segmentDirections[i];
            dir.x *= sideSign;
            dir = rotation * dir;
            dir = (dir + Jitter(spec.jitter)).normalized;

            current = SpawnChild(current, dir * spec.segmentLengths[i], size, spec.name + i, nodePrefab);
        }

        foreach (Attachment attachment in spec.attachments)
        {
            switch (attachment.arrangement)
            {
                case Arrangement.Continue:
                    BuildPart(current, attachment.spec, sideSign, rotation, nodePrefab);
                    break;

                case Arrangement.BilateralPair:
                    BuildPart(current, attachment.spec, sideSign, rotation, nodePrefab);
                    BuildPart(current, attachment.spec, -sideSign, rotation, nodePrefab);
                    break;

                case Arrangement.RadialRing:
                    for (int i = 0; i < attachment.count; i++)
                    {
                        float angle = i * 360f / attachment.count;
                        Quaternion ring = rotation * Quaternion.AngleAxis(angle, Vector3.up);
                        BuildPart(current, attachment.spec, sideSign, ring, nodePrefab);
                    }
                    break;
            }
        }

        return current;
    }

    static GameObject SpawnChild(GameObject parent, Vector3 localPosition, float size, string name, GameObject nodePrefab)
    {
        GameObject go = NodeFactory.Create(nodePrefab);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPosition;
        go.name = name;
        go.GetComponent<Node>().size = size;
        return go;
    }

    static Vector3 Jitter(float range)
    {
        return new Vector3(Random.Range(-range, range), Random.Range(-range, range), Random.Range(-range, range));
    }
}
