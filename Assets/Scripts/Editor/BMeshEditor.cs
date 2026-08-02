using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BMesh))]
public class BMeshEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        BMesh bm = (BMesh)target;

        if (GUILayout.Button("Generate"))
        {
            bm.Generate();
        }
    }
}
