using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Single source of the creature's idle / walking motion. The topology is random,
// so there is nothing to hand-key -- the motion is procedural but fully baked
// into keyframes here so both consumers read identical data:
//   * CreatureIdleSway builds a runtime AnimationClip from it (BuildClip)
//   * GltfExporter writes the same keyframes into the exported .glb
//
// The skeleton is split into a "spine" (the straightest / heaviest chain from
// the root) and "limbs" (everything that branches off it). Idle: gentle sway,
// a lazy wave down each limb. Walking: each limb does a real fore-aft step
// swing, limbs alternating in phase, deeper bones trailing (knee/elbow flex),
// with a body bob + roll synced to the steps.
public static class CreatureMotion
{
    private static readonly Vector3 BendAxis = new Vector3(1f, 0f, 0.6f).normalized;
    private static readonly Vector3 StepAxis = Vector3.right; // fore-aft swing

    public class BoneTrack
    {
        public Transform bone;
        public int jointIndex;                 // index into SkinnedMeshRenderer.bones (-1 if not skinned)
        public string path;                    // transform path relative to the animator root
        public Quaternion[] localRotation;     // length sampleCount + 1 (closing key repeats key 0)
        public Vector3[] localPosition;        // null unless this is the root bone with a bob
        public bool isRoot;

        // topology metadata (filled during the traversal)
        internal bool isSpine;
        internal int limbId;                   // -1 for the spine
        internal int limbDepth;                // 0 at the first bone of a limb
        internal bool legLike;                 // limb points downward -> steps big
        internal float limbPhaseBase;          // step phase for this limb (walk mode)
    }

    public class Data
    {
        public float duration;
        public float[] times;                  // length sampleCount + 1
        public readonly List<BoneTrack> tracks = new List<BoneTrack>();
    }

    private struct Settings
    {
        public float duration;
        public int sampleCount;
        public float rootSwayDeg;
        public float limbSwayDeg;
        public float wavePhaseStep;
        public float bob;
        public float walkLegDeg;
        public float walkArmDeg;
        public float walkTrail;
        public float walkRollDeg;
        public bool walking;
    }

    private static Settings SettingsFor(CreatureGenerator.AnimationMode mode)
    {
        if (mode == CreatureGenerator.AnimationMode.Walking)
        {
            return new Settings
            {
                duration = 1.15f, sampleCount = 28, walking = true,
                rootSwayDeg = 3f, bob = 0.14f, walkRollDeg = 5f,
                walkLegDeg = 34f, walkArmDeg = 18f, walkTrail = 1.1f,
                limbSwayDeg = 0f, wavePhaseStep = 0f,
            };
        }
        return new Settings
        {
            duration = 3.2f, sampleCount = 24, walking = false,
            rootSwayDeg = 7f, bob = 0f, walkRollDeg = 0f,
            walkLegDeg = 0f, walkArmDeg = 0f, walkTrail = 0f,
            limbSwayDeg = 5.5f, wavePhaseStep = 0.6f,
        };
    }

    public static Data Build(
        Transform rootBone,
        Transform[] joints,
        Transform animatorRoot,
        IReadOnlyDictionary<Transform, Quaternion> restRotation,
        IReadOnlyDictionary<Transform, Vector3> restPosition,
        CreatureGenerator.AnimationMode mode)
    {
        Settings s = SettingsFor(mode);
        int n = Mathf.Max(2, s.sampleCount);

        Data data = new Data { duration = s.duration, times = new float[n + 1] };
        for (int k = 0; k <= n; k++)
        {
            data.times[k] = s.duration * k / n;
        }

        Dictionary<Transform, int> jointIndex = new Dictionary<Transform, int>();
        if (joints != null)
        {
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null)
                {
                    jointIndex[joints[i]] = i;
                }
            }
        }

        // pass 1: walk the skeleton, classify spine vs limbs, create tracks
        int nextLimbId = 0;
        Classify(rootBone, animatorRoot, jointIndex, isSpine: true, limbId: -1, limbDepth: 0, legLike: false, ref nextLimbId, data);

        // pass 2: alternate the step phase per limb, legs and arms counted apart
        // (so left/right of a pair land opposite, whatever discovery order they had)
        Dictionary<int, float> phaseByLimb = new Dictionary<int, float>();
        int legN = 0, armN = 0;
        foreach (BoneTrack tr in data.tracks)
        {
            if (tr.isSpine || tr.limbDepth != 0)
            {
                continue;
            }
            int idx = tr.legLike ? legN++ : armN++;
            phaseByLimb[tr.limbId] = (idx % 2 == 0) ? 0f : Mathf.PI;
        }
        foreach (BoneTrack tr in data.tracks)
        {
            if (!tr.isSpine && phaseByLimb.TryGetValue(tr.limbId, out float ph))
            {
                tr.limbPhaseBase = ph;
            }
        }

        // pass 3: fill keyframes
        foreach (BoneTrack tr in data.tracks)
        {
            FillKeys(tr, n, s, restRotation, restPosition);
        }

        return data;
    }

    private static void Classify(
        Transform bone, Transform animatorRoot, Dictionary<Transform, int> jointIndex,
        bool isSpine, int limbId, int limbDepth, bool legLike, ref int nextLimbId,
        Data data)
    {
        data.tracks.Add(new BoneTrack
        {
            bone = bone,
            jointIndex = jointIndex.TryGetValue(bone, out int ji) ? ji : -1,
            path = RelativePath(bone, animatorRoot),
            isRoot = isSpine && limbDepth == 0,
            isSpine = isSpine,
            limbId = limbId,
            limbDepth = limbDepth,
            legLike = legLike,
        });

        if (bone.childCount == 0)
        {
            return;
        }

        // The spine continues into whichever child keeps growing in roughly the
        // same direction; every child that clearly diverges is a limb. A radial
        // body (all children fanning outward, e.g. an octopus) therefore has NO
        // spine continuation -- every arm is a limb and steps.
        int spineChild = -1;
        if (isSpine)
        {
            Vector3 fwd = bone.parent != null ? bone.position - bone.parent.position : Vector3.up;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = Vector3.up;
            }
            fwd = fwd.normalized;

            float bestDot = 0.55f; // must stay within ~55 deg of the incoming direction
            for (int i = 0; i < bone.childCount; i++)
            {
                Vector3 cd = bone.GetChild(i).position - bone.position;
                if (cd.sqrMagnitude < 1e-6f)
                {
                    continue;
                }
                float dot = Vector3.Dot(cd.normalized, fwd);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    spineChild = i;
                }
            }
        }

        for (int i = 0; i < bone.childCount; i++)
        {
            Transform child = bone.GetChild(i);
            if (isSpine && i == spineChild)
            {
                Classify(child, animatorRoot, jointIndex, true, -1, limbDepth + 1, false, ref nextLimbId, data);
            }
            else if (isSpine)
            {
                // limb direction from where it branches off the spine
                Vector3 d = child.position - bone.position;
                bool leg = d.sqrMagnitude > 1e-6f && d.normalized.y < -0.1f;
                Classify(child, animatorRoot, jointIndex, false, nextLimbId++, 0, leg, ref nextLimbId, data);
            }
            else
            {
                Classify(child, animatorRoot, jointIndex, false, limbId, limbDepth + 1, legLike, ref nextLimbId, data);
            }
        }
    }

    private static void FillKeys(
        BoneTrack tr, int n, Settings s,
        IReadOnlyDictionary<Transform, Quaternion> restRotation,
        IReadOnlyDictionary<Transform, Vector3> restPosition)
    {
        Quaternion rest = restRotation != null && restRotation.TryGetValue(tr.bone, out Quaternion rr) ? rr : tr.bone.localRotation;

        tr.localRotation = new Quaternion[n + 1];

        for (int k = 0; k <= n; k++)
        {
            float phi = 2f * Mathf.PI * k / n; // key n repeats key 0
            Quaternion offset;

            if (s.walking)
            {
                if (tr.isSpine)
                {
                    if (tr.isRoot)
                    {
                        float pitch = -Mathf.Cos(2f * phi) * 2f;
                        float roll = Mathf.Sin(phi) * s.walkRollDeg;
                        float yaw = Mathf.Sin(phi) * s.rootSwayDeg;
                        offset = Quaternion.Euler(pitch, yaw, roll);
                    }
                    else
                    {
                        // travelling body wave -- keeps a limbless / worm creature moving too
                        float wave = Mathf.Sin(phi - tr.limbDepth * 0.6f) * 5f;
                        offset = Quaternion.AngleAxis(wave, StepAxis) * Quaternion.AngleAxis(-Mathf.Sin(phi) * 3f, Vector3.up);
                    }
                }
                else
                {
                    float amp = tr.legLike ? s.walkLegDeg : s.walkArmDeg;
                    float ph = tr.limbPhaseBase + (tr.legLike ? 0f : Mathf.PI); // arms counter the legs
                    if (tr.limbDepth == 0)
                    {
                        offset = Quaternion.AngleAxis(Mathf.Sin(phi + ph) * amp, StepAxis);
                    }
                    else
                    {
                        float trail = Mathf.Sin(phi + ph + tr.limbDepth * s.walkTrail) * amp * 0.5f;
                        offset = Quaternion.AngleAxis(trail, StepAxis);
                    }
                }
            }
            else // idle
            {
                if (tr.isSpine)
                {
                    float yaw = Mathf.Sin(phi + tr.limbDepth * 0.4f) * s.rootSwayDeg * (tr.isRoot ? 1f : 0.5f);
                    offset = Quaternion.Euler(0f, yaw, yaw * 0.4f);
                }
                else
                {
                    float ampScale = 1f / (1f + tr.limbDepth * 0.35f);
                    float ang = Mathf.Sin(phi + tr.limbId * 0.7f + tr.limbDepth * s.wavePhaseStep) * s.limbSwayDeg * ampScale;
                    offset = Quaternion.AngleAxis(ang, BendAxis);
                }
            }

            Quaternion q = rest * offset;
            if (k > 0 && Quaternion.Dot(q, tr.localRotation[k - 1]) < 0f)
            {
                q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            }
            tr.localRotation[k] = q;
        }

        if (tr.isRoot && s.bob > 0f)
        {
            Vector3 restPos = restPosition != null && restPosition.TryGetValue(tr.bone, out Vector3 rp) ? rp : tr.bone.localPosition;
            tr.localPosition = new Vector3[n + 1];
            for (int k = 0; k <= n; k++)
            {
                float phi = 2f * Mathf.PI * k / n;
                float up = (0.5f - 0.5f * Mathf.Cos(2f * phi)) * s.bob; // 2 rises per loop, smooth, seamless
                tr.localPosition[k] = restPos + Vector3.up * up;
            }
        }
    }

    public static string RelativePath(Transform t, Transform root)
    {
        if (t == root)
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder(t.name);
        for (Transform p = t.parent; p != null && p != root; p = p.parent)
        {
            sb.Insert(0, p.name + "/");
        }
        return sb.ToString();
    }

    // A looping AnimationClip: Transform localRotation (x/y/z/w) per bone, plus
    // localPosition for the root bone when there is a bob.
    //
    // legacy = true: AnimationClip.SetCurve only works at runtime on legacy
    // clips, so this plays through a legacy Animation component (see
    // CreatureIdleSway), not an Animator/Playable graph.
    public static AnimationClip BuildClip(Data data, string name)
    {
        AnimationClip clip = new AnimationClip { name = name, legacy = true, wrapMode = WrapMode.Loop };

        foreach (BoneTrack tr in data.tracks)
        {
            SetQuaternionCurves(clip, tr.path, data.times, tr.localRotation);

            if (tr.localPosition != null)
            {
                SetVectorCurves(clip, tr.path, data.times, tr.localPosition);
            }
        }

        clip.EnsureQuaternionContinuity();
        return clip;
    }

    private static void SetQuaternionCurves(AnimationClip clip, string path, float[] times, Quaternion[] values)
    {
        int m = times.Length;
        Keyframe[] cx = new Keyframe[m], cy = new Keyframe[m], cz = new Keyframe[m], cw = new Keyframe[m];
        for (int k = 0; k < m; k++)
        {
            Quaternion q = values[k];
            cx[k] = new Keyframe(times[k], q.x);
            cy[k] = new Keyframe(times[k], q.y);
            cz[k] = new Keyframe(times[k], q.z);
            cw[k] = new Keyframe(times[k], q.w);
        }
        clip.SetCurve(path, typeof(Transform), "localRotation.x", Smooth(cx));
        clip.SetCurve(path, typeof(Transform), "localRotation.y", Smooth(cy));
        clip.SetCurve(path, typeof(Transform), "localRotation.z", Smooth(cz));
        clip.SetCurve(path, typeof(Transform), "localRotation.w", Smooth(cw));
    }

    private static void SetVectorCurves(AnimationClip clip, string path, float[] times, Vector3[] values)
    {
        int m = times.Length;
        Keyframe[] px = new Keyframe[m], py = new Keyframe[m], pz = new Keyframe[m];
        for (int k = 0; k < m; k++)
        {
            px[k] = new Keyframe(times[k], values[k].x);
            py[k] = new Keyframe(times[k], values[k].y);
            pz[k] = new Keyframe(times[k], values[k].z);
        }
        clip.SetCurve(path, typeof(Transform), "localPosition.x", Smooth(px));
        clip.SetCurve(path, typeof(Transform), "localPosition.y", Smooth(py));
        clip.SetCurve(path, typeof(Transform), "localPosition.z", Smooth(pz));
    }

    private static AnimationCurve Smooth(Keyframe[] keys)
    {
        AnimationCurve c = new AnimationCurve(keys);
        for (int i = 0; i < c.length; i++)
        {
            c.SmoothTangents(i, 0f);
        }
        return c;
    }
}
