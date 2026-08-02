using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations.Rigging;

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

    [Header("Bone rig (created on demand, see AddSkeleton)")]
    [Tooltip("Root of the bone hierarchy built by AddSkeleton(). Destroyed and rebuilt whenever the body regenerates.")]
    public GameObject skeleton;
    [Tooltip("GameObject holding the SkinnedMeshRenderer built by AddSkeleton().")]
    public GameObject skinnedMeshObject;
    [Tooltip("Animation Rigging constraint rig built by AddIdleAnimation(), driving the secondary-motion wobble.")]
    public GameObject rig;

    public void Clear()
    {
        DestroyIfPresent(body);
        ClearSkeleton();
    }

    // A skeleton built from a previous body is meaningless once that body is
    // gone, so wipe it (and any animation rig sitting on top of it) too
    // whenever the body is cleared/regenerated.
    void ClearSkeleton()
    {
        DestroyIfPresent(skeleton);
        DestroyIfPresent(skinnedMeshObject);
        DestroyIfPresent(rig);
        skeleton = null;
        skinnedMeshObject = null;
        rig = null;

        DestroyComponentIfPresent<CreatureIdleSway>();
        DestroyComponentIfPresent<RigBuilder>();
        DestroyComponentIfPresent<Animator>();

        // AddSkeleton() hides the static mesh via showMode = Gizmo so the
        // skinned mesh doesn't double-render alongside it -- once the skeleton
        // is gone (e.g. switching Animation to None via ApplyAnimationMode(),
        // without a full Generate() to reset it), that would otherwise leave
        // the creature invisible with nothing left to show it. Only undoes
        // that specific auto-hide, not an explicit Vertices/Wireframe choice
        // made independently via the Show Mode dropdown.
        BMesh bmesh = GetComponent<BMesh>();
        if (bmesh.showMode == BMesh.ShowMode.Gizmo)
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

        List<GameObject> roots = CreatureIO.ImportFromFile(path, transform, nodePrefab, out string importedName);
        if (roots.Count == 0)
        {
            Debug.LogWarning($"CreatureGenerator: no creature found in {path}.");
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
    // between Idle and Walking just reconfigures the existing sway/rig.
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

        if (skeleton != null)
        {
            bool hadAnimation = rig != null;
            AddSkeleton();
            if (hadAnimation)
            {
                AddIdleAnimation();
            }
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

    // Adds a small generic idle animation on top of an existing skeleton
    // (BMeshBoneExtensions.AddSecondaryMotionRig): the root bone gently sways,
    // and every other bone -- whatever limbs/tentacles this random topology
    // happens to have -- lags behind it via a DampedTransform chain, so the
    // whole creature wobbles organically without any per-limb setup. Requires
    // AddSkeleton() to have been called first.
    public void AddIdleAnimation()
    {
        if (skeleton == null || animationMode == AnimationMode.None)
        {
            return;
        }

        // Destroying and re-adding the Animator/RigBuilder here would be unsafe in
        // play mode (Destroy() is deferred to end of frame, so a same-frame re-add
        // could bind to the about-to-be-destroyed component) -- AddSecondaryMotionRig
        // reuses an existing RigBuilder and clears its stale layers instead.
        DestroyComponentIfPresent<CreatureIdleSway>();
        DestroyIfPresent(rig);

        rig = GetComponent<BMesh>().AddSecondaryMotionRig(skeleton.transform);

        CreatureIdleSway sway = gameObject.AddComponent<CreatureIdleSway>();
        sway.rootBone = skeleton.transform.childCount > 0 ? skeleton.transform.GetChild(0) : null;
        sway.ConfigureForMode(animationMode);
    }
}
