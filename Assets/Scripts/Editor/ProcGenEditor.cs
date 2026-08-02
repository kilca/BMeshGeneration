using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ProcGen))]
public class ProcGenEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ProcGen bm = (ProcGen)target;

        if (GUILayout.Button("Generate"))
        {
            bm.Generate();
        }
        if (GUILayout.Button("Clear"))
        {
            bm.Clear();
        }
    }
}
