// ============================================================================
// Bone Generator Utility
// ============================================================================
using System.Collections.Generic;
using UnityEngine;

public static class BoneGenerator
{
    public static List<BoneData> GenerateBonesFromNodes(List<Node> nodes, Transform rootTransform)
    {
        List<BoneData> bones = new List<BoneData>();
        Dictionary<Transform, int> transformToIndex = new Dictionary<Transform, int>();

        // First pass: Create all bones
        int boneIndex = 0;
        foreach (Node node in nodes)
        {
            if (node == null) continue;

            int parentIndex = -1;
            if (node.transform.parent != null)
            {
                Node parentNode = node.transform.parent.GetComponent<Node>();
                if (parentNode != null && transformToIndex.ContainsKey(node.transform.parent))
                {
                    parentIndex = transformToIndex[node.transform.parent];
                }
            }

            BoneData bone = new BoneData(
                node.gameObject.name,
                boneIndex,
                node.transform.position,
                node.transform.rotation,
                parentIndex
            );

            bones.Add(bone);
            transformToIndex[node.transform] = boneIndex;
            boneIndex++;
        }

        // Second pass: Build child relationships
        foreach (Node node in nodes)
        {
            if (node == null) continue;

            int currentIndex = transformToIndex[node.transform];
            
            foreach (Node childNode in node.childNodes)
            {
                if (childNode != null && transformToIndex.ContainsKey(childNode.transform))
                {
                    int childIndex = transformToIndex[childNode.transform];
                    bones[currentIndex].childIndices.Add(childIndex);
                }
            }
        }

        // Third pass: Calculate bind poses
        foreach (BoneData bone in bones)
        {
            bone.bindPose = Matrix4x4.TRS(bone.position, bone.rotation, Vector3.one).inverse;
        }

        return bones;
    }

    public static Transform[] CreateBoneTransforms(List<BoneData> bones, Transform parent)
    {
        Transform[] boneTransforms = new Transform[bones.Count];
        
        // Create all bone GameObjects. The index suffix keeps every name unique:
        // mirrored limbs (BilateralPair) reuse the same PartSpec, so without it
        // two sibling bones share a name and an AnimationClip transform path
        // resolves to only one of them -- which left just a single limb animating.
        for (int i = 0; i < bones.Count; i++)
        {
            GameObject boneObj = new GameObject($"{bones[i].name}_{i}_Bone");
            boneObj.transform.position = bones[i].position;
            boneObj.transform.rotation = bones[i].rotation;
            boneTransforms[i] = boneObj.transform;
        }

        // Set up hierarchy
        for (int i = 0; i < bones.Count; i++)
        {
            if (bones[i].parentIndex >= 0)
            {
                boneTransforms[i].SetParent(boneTransforms[bones[i].parentIndex]);
            }
            else
            {
                boneTransforms[i].SetParent(parent);
            }
        }

        return boneTransforms;
    }
}