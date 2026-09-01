// ============================================================================
// BMesh Extensions for Bone Generation
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

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
    // export). The static MeshRenderer is kept hidden while bmesh.skinnedRenderer
    // is set (see BMesh.Update) so the two copies don't render on top of each other.
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
        // The idle sway swings bones well past the mesh's baked bounds -- without
        // this the whole creature pops in and out as the bounds leave the frustum.
        renderer.updateWhenOffscreen = true;

        bmesh.skinnedRenderer = renderer;

        // Wireframe copy: same skin, unwelded triangles carrying barycentric
        // vertex colours, drawn by Custom/WireframeBary. Hidden until the
        // Wireframe show mode selects it (see BMesh.Update).
        if (bmesh.wireframeMaterial != null)
        {
            Mesh wireMesh = BuildWireframeMesh(skinnedMesh);
            GameObject wireObject = new GameObject("Wireframe");
            wireObject.transform.SetParent(skinnedObject.transform, false);
            SkinnedMeshRenderer wireRenderer = wireObject.AddComponent<SkinnedMeshRenderer>();
            wireRenderer.sharedMesh = wireMesh;
            wireRenderer.bones = boneTransforms;
            wireRenderer.rootBone = renderer.rootBone;
            wireRenderer.material = bmesh.wireframeMaterial;
            wireRenderer.updateWhenOffscreen = true;
            wireRenderer.enabled = false;
            bmesh.wireframeRenderer = wireRenderer;
        }

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

    // Unwelds every triangle (3 fresh verts) and writes barycentric coords into
    // the vertex colours, keeping the source skin weights / bind poses so it can
    // ride the same bones as the main skinned mesh (see Custom/WireframeBary).
    private static Mesh BuildWireframeMesh(Mesh src)
    {
        Vector3[] sv = src.vertices;
        Vector3[] sn = src.normals;
        BoneWeight[] sbw = src.boneWeights;
        int[] tris = src.triangles;
        int count = tris.Length;

        Vector3[] v = new Vector3[count];
        Vector3[] n = sn.Length == sv.Length ? new Vector3[count] : System.Array.Empty<Vector3>();
        BoneWeight[] w = sbw.Length == sv.Length ? new BoneWeight[count] : System.Array.Empty<BoneWeight>();
        Color[] colors = new Color[count];
        int[] idx = new int[count];

        Color[] bary = { new Color(1f, 0f, 0f), new Color(0f, 1f, 0f), new Color(0f, 0f, 1f) };

        for (int i = 0; i < count; i++)
        {
            int s = tris[i];
            v[i] = sv[s];
            if (n.Length > 0) n[i] = sn[s];
            if (w.Length > 0) w[i] = sbw[s];
            colors[i] = bary[i % 3];
            idx[i] = i;
        }

        Mesh m = new Mesh
        {
            indexFormat = count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
            vertices = v,
            colors = colors,
        };
        if (n.Length > 0) m.normals = n;
        if (w.Length > 0) m.boneWeights = w;
        m.bindposes = src.bindposes;
        m.triangles = idx;
        m.RecalculateBounds();
        return m;
    }
}