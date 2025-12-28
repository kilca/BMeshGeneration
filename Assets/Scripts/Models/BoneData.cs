using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class BoneData
{
    public string name;
    public int index;
    public Vector3 position;
    public Quaternion rotation;
    public int parentIndex;
    public List<int> childIndices = new List<int>();
    public Matrix4x4 bindPose;

    public BoneData(string name, int index, Vector3 pos, Quaternion rot, int parent)
    {
        this.name = name;
        this.index = index;
        this.position = pos;
        this.rotation = rot;
        this.parentIndex = parent;
    }
}