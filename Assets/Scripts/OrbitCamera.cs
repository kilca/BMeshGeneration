using UnityEngine;

// Orbit camera for inspecting generated creatures in Play mode: hold the right
// mouse button to orbit, scroll to zoom. Uses the legacy Input Manager (this
// project has no Input System package installed). Right mouse button (rather
// than left) so left-click stays free for node selection/dragging
// (NodeEditController) and for UI Toolkit buttons.
public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 8f;
    public float minDistance = 2f;
    public float maxDistance = 30f;
    public float rotationSpeed = 3f;
    public float zoomSpeed = 4f;
    public float startPitch = 20f;

    [Tooltip("Flat solid-color background instead of Unity's default procedural skybox -- a dark neutral charcoal.")]
    public Color backgroundColor = new Color(0.09f, 0.09f, 0.105f);

    [Tooltip("How much farther than a subject's bounding radius Frame() pulls the camera back -- higher leaves more breathing room around the edges.")]
    public float framingPadding = 2.2f;

    private float yaw;
    private float pitch;
    private Vector3 framedPivot;

    void Start()
    {
        pitch = startPitch;

        Camera cam = GetComponent<Camera>();
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
        }
    }

    void LateUpdate()
    {
        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * rotationSpeed;
            pitch -= Input.GetAxis("Mouse Y") * rotationSpeed;
            pitch = Mathf.Clamp(pitch, -80f, 80f);
        }

        // Don't zoom when the wheel is being used to scroll a UI panel.
        if (!CreatureGeneratorUI.PointerOverPanel)
        {
            distance -= Input.mouseScrollDelta.y * zoomSpeed;
        }
        distance = Mathf.Clamp(distance, minDistance, maxDistance);

        Vector3 pivot = target != null ? target.position : framedPivot;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * distance, rotation);
    }

    // Points the pivot at `center` and sets distance so a subject of the
    // given bounding radius comfortably fits in view -- called after
    // Generate() so a creature is framed automatically instead of possibly
    // ending up outside the default view distance. `followTarget`, if given, keeps the pivot
    // tracking that transform afterwards (e.g. so it stays centered through
    // later node edits); leave null for a fixed point (there's no single
    // transform to follow for a whole batch).
    public void Frame(Vector3 center, float radius, Transform followTarget = null)
    {
        target = followTarget;
        framedPivot = center;
        distance = Mathf.Clamp(Mathf.Max(radius, 0.5f) * framingPadding, minDistance, maxDistance);
    }

    // Bounding sphere (as a Bounds, for its center+extents) of every Node
    // under `root` -- used by Frame() to size the camera pull-back to
    // whatever was actually generated, rather than a guessed fixed distance.
    public static Bounds ComputeNodeBounds(GameObject root)
    {
        Node[] nodes = root.GetComponentsInChildren<Node>();
        if (nodes.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.one);
        }

        Bounds bounds = new Bounds(nodes[0].transform.position, Vector3.zero);
        foreach (Node node in nodes)
        {
            bounds.Encapsulate(node.transform.position);
        }
        return bounds;
    }
}
