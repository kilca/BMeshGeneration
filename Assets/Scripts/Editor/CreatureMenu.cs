using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Zero-setup entry points: creates GameObjects with the creature-generation
// pieces already wired together, so there's nothing to assign by hand before
// either one works.
public static class CreatureMenu
{
    [MenuItem("GameObject/3D Object/Random Creature", false, 10)]
    public static void CreateRandomCreature(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("Creature");
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

        go.AddComponent<BMesh>();
        CreatureGenerator generator = go.AddComponent<CreatureGenerator>();

        Undo.RegisterCreatedObjectUndo(go, "Create Random Creature");
        Selection.activeGameObject = go;

        EnsureCameraTools();

        generator.Generate();
    }

    // Adds the in-game "Generate Creature" panel (CreatureGeneratorUI.cs) to the
    // scene. Works both while testing in the Editor's Play mode and, eventually,
    // in a build -- the panel itself has no editor-only dependency.
    [MenuItem("GameObject/UI Toolkit/Creature Generator Panel", false, 10)]
    public static void CreateCreatureGeneratorPanel(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("Creature Generator UI");
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

        go.AddComponent<UIDocument>();
        go.AddComponent<CreatureGeneratorUI>();

        Undo.RegisterCreatedObjectUndo(go, "Create Creature Generator Panel");
        Selection.activeGameObject = go;

        EnsureCameraTools();
    }

    // Adds an OrbitCamera (right-click drag to orbit, scroll to zoom) and a
    // NodeEditController (left-click to select/drag nodes, keyboard for
    // resize/add/delete) to the main camera, if any -- called from both menu
    // items above so neither entry point into creature generation leaves you
    // without a way to look at and hand-edit whatever gets generated.
    static void EnsureCameraTools()
    {
        if (Camera.main == null)
        {
            return;
        }

        GameObject cameraObject = Camera.main.gameObject;
        if (cameraObject.GetComponent<OrbitCamera>() == null)
        {
            Undo.AddComponent<OrbitCamera>(cameraObject);
        }
        if (cameraObject.GetComponent<NodeEditController>() == null)
        {
            Undo.AddComponent<NodeEditController>(cameraObject);
        }
    }
}
