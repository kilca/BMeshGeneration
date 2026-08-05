using System.Collections.Generic;
using PrimeTween;
using UnityEngine;

// Manual node editing at runtime: left-click to select/drag any Node found in
// the scene, keyboard for discrete actions (resize, add, delete, force a mesh
// refresh, undo/redo). Deliberately not tied to a specific CreatureGenerator --
// it finds the enclosing BMesh via GetComponentInParent<BMesh>() and
// regenerates that, so it works on anything built from Node/BMesh
// (CreatureGenerator, ProcGen, hand-placed nodes, ...).
//
// Left mouse button is used for selection/drag (OrbitCamera uses the right
// button to orbit, so the two don't fight over input).
public class NodeEditController : MonoBehaviour
{
    public float resizeStep = 0.1f;
    public float minSize = 0.05f;
    public float maxSize = 4f;
    public float newNodeOffset = 1f;
    public float popStrength = 0.3f;
    public float popDuration = 0.25f;

    public Color selectedColor = Color.yellow;
    public Color draggingColor = new Color(1f, 0.35f, 0.1f);

    [Tooltip("Rebuild the mesh (and skeleton, if any) every frame while dragging a node, instead of only when the drag ends. More responsive, but costs a full BMesh regeneration per frame.")]
    public bool liveEditing = false;

    [Tooltip("Show a small marker on every node, not just the selected one -- useful to see what's clickable. Also on automatically while Live Node Editing is on.")]
    public bool showNodes = false;

    private Node selected;
    private GameObject marker;
    private Material markerMaterial;
    private bool dragging;
    private Plane dragPlane;
    private Vector3 dragOffset;
    private Vector3 dragStartPosition;

    private readonly Dictionary<Node, GameObject> nodeMarkers = new Dictionary<Node, GameObject>();
    private Material nodeMarkerMaterial;
    private readonly HashSet<Node> seenNodes = new HashSet<Node>();
    private readonly List<Node> staleNodes = new List<Node>();

    // ------ Undo/redo ------
    // A linear history of (undo, redo) action pairs, PrimeTween/DOTween-style:
    // each edit records how to reverse itself and how to re-apply itself, rather
    // than snapshotting the whole creature -- this keeps eyes/skeleton/whatever
    // else is hanging off untouched nodes intact across an undo, instead of
    // rebuilding everything from scratch.
    private struct UndoRecord
    {
        public System.Action undo;
        public System.Action redo;
    }

    private readonly List<UndoRecord> history = new List<UndoRecord>();
    private int historyIndex = -1;

    void Update()
    {
        Node[] allNodes = Object.FindObjectsByType<Node>(FindObjectsSortMode.None);
        EnsureCollidersOnAllNodes(allNodes);
        UpdateNodeMarkers(allNodes);
        UpdateSelectedMarkerPosition();
        HandleSelectionAndDrag();
        HandleKeyboardActions();
    }

    // Keeps the selection marker tracking the node's current (possibly
    // animated) position between frames -- HandleSelectionAndDrag only moves
    // it explicitly while actively dragging.
    void UpdateSelectedMarkerPosition()
    {
        if (selected == null || dragging || marker == null)
        {
            return;
        }

        marker.transform.position = ResolveMarkerPosition(selected);
    }

    // BMesh's own Gizmo/Vertices show modes hide the MeshRenderer and rely on
    // Node.OnDrawGizmos for feedback -- but Gizmos only render in the Scene
    // view, never in the Game view a Play-mode build/panel actually looks at,
    // so those modes appeared to just show nothing. This gives every node a
    // small always-on-top marker (same X-ray trick as the selection marker)
    // that's actually visible at runtime, shown only while showNodes or
    // liveEditing is on -- earlier this was also tied to showMode == Gizmo,
    // but AddSkeleton() itself sets that same mode (to hide the static mesh
    // behind the skinned one) on every animated creature, which is the
    // default, so markers ended up on unconditionally. Explicit toggle only.
    void UpdateNodeMarkers(Node[] allNodes)
    {
        seenNodes.Clear();

        foreach (Node node in allNodes)
        {
            if (node == selected || !(liveEditing || showNodes))
            {
                RemoveNodeMarker(node);
                continue;
            }

            if (!nodeMarkers.TryGetValue(node, out GameObject nodeMarker) || nodeMarker == null)
            {
                nodeMarker = CreateNodeMarker();
                nodeMarkers[node] = nodeMarker;
            }
            nodeMarker.transform.position = ResolveMarkerPosition(node);
            nodeMarker.transform.localScale = Vector3.one * Mathf.Max(node.size * 0.2f, 0.1f);
            seenNodes.Add(node);
        }

        staleNodes.Clear();
        foreach (Node node in nodeMarkers.Keys)
        {
            if (!seenNodes.Contains(node))
            {
                staleNodes.Add(node);
            }
        }
        foreach (Node node in staleNodes)
        {
            RemoveNodeMarker(node);
        }
    }

    // A Node's own transform stays put once a skeleton takes over the visible
    // mesh (see BMeshBoneExtensions.CreateSkeleton) -- animation moves bones,
    // not the original Node transforms -- so a marker sitting at
    // node.transform.position visibly lagged behind an animated creature.
    // Follow the matching bone (via SkeletonBoneMap) instead whenever one exists.
    Vector3 ResolveMarkerPosition(Node node)
    {
        BMesh bmesh = node.GetComponentInParent<BMesh>();
        CreatureGenerator generator = bmesh != null ? bmesh.GetComponent<CreatureGenerator>() : null;
        if (generator != null && generator.skeleton != null)
        {
            SkeletonBoneMap boneMap = generator.skeleton.GetComponent<SkeletonBoneMap>();
            Transform bone = boneMap != null ? boneMap.GetBone(node) : null;
            if (bone != null)
            {
                return bone.position;
            }
        }

        return node.transform.position;
    }

    GameObject CreateNodeMarker()
    {
        if (nodeMarkerMaterial == null)
        {
            nodeMarkerMaterial = new Material(Shader.Find("Custom/XRayMarker"));
            nodeMarkerMaterial.color = new Color(0.3f, 0.8f, 1f, 0.6f); // dim cyan, distinct from the yellow/orange selection marker
        }

        return CreateXRayMarkerSphere("NodeMarker", nodeMarkerMaterial);
    }

    // Shared by the selection marker (Select()) and per-node markers
    // (CreateNodeMarker() above): an unlit sphere using the given
    // Custom/XRayMarker material (ZTest Always, so it stays visible through
    // the creature's own mesh) with no collider of its own.
    static GameObject CreateXRayMarkerSphere(string name, Material material)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        Object.Destroy(sphere.GetComponent<Collider>());
        sphere.GetComponent<Renderer>().material = material;
        return sphere;
    }

    void RemoveNodeMarker(Node node)
    {
        if (nodeMarkers.TryGetValue(node, out GameObject nodeMarker))
        {
            if (nodeMarker != null)
            {
                Object.DestroyImmediate(nodeMarker);
            }
            nodeMarkers.Remove(node);
        }
    }

    void HandleSelectionAndDrag()
    {
        Camera cam = GetActiveCamera();
        if (cam == null)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            // Node colliders are triggers (see EnsureCollider) -- force-hit them
            // regardless of the project's global queriesHitTriggers setting.
            bool didHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Collide);
            Node hitNode = didHit ? hit.collider.GetComponentInParent<Node>() : null;

            if (hitNode != null)
            {
                Select(hitNode);
                dragging = true;
                markerMaterial.color = draggingColor;
                dragStartPosition = hitNode.transform.position;
                dragPlane = new Plane(cam.transform.forward, hitNode.transform.position);
                dragOffset = dragPlane.Raycast(ray, out float enter) ? hitNode.transform.position - ray.GetPoint(enter) : Vector3.zero;
            }
            else
            {
                Deselect();
            }
        }

        if (dragging && selected != null && Input.GetMouseButton(0))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (dragPlane.Raycast(ray, out float enter))
            {
                selected.transform.position = ray.GetPoint(enter) + dragOffset;
                marker.transform.position = selected.transform.position;
                if (liveEditing)
                {
                    RegenerateMesh(selected);
                }
            }
        }

        if (Input.GetMouseButtonUp(0) && dragging)
        {
            dragging = false;
            if (selected != null)
            {
                markerMaterial.color = selectedColor;
                RegenerateMesh(selected);
                PopFeedback(marker.transform);

                Node movedNode = selected;
                Vector3 oldPosition = dragStartPosition;
                Vector3 newPosition = movedNode.transform.position;
                if ((newPosition - oldPosition).sqrMagnitude > 0.0001f)
                {
                    RecordAction(
                        () => { movedNode.transform.position = oldPosition; RegenerateMesh(movedNode); },
                        () => { movedNode.transform.position = newPosition; RegenerateMesh(movedNode); });
                }
            }
        }
    }

    void HandleKeyboardActions()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (ctrl && Input.GetKeyDown(KeyCode.Z))
        {
            Undo();
            return;
        }
        if (ctrl && Input.GetKeyDown(KeyCode.Y))
        {
            Redo();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            CycleSelection();
        }

        if (selected == null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            Resize(selected, resizeStep);
        }

        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            Resize(selected, -resizeStep);
        }

        if (Input.GetKeyDown(KeyCode.N))
        {
            AddChildNode(selected);
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            AddEyeAtSelected(selected);
        }

        if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
        {
            DeleteSelected();
        }

        if (Input.GetKeyDown(KeyCode.G))
        {
            RegenerateMesh(selected);
        }
    }

    void Resize(Node node, float delta)
    {
        float oldSize = node.size;
        node.size = Mathf.Clamp(node.size + delta, minSize, maxSize);
        float newSize = node.size;
        RegenerateMesh(node);
        PopFeedback(marker.transform);

        if (!Mathf.Approximately(oldSize, newSize))
        {
            RecordAction(
                () => { node.size = oldSize; RegenerateMesh(node); },
                () => { node.size = newSize; RegenerateMesh(node); });
        }
    }

    void Select(Node node)
    {
        selected = node;

        if (marker == null)
        {
            // ZTest Always so the marker stays visible through the creature's
            // own mesh instead of being hidden inside it (a selected node's
            // position is very often inside the body, not on its surface).
            markerMaterial = new Material(Shader.Find("Custom/XRayMarker"));
            marker = CreateXRayMarkerSphere("NodeSelectionMarker", markerMaterial);
        }

        markerMaterial.color = selectedColor;
        marker.transform.position = ResolveMarkerPosition(node);
        marker.transform.localScale = Vector3.one * Mathf.Max(node.size * 0.3f, 0.15f);
        marker.SetActive(true);
        PopFeedback(marker.transform);
    }

    void Deselect()
    {
        selected = null;
        dragging = false;
        if (marker != null)
        {
            marker.SetActive(false);
        }
    }

    void CycleSelection()
    {
        Node[] all = Object.FindObjectsByType<Node>(FindObjectsSortMode.None);
        if (all.Length == 0)
        {
            return;
        }

        int index = selected == null ? -1 : System.Array.IndexOf(all, selected);
        Select(all[(index + 1) % all.Length]);
    }

    // Not tracked by undo/redo: CreatureEyeAttacher rolls a random count/scale
    // each call, so a "redo" couldn't reproduce the exact same result anyway.
    void AddEyeAtSelected(Node node)
    {
        if (node == null)
        {
            return;
        }

        BMesh bmesh = node.GetComponentInParent<BMesh>();
        if (bmesh == null)
        {
            return;
        }

        CreatureGenerator generator = bmesh.GetComponent<CreatureGenerator>();
        GameObject eyePrefab = CreatureEyeAttacher.ResolveEyePrefab(generator != null ? generator.eyePrefab : null);
        if (eyePrefab == null)
        {
            Debug.LogWarning("NodeEditController: no Eye prefab available (Resources/Eye.prefab missing?).");
            return;
        }

        Vector3 lookDir = node.transform.parent != null
            ? node.transform.position - node.transform.parent.position
            : node.transform.forward;
        float extent = CreatureEyeAttacher.EstimateExtent(bmesh.transform, bmesh.gameObject);

        CreatureEyeAttacher.AttachEyes(node.gameObject, lookDir, eyePrefab, extent, eyeCount: 1);
        PopFeedback(marker.transform);

        // If a skeleton already exists, rebuilding binds the new eye's
        // FollowTransform to its matching bone -- otherwise it would just sit
        // statically instead of animating with the rest of the creature.
        RebuildMeshAndSkeleton(bmesh);
    }

    void AddChildNode(Node parent)
    {
        BMesh bmesh = parent.GetComponentInParent<BMesh>();
        Transform parentTransform = parent.transform;
        Vector3 localPosition = Vector3.up * newNodeOffset;
        float newSize = parent.size * 0.7f;

        GameObject created = CreateNodeAt(parentTransform, localPosition, newSize);
        RegenerateMesh(created.GetComponent<Node>());
        Select(created.GetComponent<Node>());

        RecordAction(
            () =>
            {
                if (created != null)
                {
                    if (selected == created.GetComponent<Node>())
                    {
                        Deselect();
                    }
                    Object.DestroyImmediate(created);
                    RebuildMeshAndSkeleton(bmesh);
                }
            },
            () =>
            {
                created = CreateNodeAt(parentTransform, localPosition, newSize);
                RebuildMeshAndSkeleton(bmesh);
                Select(created.GetComponent<Node>());
            });
    }

    GameObject CreateNodeAt(Transform parent, Vector3 localPosition, float size)
    {
        GameObject go = NodeFactory.Create();
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        Node node = go.GetComponent<Node>();
        node.size = size;
        EnsureCollider(node);
        return go;
    }

    void DeleteSelected()
    {
        if (selected == null)
        {
            return;
        }

        Transform parentTransform = selected.transform.parent;
        bool isRoot = parentTransform == null || parentTransform.GetComponent<Node>() == null;
        if (isRoot)
        {
            Debug.Log("NodeEditController: can't delete the root node -- use Clear on the Creature Generator panel instead.");
            return;
        }

        BMesh bmesh = selected.GetComponentInParent<BMesh>();
        GameObject toDelete = selected.gameObject;
        // Captured before destroying so undo can rebuild the same shape --
        // note this only restores Node structure (position/size/hierarchy), not
        // decorations like eyes, which CreatureIO doesn't track.
        CreatureNodeData snapshot = CreatureIO.CaptureHierarchy(selected);
        Deselect();

        Tween.Scale(toDelete.transform, Vector3.zero, popDuration, Ease.InBack).OnComplete(() =>
        {
            // Immediate, not Destroy(): RebuildMeshAndSkeleton runs right after and
            // scans for Node components -- a deferred destroy would still be found
            // by that same scan, so the deleted node would linger for one more mesh
            // generation before actually disappearing.
            Object.DestroyImmediate(toDelete);
            RebuildMeshAndSkeleton(bmesh);
        });

        GameObject restored = null;
        RecordAction(
            () =>
            {
                restored = CreatureIO.BuildHierarchy(snapshot, parentTransform, null);
                foreach (Node n in restored.GetComponentsInChildren<Node>())
                {
                    EnsureCollider(n);
                }
                RebuildMeshAndSkeleton(bmesh);
            },
            () =>
            {
                if (restored != null)
                {
                    if (selected == restored.GetComponent<Node>())
                    {
                        Deselect();
                    }
                    Object.DestroyImmediate(restored);
                    RebuildMeshAndSkeleton(bmesh);
                }
            });
    }

    // Public so other scripts (e.g. CreatureGeneratorUI's undoable Clear) can
    // push onto the same undo/redo stack as node edits, keeping Ctrl+Z/Ctrl+Y
    // a single unified history instead of separate, conflicting stacks.
    public void RecordAction(System.Action undo, System.Action redo)
    {
        if (historyIndex + 1 < history.Count)
        {
            history.RemoveRange(historyIndex + 1, history.Count - historyIndex - 1);
        }
        history.Add(new UndoRecord { undo = undo, redo = redo });
        historyIndex = history.Count - 1;
    }

    void Undo()
    {
        if (historyIndex < 0)
        {
            return;
        }
        history[historyIndex].undo();
        historyIndex--;
    }

    void Redo()
    {
        if (historyIndex + 1 >= history.Count)
        {
            return;
        }
        historyIndex++;
        history[historyIndex].redo();
    }

    void RegenerateMesh(Node node)
    {
        RebuildMeshAndSkeleton(node.GetComponentInParent<BMesh>());
    }

    // After editing nodes, the mesh needs a full rebuild -- and if this
    // creature already has a skeleton, that needs rebuilding too: once
    // AddSkeleton has run, the visible mesh is bone-driven (see
    // BMeshBoneExtensions.CreateSkeleton), so a node edit alone would have no
    // visible effect on the rendered creature at all otherwise.
    void RebuildMeshAndSkeleton(BMesh bmesh)
    {
        if (bmesh == null)
        {
            return;
        }

        bmesh.Generate();
        bmesh.GetComponent<CreatureGenerator>()?.RebuildSkeletonIfPresent();
    }

    // Falls back to any active camera if Camera.main is unset (e.g. the scene
    // camera isn't tagged MainCamera) -- otherwise selection/drag would
    // silently do nothing with no clue why.
    Camera GetActiveCamera()
    {
        return Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
    }

    void PopFeedback(Transform t)
    {
        Tween.PunchScale(t, Vector3.one * popStrength, popDuration);
    }

    // Node GameObjects have no collider by default (BMesh only needs the Node
    // component + transform), so raycasting for selection needs one added.
    // Takes the same per-frame Node scan Update() already did for
    // UpdateNodeMarkers rather than re-querying the scene itself -- cheap
    // either way for the node counts this project generates, but no reason to
    // pay for the query twice. Avoids coupling this controller to whichever
    // script just created new nodes (CreatureGenerator, this controller's own
    // AddChildNode, ...).
    void EnsureCollidersOnAllNodes(Node[] allNodes)
    {
        foreach (Node node in allNodes)
        {
            EnsureCollider(node);
        }
    }

    void EnsureCollider(Node node)
    {
        if (node.GetComponent<Collider>() == null)
        {
            SphereCollider collider = node.gameObject.AddComponent<SphereCollider>();
            collider.radius = Mathf.Max(node.size * 1.5f, 0.2f); // generous click target, small limbs are easy to miss otherwise
            collider.isTrigger = true;
        }
    }
}
