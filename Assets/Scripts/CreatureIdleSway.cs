using UnityEngine;

// The one actual motion source for the secondary-motion rig (see
// BMeshBoneExtensions.AddSecondaryMotionRig): motion on the root bone.
// Everything downstream (DampedTransform chain) reacts to this with lag, which
// is what makes the rest of the creature visibly wobble. Runtime-only (not
// [ExecuteInEditMode]) -- this is meant to be seen in Play mode.
//
// "Walking" isn't a real gait -- there's no semantic notion of "leg" in the
// random topology to cycle a step on -- it's the same sway mechanism with
// faster, more pronounced parameters plus a vertical bob, giving a distinctly
// different feel from "Idle" without pretending to simulate footfalls.
public class CreatureIdleSway : MonoBehaviour
{
    public Transform rootBone;
    public float swayAmplitude = 8f; // degrees
    public float swaySpeed = 1.2f;
    public float bobAmplitude = 0f; // world units, 0 = no vertical bob

    private Quaternion baseRotation;
    private Vector3 baseLocalPosition;
    private float phase;

    public void ConfigureForMode(CreatureGenerator.AnimationMode mode)
    {
        switch (mode)
        {
            case CreatureGenerator.AnimationMode.Walking:
                swayAmplitude = 14f;
                swaySpeed = 3f;
                bobAmplitude = 0.15f;
                break;

            case CreatureGenerator.AnimationMode.Idle:
            default:
                swayAmplitude = 8f;
                swaySpeed = 1.2f;
                bobAmplitude = 0f;
                break;
        }
    }

    void Start()
    {
        if (rootBone != null)
        {
            baseRotation = rootBone.localRotation;
            baseLocalPosition = rootBone.localPosition;
        }
        phase = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        if (rootBone == null)
        {
            return;
        }

        float angle = Mathf.Sin(Time.time * swaySpeed + phase) * swayAmplitude;
        rootBone.localRotation = baseRotation * Quaternion.Euler(0f, angle, angle * 0.5f);

        if (bobAmplitude > 0f)
        {
            float bob = Mathf.Abs(Mathf.Sin(Time.time * swaySpeed * 2f + phase)) * bobAmplitude;
            rootBone.localPosition = baseLocalPosition + Vector3.up * bob;
        }
    }
}
