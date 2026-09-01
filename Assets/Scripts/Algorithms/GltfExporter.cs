using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Minimal hand-rolled glTF 2.0 (.glb) writer: mesh + optional skin (skeleton,
// bind matrices, skin weights) + optional looping animation (CreatureMotion).
// No package dependency -- same spirit as the OBJ/COLLADA writers in
// MeshExportData.cs. Unity's left-handed space (+Z forward) is converted to
// glTF's right-handed space (-Z forward) by mirroring the X axis.
public static class GltfExporter
{
    // Mirror X: reflection, so it also flips triangle winding (handled below).
    private static readonly Matrix4x4 Mirror = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));

    private static Vector3 P(Vector3 v) => new Vector3(-v.x, v.y, v.z);

    private static Quaternion Qn(Quaternion q)
    {
        q = new Quaternion(q.x, -q.y, -q.z, q.w);
        float len = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (len < 1e-6f)
        {
            return Quaternion.identity;
        }
        return new Quaternion(q.x / len, q.y / len, q.z / len, q.w / len);
    }

    // joints / bindPoses / motion / vertexColors may be null.
    public static byte[] BuildGlb(Mesh mesh, Transform[] joints, Matrix4x4[] bindPoses, CreatureMotion.Data motion, Color[] vertexColors, Color baseColor, string name)
    {
        if (mesh == null)
        {
            throw new Exception("No mesh to export.");
        }

        Vector3[] verts = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;
        int[] tris = mesh.triangles;
        BoneWeight[] weights = mesh.boneWeights;
        bool hasColors = vertexColors != null && vertexColors.Length == verts.Length;

        bool skinned = joints != null && joints.Length > 0
                       && bindPoses != null && bindPoses.Length == joints.Length
                       && weights != null && weights.Length == verts.Length;

        Builder b = new Builder();

        // ---- geometry ----
        int posAcc = b.Accessor(FlattenP(verts), 5126, verts.Length, "VEC3", 34962, Bounds(verts, out float[] bmin, out float[] bmax) ? bmin : null, bmax);

        int normAcc = -1;
        if (normals != null && normals.Length == verts.Length)
        {
            normAcc = b.Accessor(FlattenDir(normals), 5126, normals.Length, "VEC3", 34962);
        }

        int uvAcc = -1;
        if (uvs != null && uvs.Length == verts.Length)
        {
            float[] uvFlat = new float[uvs.Length * 2];
            for (int i = 0; i < uvs.Length; i++)
            {
                uvFlat[i * 2] = uvs[i].x;
                uvFlat[i * 2 + 1] = 1f - uvs[i].y; // glTF UV origin is top-left
            }
            uvAcc = b.Accessor(uvFlat, 5126, uvs.Length, "VEC2", 34962);
        }

        int colAcc = -1;
        if (hasColors)
        {
            float[] colFlat = new float[verts.Length * 4];
            for (int i = 0; i < verts.Length; i++)
            {
                Color c = vertexColors[i];
                colFlat[i * 4 + 0] = Mathf.Clamp01(c.r);
                colFlat[i * 4 + 1] = Mathf.Clamp01(c.g);
                colFlat[i * 4 + 2] = Mathf.Clamp01(c.b);
                colFlat[i * 4 + 3] = 1f;
            }
            colAcc = b.Accessor(colFlat, 5126, verts.Length, "VEC4", 34962);
        }

        uint[] idx = new uint[tris.Length];
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            idx[t] = (uint)tris[t];
            idx[t + 1] = (uint)tris[t + 2]; // swap 2 & 3 -- mirror flips winding
            idx[t + 2] = (uint)tris[t + 1];
        }
        int idxAcc = b.Accessor(idx, tris.Length, "SCALAR", 34963);

        // ---- skin ----
        int jointsAcc = -1, weightsAcc = -1, ibmAcc = -1;
        if (skinned)
        {
            ushort[] j = new ushort[verts.Length * 4];
            float[] w = new float[verts.Length * 4];
            int last = joints.Length - 1;
            for (int i = 0; i < verts.Length; i++)
            {
                BoneWeight bw = weights[i];
                j[i * 4 + 0] = (ushort)Mathf.Clamp(bw.boneIndex0, 0, last);
                j[i * 4 + 1] = (ushort)Mathf.Clamp(bw.boneIndex1, 0, last);
                j[i * 4 + 2] = (ushort)Mathf.Clamp(bw.boneIndex2, 0, last);
                j[i * 4 + 3] = (ushort)Mathf.Clamp(bw.boneIndex3, 0, last);

                float sum = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
                if (sum <= 0f)
                {
                    w[i * 4] = 1f;
                }
                else
                {
                    w[i * 4 + 0] = bw.weight0 / sum;
                    w[i * 4 + 1] = bw.weight1 / sum;
                    w[i * 4 + 2] = bw.weight2 / sum;
                    w[i * 4 + 3] = bw.weight3 / sum;
                }

                // glTF: a joint index paired with a zero weight should be 0.
                for (int c = 0; c < 4; c++)
                {
                    if (w[i * 4 + c] == 0f)
                    {
                        j[i * 4 + c] = 0;
                    }
                }
            }
            jointsAcc = b.Accessor(j, verts.Length, "VEC4", 34962);
            weightsAcc = b.Accessor(w, 5126, verts.Length, "VEC4", 34962);

            float[] ibm = new float[bindPoses.Length * 16];
            for (int i = 0; i < bindPoses.Length; i++)
            {
                WriteColMajor(ibm, i * 16, Mirror * bindPoses[i] * Mirror);
            }
            ibmAcc = b.Accessor(ibm, 5126, bindPoses.Length, "MAT4", null);
        }

        // ---- nodes ----
        List<object> nodes = new List<object>();
        Dictionary<string, object> meshNode = new Dictionary<string, object> { ["name"] = "mesh", ["mesh"] = 0 };
        nodes.Add(meshNode);
        List<int> sceneNodes = new List<int> { 0 };

        List<int> jointNodeIndex = null;
        Dictionary<Transform, int> jointOf = null;
        if (skinned)
        {
            jointOf = new Dictionary<Transform, int>();
            for (int i = 0; i < joints.Length; i++)
            {
                jointOf[joints[i]] = i;
            }

            jointNodeIndex = new List<int>(joints.Length);
            for (int i = 0; i < joints.Length; i++)
            {
                Transform bone = joints[i];
                Matrix4x4 global = Mirror * Matrix4x4.TRS(bone.position, bone.rotation, Vector3.one) * Mirror;
                Matrix4x4 parentGlobal = bone.parent != null && jointOf.ContainsKey(bone.parent)
                    ? Mirror * Matrix4x4.TRS(bone.parent.position, bone.parent.rotation, Vector3.one) * Mirror
                    : Matrix4x4.identity;
                Matrix4x4 local = parentGlobal.inverse * global;

                Vector3 tr = local.GetColumn(3);
                Quaternion rot = local.rotation;

                nodes.Add(new Dictionary<string, object>
                {
                    ["name"] = bone.name,
                    ["translation"] = new[] { tr.x, tr.y, tr.z },
                    ["rotation"] = new[] { rot.x, rot.y, rot.z, rot.w },
                });
                jointNodeIndex.Add(nodes.Count - 1);
            }

            for (int i = 0; i < joints.Length; i++)
            {
                List<int> kids = new List<int>();
                for (int c = 0; c < joints.Length; c++)
                {
                    if (joints[c].parent == joints[i])
                    {
                        kids.Add(jointNodeIndex[c]);
                    }
                }
                if (kids.Count > 0)
                {
                    ((Dictionary<string, object>)nodes[jointNodeIndex[i]])["children"] = kids;
                }
                else if (joints[i].parent == null || !jointOf.ContainsKey(joints[i].parent))
                {
                    sceneNodes.Add(jointNodeIndex[i]);
                }
            }
            // second pass caught leaf roots too; make sure every non-joint-parented joint is a scene node
            for (int i = 0; i < joints.Length; i++)
            {
                bool rooted = joints[i].parent == null || !jointOf.ContainsKey(joints[i].parent);
                if (rooted && !sceneNodes.Contains(jointNodeIndex[i]))
                {
                    sceneNodes.Add(jointNodeIndex[i]);
                }
            }

            meshNode["skin"] = 0;
        }

        // ---- mesh + material ----
        Dictionary<string, object> attributes = new Dictionary<string, object> { ["POSITION"] = posAcc };
        if (normAcc >= 0) attributes["NORMAL"] = normAcc;
        if (uvAcc >= 0) attributes["TEXCOORD_0"] = uvAcc;
        if (colAcc >= 0) attributes["COLOR_0"] = colAcc;
        if (skinned)
        {
            attributes["JOINTS_0"] = jointsAcc;
            attributes["WEIGHTS_0"] = weightsAcc;
        }

        Dictionary<string, object> primitive = new Dictionary<string, object>
        {
            ["attributes"] = attributes,
            ["indices"] = idxAcc,
            ["mode"] = 4,
            ["material"] = 0,
        };

        Dictionary<string, object> gltf = new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, object> { ["version"] = "2.0", ["generator"] = "BMeshGeneration GltfExporter" },
            ["scene"] = 0,
            ["scenes"] = new List<object> { new Dictionary<string, object> { ["nodes"] = sceneNodes } },
            ["nodes"] = nodes,
            ["meshes"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["name"] = string.IsNullOrEmpty(name) ? "Creature" : name,
                    ["primitives"] = new List<object> { primitive },
                }
            },
            ["materials"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["name"] = "CreatureSkin",
                    ["pbrMetallicRoughness"] = new Dictionary<string, object>
                    {
                        // white when COLOR_0 carries the baked pattern (glTF multiplies the two)
                        ["baseColorFactor"] = hasColors
                            ? new[] { 1f, 1f, 1f, 1f }
                            : new[] { baseColor.r, baseColor.g, baseColor.b, 1f },
                        ["metallicFactor"] = 0f,
                        ["roughnessFactor"] = 0.85f,
                    },
                    ["doubleSided"] = true,
                }
            },
            ["accessors"] = b.Accessors,
            ["bufferViews"] = b.BufferViews,
        };

        // ---- animation ----
        if (motion != null && skinned && motion.tracks.Count > 0)
        {
            int timeAcc = b.Accessor(motion.times, motion.times.Length, "SCALAR", null, new[] { 0f }, new[] { motion.duration });

            List<object> samplers = new List<object>();
            List<object> channels = new List<object>();

            foreach (CreatureMotion.BoneTrack track in motion.tracks)
            {
                if (track.bone == null || !jointOf.ContainsKey(track.bone))
                {
                    continue;
                }
                int nodeIdx = jointNodeIndex[jointOf[track.bone]];

                float[] rot = new float[track.localRotation.Length * 4];
                for (int k = 0; k < track.localRotation.Length; k++)
                {
                    Quaternion q = Qn(track.localRotation[k]);
                    rot[k * 4 + 0] = q.x;
                    rot[k * 4 + 1] = q.y;
                    rot[k * 4 + 2] = q.z;
                    rot[k * 4 + 3] = q.w;
                }
                int rotAcc = b.Accessor(rot, track.localRotation.Length, "VEC4", null);
                samplers.Add(new Dictionary<string, object> { ["input"] = timeAcc, ["output"] = rotAcc, ["interpolation"] = "LINEAR" });
                channels.Add(new Dictionary<string, object>
                {
                    ["sampler"] = samplers.Count - 1,
                    ["target"] = new Dictionary<string, object> { ["node"] = nodeIdx, ["path"] = "rotation" },
                });

                if (track.localPosition != null)
                {
                    float[] pos = new float[track.localPosition.Length * 3];
                    for (int k = 0; k < track.localPosition.Length; k++)
                    {
                        Vector3 p = P(track.localPosition[k]);
                        pos[k * 3 + 0] = p.x;
                        pos[k * 3 + 1] = p.y;
                        pos[k * 3 + 2] = p.z;
                    }
                    int posAccA = b.Accessor(pos, track.localPosition.Length, "VEC3", null);
                    samplers.Add(new Dictionary<string, object> { ["input"] = timeAcc, ["output"] = posAccA, ["interpolation"] = "LINEAR" });
                    channels.Add(new Dictionary<string, object>
                    {
                        ["sampler"] = samplers.Count - 1,
                        ["target"] = new Dictionary<string, object> { ["node"] = nodeIdx, ["path"] = "translation" },
                    });
                }
            }

            if (channels.Count > 0)
            {
                gltf["animations"] = new List<object>
                {
                    new Dictionary<string, object> { ["name"] = "Idle", ["samplers"] = samplers, ["channels"] = channels }
                };
            }
        }

        if (skinned)
        {
            gltf["skins"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["inverseBindMatrices"] = ibmAcc,
                    ["skeleton"] = jointNodeIndex[0],
                    ["joints"] = jointNodeIndex,
                }
            };
        }

        byte[] bin = b.Bin.ToArray();
        gltf["buffers"] = new List<object> { new Dictionary<string, object> { ["byteLength"] = bin.Length } };

        return Assemble(MiniJson.Serialize(gltf), bin);
    }

    // ---- buffer / accessor builder ----

    private class Builder
    {
        public readonly List<byte> Bin = new List<byte>();
        public readonly List<object> BufferViews = new List<object>();
        public readonly List<object> Accessors = new List<object>();

        private int AddView(byte[] data, int? target)
        {
            while (Bin.Count % 4 != 0)
            {
                Bin.Add(0);
            }
            int offset = Bin.Count;
            Bin.AddRange(data);

            Dictionary<string, object> view = new Dictionary<string, object>
            {
                ["buffer"] = 0,
                ["byteOffset"] = offset,
                ["byteLength"] = data.Length,
            };
            if (target.HasValue)
            {
                view["target"] = target.Value;
            }
            BufferViews.Add(view);
            return BufferViews.Count - 1;
        }

        private int Add(byte[] data, int componentType, int count, string type, int? target, float[] min, float[] max)
        {
            int view = AddView(data, target);
            Dictionary<string, object> acc = new Dictionary<string, object>
            {
                ["bufferView"] = view,
                ["componentType"] = componentType,
                ["count"] = count,
                ["type"] = type,
            };
            if (min != null) acc["min"] = min;
            if (max != null) acc["max"] = max;
            Accessors.Add(acc);
            return Accessors.Count - 1;
        }

        public int Accessor(float[] data, int componentType, int count, string type, int? target, float[] min = null, float[] max = null)
            => Add(ToBytes(data), componentType, count, type, target, min, max);

        // float SCALAR helper with min/max
        public int Accessor(float[] data, int count, string type, int? target, float[] min = null, float[] max = null)
            => Add(ToBytes(data), 5126, count, type, target, min, max);

        public int Accessor(uint[] data, int count, string type, int? target)
            => Add(ToBytes(data), 5125, count, type, target, null, null);

        public int Accessor(ushort[] data, int count, string type, int? target)
            => Add(ToBytes(data), 5123, count, type, target, null, null);
    }

    private static byte[] ToBytes(float[] a)
    {
        byte[] b = new byte[a.Length * 4];
        Buffer.BlockCopy(a, 0, b, 0, b.Length);
        return b;
    }

    private static byte[] ToBytes(uint[] a)
    {
        byte[] b = new byte[a.Length * 4];
        Buffer.BlockCopy(a, 0, b, 0, b.Length);
        return b;
    }

    private static byte[] ToBytes(ushort[] a)
    {
        byte[] b = new byte[a.Length * 2];
        Buffer.BlockCopy(a, 0, b, 0, b.Length);
        return b;
    }

    private static float[] FlattenP(Vector3[] v)
    {
        float[] f = new float[v.Length * 3];
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = P(v[i]);
            f[i * 3] = p.x;
            f[i * 3 + 1] = p.y;
            f[i * 3 + 2] = p.z;
        }
        return f;
    }

    private static float[] FlattenDir(Vector3[] v)
    {
        float[] f = new float[v.Length * 3];
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 d = P(v[i]); // direction mirrors the same way as position for an X-flip
            f[i * 3] = d.x;
            f[i * 3 + 1] = d.y;
            f[i * 3 + 2] = d.z;
        }
        return f;
    }

    private static bool Bounds(Vector3[] v, out float[] min, out float[] max)
    {
        min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
        max = new[] { float.MinValue, float.MinValue, float.MinValue };
        if (v.Length == 0)
        {
            min = max = null;
            return false;
        }
        foreach (Vector3 raw in v)
        {
            Vector3 p = P(raw);
            min[0] = Mathf.Min(min[0], p.x); min[1] = Mathf.Min(min[1], p.y); min[2] = Mathf.Min(min[2], p.z);
            max[0] = Mathf.Max(max[0], p.x); max[1] = Mathf.Max(max[1], p.y); max[2] = Mathf.Max(max[2], p.z);
        }
        return true;
    }

    private static void WriteColMajor(float[] dst, int at, Matrix4x4 m)
    {
        for (int col = 0; col < 4; col++)
        {
            for (int row = 0; row < 4; row++)
            {
                dst[at + col * 4 + row] = m[row, col];
            }
        }
    }

    // ---- triplanar -> vertex colours ----
    //
    // The creature mesh has no UV unwrap (see CreatureMaterialGenerator) -- it's
    // shaded by Custom/TriplanarCreature projecting a texture from the baked rest
    // position. There's nothing to export as a UV map, so instead we sample that
    // same projection per vertex and bake it into COLOR_0 so the pattern survives
    // into Blender / three.js. Returns null if there's no texture to sample.
    public static Color[] BakeTriplanarVertexColors(Mesh mesh, Material material)
    {
        if (mesh == null || material == null || material.mainTexture == null)
        {
            return null;
        }

        Texture2D tex = MakeReadable(material.mainTexture);
        if (tex == null)
        {
            return null;
        }

        float texScale = material.HasProperty("_TexScale") ? material.GetFloat("_TexScale") : 1f;
        float sharp = material.HasProperty("_BlendSharpness") ? material.GetFloat("_BlendSharpness") : 4f;

        int vc = mesh.vertexCount;
        Vector3[] normals = mesh.normals;
        Vector2[] uv2 = mesh.uv2;
        Vector2[] uv3 = mesh.uv3;
        bool haveRest = uv2 != null && uv2.Length == vc && uv3 != null && uv3.Length == vc;
        Vector3[] verts = haveRest ? null : mesh.vertices;

        Color[] cols = new Color[vc];
        for (int i = 0; i < vc; i++)
        {
            Vector3 rp = haveRest ? new Vector3(uv2[i].x, uv2[i].y, uv3[i].x) : verts[i];
            Vector3 nrm = normals != null && normals.Length == vc ? normals[i] : Vector3.up;
            cols[i] = TriplanarSample(tex, rp * texScale, nrm, sharp);
        }

        if (tex != material.mainTexture)
        {
            UnityEngine.Object.Destroy(tex);
        }
        return cols;
    }

    private static Color TriplanarSample(Texture2D t, Vector3 p, Vector3 n, float sharp)
    {
        float bx = Mathf.Pow(Mathf.Abs(n.x), sharp);
        float by = Mathf.Pow(Mathf.Abs(n.y), sharp);
        float bz = Mathf.Pow(Mathf.Abs(n.z), sharp);
        float sum = Mathf.Max(bx + by + bz, 1e-5f);
        bx /= sum; by /= sum; bz /= sum;

        Color cx = t.GetPixelBilinear(p.y, p.z);
        Color cy = t.GetPixelBilinear(p.x, p.z);
        Color cz = t.GetPixelBilinear(p.x, p.y);
        return new Color(
            cx.r * bx + cy.r * by + cz.r * bz,
            cx.g * bx + cy.g * by + cz.g * bz,
            cx.b * bx + cy.b * by + cz.b * bz,
            1f);
    }

    private static Texture2D MakeReadable(Texture src)
    {
        if (src is Texture2D t2 && t2.isReadable)
        {
            return t2;
        }

        RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return readable;
    }

    // ---- GLB container ----

    private static byte[] Assemble(string json, byte[] bin)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        int jsonPad = (4 - jsonBytes.Length % 4) % 4;
        int binPad = (4 - bin.Length % 4) % 4;
        int total = 12 + 8 + jsonBytes.Length + jsonPad + 8 + bin.Length + binPad;

        using MemoryStream ms = new MemoryStream(total);
        using BinaryWriter w = new BinaryWriter(ms);

        w.Write(0x46546C67u); // "glTF"
        w.Write(2u);
        w.Write((uint)total);

        w.Write((uint)(jsonBytes.Length + jsonPad));
        w.Write(0x4E4F534Au); // "JSON"
        w.Write(jsonBytes);
        for (int i = 0; i < jsonPad; i++)
        {
            w.Write((byte)0x20);
        }

        w.Write((uint)(bin.Length + binPad));
        w.Write(0x004E4942u); // "BIN\0"
        w.Write(bin);
        for (int i = 0; i < binPad; i++)
        {
            w.Write((byte)0x00);
        }

        w.Flush();
        return ms.ToArray();
    }

    // ---- tiny JSON serializer (Dictionary<string,object> / IEnumerable / primitives) ----

    private static class MiniJson
    {
        public static string Serialize(object root)
        {
            StringBuilder sb = new StringBuilder(4096);
            Write(sb, root);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object o)
        {
            switch (o)
            {
                case null:
                    sb.Append("null");
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case float f:
                    WriteNumber(sb, f);
                    break;
                case double d:
                    WriteNumber(sb, (float)d);
                    break;
                case IDictionary<string, object> map:
                    sb.Append('{');
                    bool firstKey = true;
                    foreach (KeyValuePair<string, object> kv in map)
                    {
                        if (!firstKey) sb.Append(',');
                        firstKey = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable seq:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (object item in seq)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                default:
                    throw new Exception($"glTF JSON: unsupported value type {o.GetType()}");
            }
        }

        private static void WriteNumber(StringBuilder sb, float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f))
            {
                throw new Exception("glTF JSON: non-finite number");
            }
            sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
