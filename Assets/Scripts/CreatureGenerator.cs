using System.Collections.Generic;
using UnityEngine;

// Orchestrates a single creature's lifecycle: rolling a random body via
// CreatureBodyGenerator, applying skin/eyes, and optionally rigging a bone
// skeleton with a simple idle-sway animation on top. The actual body-shape
// grammar lives in CreatureBodyGenerator, and eye anchor selection/attachment
// in CreatureEyeAttacher -- this class only wires them together and owns the
// GameObjects they produce.
[RequireComponent(typeof(BMesh))]
public class CreatureGenerator : MonoBehaviour
{
    [Tooltip("How many levels of branching the random body can have (body -> limbs -> sub-limbs -> ...). Not a creature \"type\" -- just how elaborate the random result gets.")]
    [Range(1, 4)]
    public int complexity = 3;

    [Tooltip("If on, Generate() rolls a new random seed each time. Turn off to reuse the Seed field below and reproduce the exact same creature.")]
    public bool randomizeSeedOnGenerate = true;

    [Tooltip("The random seed used for the last (or next, if Random Seed is off) generation. Same seed + same settings = the same creature.")]
    public int seed = -987354147;

    [Tooltip("Attach a random number of eyes (1, 2, 4, or 6) to the generated body.")]
    public bool addEyes = true;

    public enum AnimationMode { None, Idle, Walking }

    [Tooltip("Skin (material) always applies automatically. None: static mesh only. Idle/Walking also build a bone skeleton and animate it (see CreatureIdleSway for what each mode configures).")]
    public AnimationMode animationMode = AnimationMode.Idle;

    [Tooltip("Cosmetic display name, randomized on every Generate() and freely editable afterwards -- not used by the generation logic itself.")]
    public string creatureName = "";

    [Header("References (optional -- auto-resolved if left empty)")]
    [Tooltip("Prefab used for each body segment. Leave empty to auto-resolve Resources/Node.prefab, or fall back to a bare GameObject with a Node component.")]
    public GameObject nodePrefab;
    [Tooltip("Eye prefab override. Leave empty to auto-resolve Resources/Eye.prefab.")]
    public GameObject eyePrefab;

    [Tooltip("Root GameObject of the generated body's Node hierarchy. Set by Generate() -- treat as read-only.")]
    public GameObject body;

    [Header("Bone skeleton (created on demand, see AddSkeleton)")]
    [Tooltip("Root of the bone hierarchy built by AddSkeleton(). Destroyed and rebuilt whenever the body regenerates.")]
    public GameObject skeleton;
    [Tooltip("GameObject holding the SkinnedMeshRenderer built by AddSkeleton().")]
    public GameObject skinnedMeshObject;

    public void Clear()
    {
        DestroyIfPresent(body);
        ClearSkeleton();
    }

    // Show/hide the eyes already on the current creature (the "Add Eyes" toggle
    // in the panel). Does not add eyes that were never generated.
    public void SetEyesVisible(bool visible)
    {
        if (body == null)
        {
            return;
        }
        foreach (CreatureEye eye in GetComponentsInChildren<CreatureEye>(true))
        {
            eye.gameObject.SetActive(visible);
        }
    }

    // A skeleton built from a previous body is meaningless once that body is
    // gone, so wipe it (and the idle animation on top of it) too whenever the
    // body is cleared/regenerated.
    void ClearSkeleton()
    {
        BMesh bmesh = GetComponent<BMesh>();

        // The instanced skinned + wireframe meshes aren't owned by anything else,
        // so destroy them here or they leak on every rebuild.
        DestroyMeshOf(bmesh.skinnedRenderer);
        DestroyMeshOf(bmesh.wireframeRenderer);

        DestroyIfPresent(skeleton);
        DestroyIfPresent(skinnedMeshObject);
        skeleton = null;
        skinnedMeshObject = null;

        DestroyComponentIfPresent<CreatureIdleSway>();
        DestroyComponentIfPresent<Animation>(); // added by CreatureIdleSway's RequireComponent

        // Hand rendering back to the static MeshRenderer now that the skinned
        // copy is gone (BMesh keeps the static one hidden while skinnedRenderer
        // is set -- see BMesh.Update).
        bmesh.skinnedRenderer = null;
        bmesh.wireframeRenderer = null;

        // The skinned mesh was showing regardless of Show Mode; a Gizmo/Vertices
        // mode now renders nothing, so fall back to Mesh rather than leave the
        // creature invisible.
        if (bmesh.showMode == BMesh.ShowMode.Gizmo || bmesh.showMode == BMesh.ShowMode.Vertices)
        {
            bmesh.showMode = BMesh.ShowMode.Mesh;
        }
    }

    // Always immediate, even in play mode: Clear()/ClearSkeleton() are always
    // followed, in the same method call, by rebuilding fresh objects (Generate(),
    // AddSkeleton()). Destroy() defers to end of frame, which left the old body
    // still present -- and still findable by GetComponentsInChildren<Node>() --
    // for the rest of that frame, so the freshly-built body ended up sharing
    // the hierarchy with a stale, about-to-die copy of the previous one. That's
    // what caused old geometry/eyes/nodes to seemingly never get cleared, and
    // made node selection unreliable (raycasts could hit the stale copy).
    void DestroyIfPresent(GameObject go)
    {
        if (go == null)
        {
            return;
        }

        DestroyImmediate(go);
    }

    static void DestroyMeshOf(SkinnedMeshRenderer renderer)
    {
        if (renderer != null && renderer.sharedMesh != null)
        {
            DestroyImmediate(renderer.sharedMesh);
        }
    }

    void DestroyComponentIfPresent<T>() where T : Component
    {
        T component = GetComponent<T>();
        if (component == null)
        {
            return;
        }

        DestroyImmediate(component);
    }

    public void Generate()
    {
        if (randomizeSeedOnGenerate)
        {
            seed = (int)System.DateTime.Now.Ticks;
        }
        Random.InitState(seed);
        Clear();

        creatureName = CreatureNameGenerator.GenerateRandomName();
        body = CreatureBodyGenerator.Generate(gameObject, complexity, nodePrefab);
        FinishBody();
    }

    // Loads a previously exported creature's Node hierarchy (see CreatureIO,
    // "Export Creature (JSON)" in the runtime panel) and finishes it exactly
    // like a freshly generated one (mesh, skin, eyes, skeleton). Eyes/skeleton
    // aren't part of the exported data, so they're rolled fresh rather than
    // restored exactly as they were when exported.
    public void ImportFromFile(string path)
    {
        Clear();
        ApplyImportedRoots(CreatureIO.ImportFromFile(path, transform, nodePrefab, out string importedName), importedName, path);
    }

    // Same as ImportFromFile but from JSON text already in memory -- used by the
    // WebGL file-upload path (see CreatureGeneratorUI.OnCreatureFileUploaded),
    // where there is no file path to read.
    public void ImportFromJson(string json)
    {
        Clear();
        ApplyImportedRoots(CreatureIO.ImportFromString(json, transform, nodePrefab, out string importedName), importedName, "uploaded JSON");
    }

    void ApplyImportedRoots(List<GameObject> roots, string importedName, string source)
    {
        if (roots.Count == 0)
        {
            Debug.LogWarning($"CreatureGenerator: no creature found in {source}.");
            return;
        }

        creatureName = importedName;
        body = roots[0];
        FinishBody();
    }

    // Rebuilds mesh/skin/eyes/skeleton for a body that was restored externally
    // (see CreatureGeneratorUI's undoable Clear) -- like ImportFromFile, but
    // the Node hierarchy is already built by the caller instead of loaded
    // from a file.
    public void RestoreBody(GameObject restoredBody, string restoredName)
    {
        Clear();
        body = restoredBody;
        creatureName = restoredName;
        FinishBody();
    }

    // Shared tail end of Generate()/ImportFromFile(): once `body` is set, both
    // paths need the same mesh/skin/eyes/skeleton finishing steps.
    void FinishBody()
    {
        BMesh bmesh = GetComponent<BMesh>();
        bmesh.showMode = BMesh.ShowMode.Mesh;
        CreatureMaterialGenerator.ApplyToBMesh(bmesh);
        bmesh.Generate();

        if (addEyes)
        {
            Node eyeAnchor = CreatureEyeAttacher.ChooseEyeAnchor(body);
            Vector3 lookDir = eyeAnchor.transform.position - eyeAnchor.transform.parent.position;
            float creatureExtent = CreatureEyeAttacher.EstimateExtent(body.transform, body);
            CreatureEyeAttacher.AttachEyes(eyeAnchor.gameObject, lookDir, CreatureEyeAttacher.ResolveEyePrefab(eyePrefab), creatureExtent);
        }

        if (animationMode != AnimationMode.None)
        {
            AddSkeleton();
            AddIdleAnimation();
        }
    }

    // Applies a change to animationMode to the CURRENT body immediately,
    // without rerolling it -- used by the runtime panel's Animation dropdown,
    // which previously only took effect after a full Generate() (which also
    // rerolls the whole body, not just the animation). Only rebuilds the
    // skeleton/skin from scratch if one doesn't already exist; switching
    // between Idle and Walking just reconfigures the existing sway.
    public void ApplyAnimationMode()
    {
        if (body == null)
        {
            return;
        }

        if (animationMode == AnimationMode.None)
        {
            ClearSkeleton();
            return;
        }

        if (skeleton == null)
        {
            AddSkeleton();
        }
        AddIdleAnimation();
    }

    // Rerolls one random existing limb/branch instead of the whole body (see
    // CreatureBodyGenerator.MutateRandomPart) -- keeps the rest of the shape
    // intact while still giving some fresh variety. Not undoable (unlike
    // Clear): unlike a full regenerate, this only ever touches a small,
    // visually-obvious part of the creature, so a bad roll is cheap to retry.
    public void MutatePart()
    {
        if (body == null)
        {
            return;
        }

        CreatureBodyGenerator.MutateRandomPart(body, complexity, nodePrefab);

        BMesh bmesh = GetComponent<BMesh>();
        bmesh.Generate();

        RebuildSkeletonIfPresent();
    }

    // Rebuilds the skeleton (and re-applies the idle animation) if one already
    // existed -- used here by MutatePart(), and by NodeEditController after it
    // edits a Node hierarchy out from under a creature (a node move/add/delete
    // needs the skeleton re-bound to the new node layout, exactly like a
    // mutation does). AddIdleAnimation no-ops when animationMode is None.
    public void RebuildSkeletonIfPresent()
    {
        if (skeleton == null)
        {
            return;
        }

        // Capture before AddSkeleton -- it destroys the sway via ClearSkeleton.
        bool hadAnimation = GetComponent<CreatureIdleSway>() != null;
        AddSkeleton();
        if (hadAnimation)
        {
            AddIdleAnimation();
        }
    }

    // Re-rolls the skin material/texture without rebuilding the body --
    // lets you keep a shape you like and just try a different look.
    public void RegenerateSkin()
    {
        BMesh bmesh = GetComponent<BMesh>();
        CreatureMaterialGenerator.ApplyToBMesh(bmesh);

        // If a skeleton was already added, its SkinnedMeshRenderer got its
        // material as a one-time copy in AddSkeleton -- keep it in sync too.
        if (skinnedMeshObject != null)
        {
            SkinnedMeshRenderer renderer = skinnedMeshObject.GetComponent<SkinnedMeshRenderer>();
            if (renderer != null)
            {
                renderer.material = bmesh.normalMaterial;
            }
        }
    }

    // Rigs the current body with an actual posable bone skeleton (BMeshBoneExtensions.CreateSkeleton):
    // one bone per Node, automatic proximity-based skin weights, bound to a
    // SkinnedMeshRenderer. Hides the static MeshRenderer so the two don't both
    // render. A one-shot finalizing step, not part of Generate() itself, since
    // it creates extra GameObjects that a quick body-shape iteration shouldn't
    // have to keep rebuilding.
    public void AddSkeleton()
    {
        if (body == null)
        {
            return;
        }

        ClearSkeleton();

        SkinnedMeshRenderer renderer = GetComponent<BMesh>().CreateSkeleton();
        skinnedMeshObject = renderer.gameObject;
        skeleton = renderer.rootBone != null ? renderer.rootBone.parent.gameObject : null;
    }

    // Builds and plays the looping idle/walking AnimationClip on top of an
    // existing skeleton (see CreatureIdleSway / CreatureMotion). The clip is
    // generated from the bone hierarchy -- every bone sways around its rest pose
    // with a phase offset by depth. Requires AddSkeleton() first.
    public void AddIdleAnimation()
    {
        if (skeleton == null || animationMode == AnimationMode.None)
        {
            return;
        }

        SkinnedMeshRenderer smr = skinnedMeshObject != null ? skinnedMeshObject.GetComponent<SkinnedMeshRenderer>() : null;
        if (smr == null || smr.rootBone == null)
        {
            return;
        }

        // Switching Idle<->Walking on a live creature: the clip is mid-play, so
        // keep the existing component (it holds the true rest pose captured when
        // the skeleton was fresh) and only re-tune it. A brand new skeleton has
        // no player yet -- Initialize captures its rest pose now, while it's at
        // rest.
        CreatureIdleSway sway = GetComponent<CreatureIdleSway>();
        if (sway == null)
        {
            sway = gameObject.AddComponent<CreatureIdleSway>();
            sway.Initialize(smr, animationMode);
        }
        else
        {
            sway.ConfigureForMode(animationMode);
        }
    }
}
