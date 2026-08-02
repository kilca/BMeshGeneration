using UnityEditor;
using UnityEngine;

// Editor-only buttons, always available (edit mode or play mode -- Inspector
// GUI doesn't care which) regardless of whether the runtime CreatureGeneratorUI
// panel exists in the scene. Kept in sync with that panel's action set.
[CustomEditor(typeof(CreatureGenerator))]
public class CreatureGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        CreatureGenerator generator = (CreatureGenerator)target;

        if (GUILayout.Button("Generate"))
        {
            generator.Generate();
        }

        EditorGUI.BeginDisabledGroup(generator.body == null);

        if (GUILayout.Button("Regenerate Skin"))
        {
            generator.RegenerateSkin();
        }
        if (GUILayout.Button("Add Skeleton"))
        {
            generator.AddSkeleton();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(generator.skeleton == null);
        if (GUILayout.Button("Add Animation"))
        {
            generator.AddIdleAnimation();
        }
        EditorGUI.EndDisabledGroup();

        if (GUILayout.Button("Clear"))
        {
            generator.Clear();
        }
    }
}
