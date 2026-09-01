# BMeshGeneration

A procedural 3D **creature generator** built on a node-based mesh system (in the
spirit of Blender's *Skin* modifier). Nodes describe body parts and are
hierarchised with Unity's transform hierarchy; a mesh is skinned over them, a
bone rig is generated, and a looping animation is built procedurally — the whole
pipeline runs at runtime, in the Editor's Play mode or in a WebGL build.

![](/Preview/sample.png "Sample creature.")

## How it works

From a set of `Node`s to an animated, exportable creature:

1. **Body grammar** — `CreatureBodyGenerator` rolls a random `Node` tree from a
   small recursive grammar: chains of segments, plus three arrangements
   (*continue*, *mirrored pair*, *radial ring*). There is no catalogue of
   creature "types" — every seed produces a different topology (biped, spider,
   tentacled blob, …).
2. **Mesh** — `BMesh` / `BMeshGenerator` build a quad mesh over the `Node` tree
   (a capped tube along each chain, convex-hull junctions where limbs meet),
   then optional subdivision + smoothing.
3. **Skin** — `CreatureMaterialGenerator` assigns the `Custom/TriplanarCreature`
   material with a random pattern picked from `Resources/Textures/Animal`.
   Triplanar projection means no UV unwrap is needed for the organic topology.
4. **Eyes** — `CreatureEyeAttacher` chooses a tip node and attaches 1–6 eyes.
5. **Rig** — `BoneGenerator` creates one bone per `Node`;
   `BMeshBoneExtensions.CreateSkeleton` binds a `SkinnedMeshRenderer` with
   automatic *envelope* skin weights (each vertex bound to its nearest bone
   segment and that bone's parent).
6. **Animation** — `CreatureMotion` bakes a looping clip. The skeleton is split
   into a *spine* (the straightest chain from the root) and *limbs*: **Idle**
   sways gently with a wave travelling down each limb; **Walking** is a real
   fore-aft step cycle (limbs alternating in phase, deeper bones trailing for a
   knee/elbow flex, body bob + roll). Played through a legacy `Animation`
   component; the same keyframes are also written into the glTF export.
7. **Edit** — `NodeEditController` lets you select / drag / add / delete /
   resize nodes at runtime with a unified undo-redo stack; the mesh and rig
   rebuild live.

## Runtime panel

`CreatureGeneratorUI` — a UI Toolkit panel built entirely in code (no UXML/USS):

- Complexity, Add Eyes, Animation (None / Idle / Walking), Name, Random Seed / Seed
- *Shape / Size / Branching* generation-bias sliders
- **Generate** (`R`), **Export** (glb / json), **Import**, **Clear**
- Top-right icon bar: **Mesh / Wireframe / Structure** view + an **Edit** toggle
  (icons are single-path SVGs from `Assets/UI/Textures`, rendered directly with
  `Painter2D` — see `SvgIcon`)
- Keyboard shortcuts panel (bottom-right)

## Export

| Format | Contents | Notes |
|---|---|---|
| **glTF (`.glb`)** | skinned mesh + skeleton + bind poses + looping animation + the triplanar look baked into vertex colours | Hand-rolled writer (`GltfExporter`), no package dependency. Validates clean against the Khronos glTF validator; opens in Blender / three.js. |
| **Creature (`.json`)** | flat `Node` hierarchy (name / local position / size / parent index) | Re-importable as an editable creature. |
| **OBJ / COLLADA / binary** | geometry (+ bones for dae) | Editor-only, via *Tools ▸ BMesh Exporter*. |

On a **WebGL build** every export triggers a browser download and *Import* opens
the file picker, through `Assets/Plugins/WebGL/FileBridge.jslib`.

## v2026 update

- **Unity 6 (6000.0)** native
- **Runtime creature generator** — the full procedural pipeline (body grammar →
  mesh → skin → eyes → rig) driven by an in-game UI Toolkit panel; nothing
  editor-specific required
- **Procedural animation** — a real looping `AnimationClip` for Idle and a
  proper Walking gait, generated from each random skeleton
- **glTF / `.glb` export** — mesh + skeleton + skin weights + animation + baked
  vertex colours, WebGL-safe
- **WebGL build target** — browser download / upload for all imports & exports;
  wheel handling so the page doesn't scroll under the panel
- **Runtime node editing** with undo/redo and live mesh + rig rebuild
- **Wireframe** show mode — a barycentric wireframe (`Custom/WireframeBary`,
  fragment-based), so it runs on WebGL where the old geometry-shader wireframe
  couldn't
- **Structure** view — hides the mesh and shows the animated node markers
- Envelope skin weighting, mesh smoothing, self-contained SVG-icon renderer
- Flat re-importable creature JSON; instanced-mesh leak on rebuild fixed
- Removed the old multi-creature *batch* mode — single-creature focus

## References

Greatly inspired by:

- [Blender Skin Modifier](https://docs.blender.org/manual/en/latest/modeling/modifiers/generate/skin.html)
- Ji, Zhongping; Liu, Ligang; Wang, Yigang (2010). [B-Mesh](https://citeseerx.ist.psu.edu/viewdoc/download?doi=10.1.1.357.7134&rep=rep1&type=pdf): A Fast Modeling System for Base Meshes of 3D Articulated Shapes

*Parts of the code were written with AI assistance and can still be optimized.*
