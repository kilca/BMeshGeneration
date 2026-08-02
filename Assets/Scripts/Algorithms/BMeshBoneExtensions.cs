// ============================================================================
// BMesh Extensions for Bone Generation
// ============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public static class BMeshBoneExtensions
{
    public static List<BoneData> GenerateBones(this BMesh bmesh)
    {
        List<Node> nodes = new List<Node>(bmesh.GetComponentsInChildren<Node>());
        return BoneGenerator.GenerateBonesFromNodes(nodes, bmesh.transform);
    }

    public static void CreateSkeletonHierarchy(this BMesh bmesh, string parentName = "Skeleton")
    {
        List<BoneData> bones = bmesh.GenerateBones();
        
        GameObject skeletonRoot = new GameObject(parentName);
        skeletonRoot.transform.SetParent(bmesh.transform);
        skeletonRoot.transform.localPosition = Vector3.zero;
        skeletonRoot.transform.localRotation = Quaternion.identity;

        Transform[] boneTransforms = BoneGenerator.CreateBoneTransforms(bones, skeletonRoot.transform);
        
        Debug.Log($"Created skeleton with {bones.Count} bones");
    }

    public static MeshExportData PrepareExport(this BMesh bmesh)
    {
        Mesh mesh = bmesh.GetComponent<MeshFilter>().sharedMesh;
        List<BoneData> bones = bmesh.GenerateBones();
        return MeshExporter.CreateExportData(mesh, bones);
    }

    // Builds an actual posable skeleton: a bone hierarchy (CreateSkeletonHierarchy)
    // bound to a SkinnedMeshRenderer on a new child GameObject, with automatic
    // proximity-based bone weights (same weighting used by the COLLADA/JSON/binary
    // export). The original MeshRenderer is hidden (showMode -> Gizmo) so the static
    // mesh and the skinned copy don't render on top of each other.
    public static SkinnedMeshRenderer CreateSkeleton(this BMesh bmesh)
    {
        Mesh sourceMesh = bmesh.GetComponent<MeshFilter>().sharedMesh;
        List<Node> nodes = new List<Node>(bmesh.GetComponentsInChildren<Node>());
        List<BoneData> bones = BoneGenerator.GenerateBonesFromNodes(nodes, bmesh.transform);

        GameObject skeletonRoot = new GameObject("Skeleton");
        skeletonRoot.transform.SetParent(bmesh.transform, false);
        Transform[] boneTransforms = BoneGenerator.CreateBoneTransforms(bones, skeletonRoot.transform);
        skeletonRoot.AddComponent<SkeletonBoneMap>().Set(nodes, boneTransforms);

        Mesh skinnedMesh = Object.Instantiate(sourceMesh);
        skinnedMesh.boneWeights = MeshExporter.GenerateBoneWeights(skinnedMesh.vertices, bones);

        Matrix4x4[] bindPoses = new Matrix4x4[bones.Count];
        for (int i = 0; i < bones.Count; i++)
        {
            bindPoses[i] = bones[i].bindPose;
        }
        skinnedMesh.bindposes = bindPoses;

        GameObject skinnedObject = new GameObject("SkinnedMesh");
        skinnedObject.transform.SetParent(bmesh.transform, false);
        SkinnedMeshRenderer renderer = skinnedObject.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = skinnedMesh;
        renderer.bones = boneTransforms;
        renderer.rootBone = boneTransforms.Length > 0 ? boneTransforms[0] : null;
        renderer.material = bmesh.normalMaterial;

        bmesh.showMode = BMesh.ShowMode.Gizmo;

        // Decorations (eyes, ...) are parented to Nodes, which stay static once
        // bones take over the visible mesh -- make each one follow its matching
        // bone instead (nodes[i] <-> boneTransforms[i], same generation order).
        for (int i = 0; i < nodes.Count && i < boneTransforms.Length; i++)
        {
            foreach (Transform child in nodes[i].transform)
            {
                if (child.GetComponent<Node>() != null)
                {
                    continue; // a nested Node, not a decoration
                }

                FollowTransform follow = child.GetComponent<FollowTransform>();
                if (follow == null)
                {
                    // Fallback for decorations that didn't already capture their
                    // offset at creation (see CreatureEyeAttacher) -- capturing
                    // here is only correct if this is the first skeleton built for
                    // this creature (i.e. the decoration hasn't been bone-driven
                    // yet), which holds for anything reaching this branch.
                    follow = child.gameObject.AddComponent<FollowTransform>();
                    follow.CaptureOffset(nodes[i].transform);
                }
                follow.SetSource(boneTransforms[i]);
            }
        }

        return renderer;
    }

    // Wires a generic "everything wobbles a bit" secondary-motion rig on top of
    // an existing skeleton (see CreateSkeleton): every bone except the root gets
    // a DampedTransform sourced from its own parent bone, so it lags/springs
    // behind whenever that parent moves. Works for any topology (legs, tentacles,
    // spine segments) with no per-limb semantic setup, unlike e.g. TwoBoneIK which
    // needs a specific 3-joint chain. Something still has to actually move the
    // root bone for this to be visible -- see CreatureIdleSway.
    public static GameObject AddSecondaryMotionRig(this BMesh bmesh, Transform skeletonGroup, float dampPosition = 0.5f, float dampRotation = 0.5f)
    {
        GameObject rigObject = new GameObject("Rig");
        rigObject.transform.SetParent(bmesh.transform, false);
        Rig rig = rigObject.AddComponent<Rig>();

        foreach (Transform bone in skeletonGroup.GetComponentsInChildren<Transform>())
        {
            if (bone == skeletonGroup || bone.parent == skeletonGroup)
            {
                continue; // the grouping object itself, or the root bone (no bone parent to lag behind)
            }

            GameObject constraintObject = new GameObject(bone.name + "_Damped");
            constraintObject.transform.SetParent(rigObject.transform, false);

            DampedTransform damped = constraintObject.AddComponent<DampedTransform>();
            damped.data.constrainedObject = bone;
            damped.data.sourceObject = bone.parent;
            damped.data.dampPosition = dampPosition;
            damped.data.dampRotation = dampRotation;
        }

        RigBuilder rigBuilder = bmesh.GetComponent<RigBuilder>();
        if (rigBuilder == null)
        {
            rigBuilder = bmesh.gameObject.AddComponent<RigBuilder>(); // auto-adds the required Animator too
        }
        else
        {
            rigBuilder.layers.Clear(); // reused from a previous AddIdleAnimation call -- drop stale layers
        }
        rigBuilder.layers.Add(new RigLayer(rig));
        rigBuilder.Build();

        return rigObject;
    }
}