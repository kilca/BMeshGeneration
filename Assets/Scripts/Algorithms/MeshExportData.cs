
// ============================================================================
// Mesh Export Data Structure
// ============================================================================
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[System.Serializable]
public class MeshExportData
{
    public Vector3[] vertices;
    public int[] triangles;
    public Vector3[] normals;
    public Vector2[] uvs;
    public BoneWeight[] boneWeights;
    public Matrix4x4[] bindPoses;
    public string[] boneNames;
    public int[] boneParents;
    public Vector3[] bonePositions;
    public Quaternion[] boneRotations;

    public MeshExportData()
    {
        vertices = new Vector3[0];
        triangles = new int[0];
        normals = new Vector3[0];
        uvs = new Vector2[0];
        boneWeights = new BoneWeight[0];
        bindPoses = new Matrix4x4[0];
        boneNames = new string[0];
        boneParents = new int[0];
        bonePositions = new Vector3[0];
        boneRotations = new Quaternion[0];
    }
}

public static class MeshExporter
{
    public static MeshExportData CreateExportData(Mesh mesh, List<BoneData> bones)
    {
        MeshExportData exportData = new MeshExportData();

        // Copy mesh data
        exportData.vertices = mesh.vertices;
        exportData.triangles = mesh.triangles;
        exportData.normals = mesh.normals;
        exportData.uvs = mesh.uv;

        // Copy bone data
        if (bones != null && bones.Count > 0)
        {
            exportData.boneNames = new string[bones.Count];
            exportData.boneParents = new int[bones.Count];
            exportData.bindPoses = new Matrix4x4[bones.Count];
            exportData.bonePositions = new Vector3[bones.Count];
            exportData.boneRotations = new Quaternion[bones.Count];

            for (int i = 0; i < bones.Count; i++)
            {
                exportData.boneNames[i] = bones[i].name;
                exportData.boneParents[i] = bones[i].parentIndex;
                exportData.bindPoses[i] = bones[i].bindPose;
                exportData.bonePositions[i] = bones[i].position;
                exportData.boneRotations[i] = bones[i].rotation;
            }

            // Generate bone weights (simple automatic weighting based on proximity)
            exportData.boneWeights = GenerateBoneWeights(exportData.vertices, bones);
        }

        return exportData;
    }

    private static BoneWeight[] GenerateBoneWeights(Vector3[] vertices, List<BoneData> bones)
    {
        BoneWeight[] weights = new BoneWeight[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            // Find closest bones
            List<(int index, float distance)> closestBones = new List<(int, float)>();

            for (int b = 0; b < bones.Count; b++)
            {
                float distance = Vector3.Distance(vertices[i], bones[b].position);
                closestBones.Add((b, distance));
            }

            // Sort by distance
            closestBones.Sort((a, b) => a.distance.CompareTo(b.distance));

            // Assign up to 4 closest bones
            BoneWeight weight = new BoneWeight();
            float totalWeight = 0f;

            if (closestBones.Count > 0)
            {
                weight.boneIndex0 = closestBones[0].index;
                weight.weight0 = 1f / (closestBones[0].distance + 0.001f);
                totalWeight += weight.weight0;
            }

            if (closestBones.Count > 1)
            {
                weight.boneIndex1 = closestBones[1].index;
                weight.weight1 = 1f / (closestBones[1].distance + 0.001f);
                totalWeight += weight.weight1;
            }

            if (closestBones.Count > 2)
            {
                weight.boneIndex2 = closestBones[2].index;
                weight.weight2 = 1f / (closestBones[2].distance + 0.001f);
                totalWeight += weight.weight2;
            }

            if (closestBones.Count > 3)
            {
                weight.boneIndex3 = closestBones[3].index;
                weight.weight3 = 1f / (closestBones[3].distance + 0.001f);
                totalWeight += weight.weight3;
            }

            // Normalize weights
            if (totalWeight > 0)
            {
                weight.weight0 /= totalWeight;
                weight.weight1 /= totalWeight;
                weight.weight2 /= totalWeight;
                weight.weight3 /= totalWeight;
            }

            weights[i] = weight;
        }

        return weights;
    }

    // Export to OBJ format (GEOMETRY ONLY - NO BONES)
    public static void ExportToOBJ(MeshExportData data, string filePath)
    {
        StringBuilder sb = new StringBuilder();
        CultureInfo culture = CultureInfo.InvariantCulture; // Use dots for decimals

        sb.AppendLine("# Exported from BMesh");
        sb.AppendLine($"# Vertices: {data.vertices.Length}");
        sb.AppendLine($"# Triangles: {data.triangles.Length / 3}");
        sb.AppendLine("# WARNING: OBJ format does not support bones/rigging");
        sb.AppendLine();

        // Write vertices
        foreach (Vector3 v in data.vertices)
        {
            sb.AppendLine(string.Format(culture, "v {0} {1} {2}", v.x, v.y, v.z));
        }

        // Write normals
        if (data.normals.Length > 0)
        {
            foreach (Vector3 n in data.normals)
            {
                sb.AppendLine(string.Format(culture, "vn {0} {1} {2}", n.x, n.y, n.z));
            }
        }

        // Write UVs
        if (data.uvs.Length > 0)
        {
            foreach (Vector2 uv in data.uvs)
            {
                sb.AppendLine(string.Format(culture, "vt {0} {1}", uv.x, uv.y));
            }
        }

        // Write faces
        sb.AppendLine();
        bool hasUVs = data.uvs.Length > 0;
        bool hasNormals = data.normals.Length > 0;

        for (int i = 0; i < data.triangles.Length; i += 3)
        {
            int v1 = data.triangles[i] + 1;
            int v2 = data.triangles[i + 1] + 1;
            int v3 = data.triangles[i + 2] + 1;

            if (hasUVs && hasNormals)
                sb.AppendLine($"f {v1}/{v1}/{v1} {v2}/{v2}/{v2} {v3}/{v3}/{v3}");
            else if (hasNormals)
                sb.AppendLine($"f {v1}//{v1} {v2}//{v2} {v3}//{v3}");
            else if (hasUVs)
                sb.AppendLine($"f {v1}/{v1} {v2}/{v2} {v3}/{v3}");
            else
                sb.AppendLine($"f {v1} {v2} {v3}");
        }

        File.WriteAllText(filePath, sb.ToString());
        Debug.Log($"Mesh exported to OBJ (geometry only): {filePath}");
    }

    // Export to JSON format (WITH BONES)
    public static void ExportToJSON(MeshExportData data, string filePath)
    {
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(filePath, json);
        Debug.Log($"Mesh data with bones exported to JSON: {filePath}");
    }

    // Export to COLLADA format (WITH BONES - simplified version)
    public static void ExportToCollada(MeshExportData data, string filePath)
    {
        StringBuilder sb = new StringBuilder();
        CultureInfo culture = CultureInfo.InvariantCulture;

        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<COLLADA xmlns=\"http://www.collada.org/2005/11/COLLADASchema\" version=\"1.4.1\">");
        sb.AppendLine("  <asset>");
        sb.AppendLine("    <contributor><authoring_tool>BMesh Exporter</authoring_tool></contributor>");
        sb.AppendLine("    <created>" + System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "</created>");
        sb.AppendLine("    <modified>" + System.DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss") + "</modified>");
        sb.AppendLine("    <up_axis>Y_UP</up_axis>");
        sb.AppendLine("  </asset>");
        
        // Library geometries
        sb.AppendLine("  <library_geometries>");
        sb.AppendLine("    <geometry id=\"mesh-geometry\" name=\"BMesh\">");
        sb.AppendLine("      <mesh>");
        
        // Positions
        sb.AppendLine("        <source id=\"mesh-positions\">");
        sb.AppendLine($"          <float_array id=\"mesh-positions-array\" count=\"{data.vertices.Length * 3}\">");
        foreach (Vector3 v in data.vertices)
        {
            sb.Append(string.Format(culture, "            {0} {1} {2}\n", v.x, v.y, v.z));
        }
        sb.AppendLine("          </float_array>");
        sb.AppendLine("          <technique_common>");
        sb.AppendLine($"            <accessor source=\"#mesh-positions-array\" count=\"{data.vertices.Length}\" stride=\"3\">");
        sb.AppendLine("              <param name=\"X\" type=\"float\"/>");
        sb.AppendLine("              <param name=\"Y\" type=\"float\"/>");
        sb.AppendLine("              <param name=\"Z\" type=\"float\"/>");
        sb.AppendLine("            </accessor>");
        sb.AppendLine("          </technique_common>");
        sb.AppendLine("        </source>");
        
        // Normals
        if (data.normals.Length > 0)
        {
            sb.AppendLine("        <source id=\"mesh-normals\">");
            sb.AppendLine($"          <float_array id=\"mesh-normals-array\" count=\"{data.normals.Length * 3}\">");
            foreach (Vector3 n in data.normals)
            {
                sb.Append(string.Format(culture, "            {0} {1} {2}\n", n.x, n.y, n.z));
            }
            sb.AppendLine("          </float_array>");
            sb.AppendLine("          <technique_common>");
            sb.AppendLine($"            <accessor source=\"#mesh-normals-array\" count=\"{data.normals.Length}\" stride=\"3\">");
            sb.AppendLine("              <param name=\"X\" type=\"float\"/>");
            sb.AppendLine("              <param name=\"Y\" type=\"float\"/>");
            sb.AppendLine("              <param name=\"Z\" type=\"float\"/>");
            sb.AppendLine("            </accessor>");
            sb.AppendLine("          </technique_common>");
            sb.AppendLine("        </source>");
        }
        
        // Vertices
        sb.AppendLine("        <vertices id=\"mesh-vertices\">");
        sb.AppendLine("          <input semantic=\"POSITION\" source=\"#mesh-positions\"/>");
        sb.AppendLine("        </vertices>");
        
        // Triangles
        sb.AppendLine("        <triangles material=\"material\" count=\"" + (data.triangles.Length / 3) + "\">");
        sb.AppendLine("          <input semantic=\"VERTEX\" source=\"#mesh-vertices\" offset=\"0\"/>");
        if (data.normals.Length > 0)
            sb.AppendLine("          <input semantic=\"NORMAL\" source=\"#mesh-normals\" offset=\"1\"/>");
        
        sb.Append("          <p>");
        for (int i = 0; i < data.triangles.Length; i++)
        {
            if (data.normals.Length > 0)
                sb.Append($"{data.triangles[i]} {data.triangles[i]} ");
            else
                sb.Append($"{data.triangles[i]} ");
        }
        sb.AppendLine("</p>");
        sb.AppendLine("        </triangles>");
        
        sb.AppendLine("      </mesh>");
        sb.AppendLine("    </geometry>");
        sb.AppendLine("  </library_geometries>");
        
        // Bones library (if bones exist)
        if (data.boneNames.Length > 0)
        {
            sb.AppendLine("  <library_visual_scenes>");
            sb.AppendLine("    <visual_scene id=\"Scene\" name=\"Scene\">");
            
            // Write bone hierarchy
            for (int i = 0; i < data.boneNames.Length; i++)
            {
                if (data.boneParents[i] == -1) // Root bones
                {
                    WriteBoneNode(sb, data, i, "      ", culture);
                }
            }
            
            sb.AppendLine("    </visual_scene>");
            sb.AppendLine("  </library_visual_scenes>");
        }
        
        sb.AppendLine("  <scene>");
        sb.AppendLine("    <instance_visual_scene url=\"#Scene\"/>");
        sb.AppendLine("  </scene>");
        sb.AppendLine("</COLLADA>");

        File.WriteAllText(filePath, sb.ToString());
        Debug.Log($"Mesh with bones exported to COLLADA: {filePath}");
    }

    private static void WriteBoneNode(StringBuilder sb, MeshExportData data, int boneIndex, string indent, CultureInfo culture)
    {
        string boneName = data.boneNames[boneIndex];
        Vector3 pos = data.bonePositions[boneIndex];
        Quaternion rot = data.boneRotations[boneIndex];
        Vector3 euler = rot.eulerAngles;

        sb.AppendLine($"{indent}<node id=\"{boneName}\" name=\"{boneName}\" type=\"JOINT\">");
        sb.AppendLine(string.Format(culture, "{0}  <translate>{1} {2} {3}</translate>", indent, pos.x, pos.y, pos.z));
        sb.AppendLine(string.Format(culture, "{0}  <rotate sid=\"rotateZ\">0 0 1 {1}</rotate>", indent, euler.z));
        sb.AppendLine(string.Format(culture, "{0}  <rotate sid=\"rotateY\">0 1 0 {1}</rotate>", indent, euler.y));
        sb.AppendLine(string.Format(culture, "{0}  <rotate sid=\"rotateX\">1 0 0 {1}</rotate>", indent, euler.x));
        
        // Write children
        for (int i = 0; i < data.boneNames.Length; i++)
        {
            if (data.boneParents[i] == boneIndex)
            {
                WriteBoneNode(sb, data, i, indent + "  ", culture);
            }
        }
        
        sb.AppendLine($"{indent}</node>");
    }

    // Export to custom binary format
    public static void ExportToBinary(MeshExportData data, string filePath)
    {
        using (BinaryWriter writer = new BinaryWriter(File.Open(filePath, FileMode.Create)))
        {
            // Write vertices
            writer.Write(data.vertices.Length);
            foreach (Vector3 v in data.vertices)
            {
                writer.Write(v.x);
                writer.Write(v.y);
                writer.Write(v.z);
            }

            // Write triangles
            writer.Write(data.triangles.Length);
            foreach (int t in data.triangles)
            {
                writer.Write(t);
            }

            // Write normals
            writer.Write(data.normals.Length);
            foreach (Vector3 n in data.normals)
            {
                writer.Write(n.x);
                writer.Write(n.y);
                writer.Write(n.z);
            }

            // Write bones
            writer.Write(data.boneNames.Length);
            for (int i = 0; i < data.boneNames.Length; i++)
            {
                writer.Write(data.boneNames[i]);
                writer.Write(data.boneParents[i]);
                writer.Write(data.bonePositions[i].x);
                writer.Write(data.bonePositions[i].y);
                writer.Write(data.bonePositions[i].z);
            }
        }

        Debug.Log($"Mesh with bones exported to binary: {filePath}");
    }
}