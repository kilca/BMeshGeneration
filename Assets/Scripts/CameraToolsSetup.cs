using UnityEngine;

// Shared "ensure OrbitCamera + NodeEditController exist on this camera"
// logic -- needed by both CreatureMenu's editor menu items (which should add
// components through Undo.AddComponent for proper undo history) and
// CreatureGeneratorUI's runtime panel (which has no editor Undo system, and
// must stay free of any UnityEditor dependency). Kept out of both of those
// files so neither has to duplicate the component list, guarded behind
// #if UNITY_EDITOR here instead so the runtime caller never references
// UnityEditor itself.
public static class CameraToolsSetup
{
    public static void EnsureComponent<T>(GameObject go, bool useUndo) where T : Component
    {
        if (go.GetComponent<T>() != null)
        {
            return;
        }

#if UNITY_EDITOR
        if (useUndo)
        {
            UnityEditor.Undo.AddComponent<T>(go);
            return;
        }
#endif
        go.AddComponent<T>();
    }

    public static void EnsureCameraTools(GameObject cameraObject, bool useUndo)
    {
        EnsureComponent<OrbitCamera>(cameraObject, useUndo);
        EnsureComponent<NodeEditController>(cameraObject, useUndo);
    }
}
