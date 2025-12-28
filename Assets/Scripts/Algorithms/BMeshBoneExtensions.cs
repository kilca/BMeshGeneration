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
}