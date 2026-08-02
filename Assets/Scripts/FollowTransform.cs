using UnityEngine;

// Rigidly follows another Transform every frame, without actually being
// parented to it. Used to keep decorations (eyes, ...) that are parented to a
// Node in sync with that Node's corresponding bone once a skeleton exists --
// bones are a separate, animated copy of the Node hierarchy (see
// BMeshBoneExtensions.CreateSkeleton), so a decoration left parented to its
// original Node would otherwise sit still while the mesh itself deforms with
// the bones. Reparenting instead of following was considered and rejected:
// destroying the skeleton (ClearSkeleton) would then cascade-destroy the
// decorations too, since GameObject destruction takes children down with it.
//
// CaptureOffset and SetSource are deliberately separate calls. The offset must
// be captured relative to the original, never-animated anchor (a Node) exactly
// once, at creation time -- not re-derived from this object's own current
// transform on every skeleton rebuild. A Node drag rebuilds the skeleton (see
// NodeEditController.RebuildMeshAndSkeleton) while the idle sway animation is
// still running, so by then this object's transform reflects whatever mid-sway
// pose it was last driven to; capturing from that instead of the true rest
// pose would bake in the animation displacement as a new "neutral" offset,
// drifting further away every time the skeleton gets rebuilt.
public class FollowTransform : MonoBehaviour
{
    public Transform source;

    private Vector3 capturedOffset;
    private Quaternion capturedRotationOffset;
    private bool captured;

    // Call once, right when this object is created/placed relative to `anchor`
    // (e.g. the Node an eye is parented to). Later calls are no-ops, so a
    // skeleton rebuild can safely call this again without side effects.
    public void CaptureOffset(Transform anchor)
    {
        if (captured || anchor == null)
        {
            return;
        }

        // Nodes (and bones at their bind pose) never rotate in this project, so
        // the offset can be captured directly in world space with no rotation math.
        capturedOffset = transform.position - anchor.position;
        capturedRotationOffset = transform.rotation;
        captured = true;
    }

    // Points this object at a (possibly new, e.g. after a skeleton rebuild) bone
    // to follow. Does not touch the captured offset.
    public void SetSource(Transform newSource)
    {
        source = newSource;
    }

    void LateUpdate()
    {
        if (!captured || source == null)
        {
            return;
        }

        transform.SetPositionAndRotation(source.position + source.rotation * capturedOffset, source.rotation * capturedRotationOffset);
    }
}
