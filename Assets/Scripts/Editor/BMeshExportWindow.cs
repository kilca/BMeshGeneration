// ============================================================================
// Editor Window for Export
// ============================================================================
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public class BMeshExportWindow : EditorWindow
{
    private BMesh targetBMesh;
    private string exportPath = "Assets/";
    private string fileName = "ExportedMesh";
    private int exportFormat = 0;
    private string[] formatOptions = { "OBJ (Geometry Only)", "COLLADA/DAE (With Bones)", "JSON (With Bones)", "Binary (With Bones)" };

    private GameObject nodePrefab;
    private string creatureFileName = "ExportedCreature";

    [MenuItem("Tools/BMesh Exporter")]
    public static void ShowWindow()
    {
        GetWindow<BMeshExportWindow>("BMesh Exporter");
    }

    void OnGUI()
    {
        GUILayout.Label("BMesh Export Tool", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        targetBMesh = (BMesh)EditorGUILayout.ObjectField("BMesh", targetBMesh, typeof(BMesh), true);
        
        EditorGUILayout.Space();
        
        exportPath = EditorGUILayout.TextField("Export Path", exportPath);
        fileName = EditorGUILayout.TextField("File Name", fileName);
        exportFormat = EditorGUILayout.Popup("Format", exportFormat, formatOptions);

        // Show format info
        EditorGUILayout.Space();
        switch (exportFormat)
        {
            case 0:
                EditorGUILayout.HelpBox("OBJ format exports geometry only. Bones are NOT included.", MessageType.Info);
                break;
            case 1:
                EditorGUILayout.HelpBox("COLLADA (.dae) format includes bones and can be imported into most 3D software.", MessageType.Info);
                break;
            case 2:
                EditorGUILayout.HelpBox("JSON format includes all data (mesh + bones) in human-readable format.", MessageType.Info);
                break;
            case 3:
                EditorGUILayout.HelpBox("Binary format is compact and includes all data (mesh + bones).", MessageType.Info);
                break;
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Browse..."))
        {
            string path = EditorUtility.SaveFolderPanel("Select Export Folder", exportPath, "");
            if (!string.IsNullOrEmpty(path))
            {
                exportPath = path;
            }
        }

        EditorGUILayout.Space();

        EditorGUI.BeginDisabledGroup(targetBMesh == null);
        
        if (GUILayout.Button("Generate Bones", GUILayout.Height(30)))
        {
            GenerateBones();
        }

        if (GUILayout.Button("Export Mesh", GUILayout.Height(30)))
        {
            ExportMesh();
        }

        EditorGUI.EndDisabledGroup();

        if (targetBMesh == null)
        {
            EditorGUILayout.HelpBox("Please assign a BMesh object to export.", MessageType.Warning);
        }

        EditorGUILayout.Space();
        GUILayout.Label("Creature Skeleton (Node hierarchy)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Exports/imports the Node hierarchy itself (name, position, size) as JSON, so a generated creature can be saved and reloaded as editable nodes -- independent of the mesh/bones export above.", MessageType.Info);

        creatureFileName = EditorGUILayout.TextField("Creature File Name", creatureFileName);
        nodePrefab = (GameObject)EditorGUILayout.ObjectField("Node Prefab (for import)", nodePrefab, typeof(GameObject), false);

        EditorGUI.BeginDisabledGroup(targetBMesh == null);

        if (GUILayout.Button("Export Creature Skeleton", GUILayout.Height(30)))
        {
            ExportCreature();
        }

        EditorGUI.BeginDisabledGroup(nodePrefab == null);
        if (GUILayout.Button("Import Creature Skeleton", GUILayout.Height(30)))
        {
            ImportCreature();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUI.EndDisabledGroup();
    }

    private void GenerateBones()
    {
        if (targetBMesh == null) return;

        targetBMesh.CreateSkeletonHierarchy();
        EditorUtility.DisplayDialog("Success", "Bones generated successfully!", "OK");
    }

    private void ExportMesh()
    {
        if (targetBMesh == null) return;

        MeshExportData exportData = targetBMesh.PrepareExport();
        string extension = "";

        switch (exportFormat)
        {
            case 0: // OBJ
                extension = ".obj";
                MeshExporter.ExportToOBJ(exportData, Path.Combine(exportPath, fileName + extension));
                break;
            case 1: // COLLADA
                extension = ".dae";
                MeshExporter.ExportToCollada(exportData, Path.Combine(exportPath, fileName + extension));
                break;
            case 2: // JSON
                extension = ".json";
                MeshExporter.ExportToJSON(exportData, Path.Combine(exportPath, fileName + extension));
                break;
            case 3: // Binary
                extension = ".bmesh";
                MeshExporter.ExportToBinary(exportData, Path.Combine(exportPath, fileName + extension));
                break;
        }

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Success", $"Mesh exported to: {Path.Combine(exportPath, fileName + extension)}", "OK");
    }

    private void ExportCreature()
    {
        if (targetBMesh == null) return;

        string filePath = Path.Combine(exportPath, creatureFileName + ".json");
        CreatureIO.ExportToFile(targetBMesh.gameObject, creatureFileName, filePath);

        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Success", $"Creature skeleton exported to: {filePath}", "OK");
    }

    private void ImportCreature()
    {
        if (targetBMesh == null || nodePrefab == null) return;

        string filePath = EditorUtility.OpenFilePanel("Select Creature Skeleton", exportPath, "json");
        if (string.IsNullOrEmpty(filePath)) return;

        CreatureIO.ImportFromFile(filePath, targetBMesh.transform, nodePrefab);
        EditorUtility.DisplayDialog("Success", "Creature skeleton imported. Click \"Generate\" on the BMesh to rebuild the mesh.", "OK");
    }
}
#endif