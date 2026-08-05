// ============================================================================
// Runtime Creature Generator Panel (UI Toolkit)
// ============================================================================
// A small in-game panel to generate/customize/export a creature, a second
// collapsible panel listing NodeEditController's shortcuts, a top-center
// creature name display, a bottom-center Single/Multiple mode selector, and a
// top-right temporary error/warning log. Meant to work both while testing in
// the Editor's Play mode and, eventually, in an actual build (no UnityEditor
// dependency anywhere in this file, unlike CreatureMenu.cs which is
// editor-only). Builds its VisualElement tree entirely in code so no
// .uxml/.uss authoring step is required to use it.
//
// This is plain runtime UI Toolkit, not the editor-only SerializedObject
// binding system, so every control is wired to CreatureGenerator's fields by
// hand (read the initial value, write back on change) rather than bound
// automatically.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class CreatureGeneratorUI : MonoBehaviour
{
    [Tooltip("Optional -- if left empty, a new creature is created when the panel is enabled.")]
    public CreatureGenerator target;

    [Tooltip("Optional -- assign a PanelSettings asset (Assets > Create > UI Toolkit > Panel Settings Asset) for proper default theming. If left empty, a bare one is created at runtime.")]
    public PanelSettings panelSettingsOverride;

    public float errorToastLifetime = 3.5f;

    private enum GenerationMode { Single, Multiple }

    private static readonly (string key, string description)[] Shortcuts =
    {
        ("Click", "select a node"),
        ("Click + drag", "move the selected node"),
        ("Tab", "select next node"),
        ("+ / -", "resize selected node"),
        ("N", "add a child node"),
        ("E", "add an eye at the selected node"),
        ("Delete / Backspace", "delete selected node"),
        ("G", "force a mesh refresh"),
        ("Ctrl+Z / Ctrl+Y", "undo / redo"),
        ("R", "generate (Single: one creature, Multiple: a new batch)"),
        ("Right-click + drag", "orbit camera"),
        ("Scroll wheel", "zoom"),
    };

    private GenerationMode mode = GenerationMode.Single;
    private CreatureBatchGenerator batchGenerator;

    private Label statusLabel;
    private Label creatureNameDisplay;
    private IntegerField seedField;
    private TextField nameField;
    private IntegerField batchCountField;
    private VisualElement errorToastContainer;
    private VisualElement singleModeSection;
    private VisualElement multipleModeSection;
    private Button singleModeButton;
    private Button multipleModeButton;

    void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();
        if (document.panelSettings == null)
        {
            document.panelSettings = panelSettingsOverride != null ? panelSettingsOverride : CreateDefaultPanelSettings();
        }

        // Ensures the camera can orbit and nodes can be clicked regardless of
        // how this panel ended up in the scene -- previously this only happened
        // in CreatureMenu's editor menu items, so a manually-added panel (or one
        // surviving from before that fix) left clicking nodes silently doing
        // nothing, with no obvious cause.
        EnsureCameraTools();

        if (target == null)
        {
            target = CreateCreature();
        }

        VisualElement root = document.rootVisualElement;
        root.Clear();
        root.Add(BuildGeneratorPanel());
        root.Add(BuildKeybindingsPanel());
        root.Add(BuildNameDisplay());
        root.Add(BuildModeSelector());
        root.Add(BuildErrorToastContainer());

        Application.logMessageReceived += HandleLogMessage;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= HandleLogMessage;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R) && !IsTextInputFocused())
        {
            GenerateCreature();
        }
    }

    private bool IsTextInputFocused()
    {
        Focusable focused = GetComponent<UIDocument>().rootVisualElement.panel?.focusController?.focusedElement;
        return focused is TextField || focused is IntegerField;
    }

    private static void EnsureCameraTools()
    {
        if (Camera.main == null)
        {
            return;
        }

        CameraToolsSetup.EnsureCameraTools(Camera.main.gameObject, useUndo: false);
    }

    private VisualElement BuildGeneratorPanel()
    {
        Foldout foldout = new Foldout { text = "Creature Generator", value = true };
        StylePanel(foldout, top: 16, left: 16);

        // ------ Customization shared by Single and Multiple ------

        SliderInt complexitySlider = new SliderInt("Complexity", 1, 4) { value = target.complexity };
        complexitySlider.RegisterValueChangedCallback(evt => target.complexity = evt.newValue);
        Style(complexitySlider);
        foldout.Add(complexitySlider);

        Toggle eyesToggle = new Toggle("Add Eyes") { value = target.addEyes };
        eyesToggle.RegisterValueChangedCallback(evt => target.addEyes = evt.newValue);
        Style(eyesToggle);
        foldout.Add(eyesToggle);

        // Skin is always applied by Generate(); this picks whether/how the
        // skeleton is animated on top of it (see CreatureIdleSway).
        // ApplyAnimationMode() no-ops if there's no body yet (e.g. right after
        // switching to Multiple mode), so this is safe to call unconditionally.
        EnumField animationModeField = new EnumField("Animation", target.animationMode);
        animationModeField.RegisterValueChangedCallback(evt =>
        {
            target.animationMode = (CreatureGenerator.AnimationMode)evt.newValue;
            target.ApplyAnimationMode();
        });
        Style(animationModeField);
        foldout.Add(animationModeField);

        // ------ Single-mode-only controls ------

        singleModeSection = new VisualElement();
        foldout.Add(singleModeSection);
        BuildSingleModeSection(singleModeSection);

        // ------ Multiple-mode-only controls ------

        multipleModeSection = new VisualElement();
        multipleModeSection.style.display = DisplayStyle.None;
        foldout.Add(multipleModeSection);
        BuildMultipleModeSection(multipleModeSection);

        // ------ Generation bias tuning ------

        foldout.Add(BuildShapeSection());
        foldout.Add(BuildSizeSection());
        foldout.Add(BuildBranchingSection());

        // ------ Actions ------

        Button generateButton = new Button(GenerateCreature) { text = "Generate (R)" };
        StyleButton(generateButton);
        foldout.Add(generateButton);

        Button clearButton = new Button(ClearCreature) { text = "Clear" };
        StyleButton(clearButton, destructive: true);
        foldout.Add(clearButton);

        statusLabel = new Label(string.Empty);
        statusLabel.style.color = Color.white;
        statusLabel.style.marginTop = 6;
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        foldout.Add(statusLabel);

        return foldout;
    }

    private void BuildSingleModeSection(VisualElement section)
    {
        nameField = new TextField("Name") { value = target.creatureName };
        nameField.RegisterValueChangedCallback(evt =>
        {
            target.creatureName = evt.newValue;
            if (creatureNameDisplay != null)
            {
                creatureNameDisplay.text = evt.newValue;
            }
        });
        Style(nameField);
        section.Add(nameField);

        // Mirrors BMesh's own Inspector show mode. Not guarded against picking
        // "Mesh" while a skeleton exists -- AddSkeleton() forces Gizmo mode
        // specifically to avoid double-rendering the static mesh alongside the
        // SkinnedMeshRenderer, so switching back to Mesh here can bring the
        // static mesh back too. Kept simple/unguarded on purpose.
        BMesh bmesh = target.GetComponent<BMesh>();
        EnumField showModeField = new EnumField("Show Mode", bmesh.showMode);
        showModeField.RegisterValueChangedCallback(evt => bmesh.showMode = (BMesh.ShowMode)evt.newValue);
        Style(showModeField);
        section.Add(showModeField);

        Toggle randomSeedToggle = new Toggle("Random Seed") { value = target.randomizeSeedOnGenerate };
        Style(randomSeedToggle);
        section.Add(randomSeedToggle);

        seedField = new IntegerField("Seed") { value = target.seed };
        seedField.SetEnabled(!target.randomizeSeedOnGenerate);
        seedField.RegisterValueChangedCallback(evt => target.seed = evt.newValue);
        Style(seedField);
        seedField.style.minWidth = 170; // the label + a full Ticks-based seed otherwise gets clipped
        section.Add(seedField);

        randomSeedToggle.RegisterValueChangedCallback(evt =>
        {
            target.randomizeSeedOnGenerate = evt.newValue;
            seedField.SetEnabled(!evt.newValue);
        });

        // Lives on NodeEditController (the camera), not on this CreatureGenerator --
        // EnsureCameraTools() (called in OnEnable, before this) guarantees it's
        // there whenever Camera.main exists.
        NodeEditController nodeEditController = GetMainCameraComponent<NodeEditController>();
        if (nodeEditController != null)
        {
            Toggle liveEditToggle = new Toggle("Live Node Editing") { value = nodeEditController.liveEditing };
            liveEditToggle.RegisterValueChangedCallback(evt => nodeEditController.liveEditing = evt.newValue);
            Style(liveEditToggle);
            section.Add(liveEditToggle);

            Toggle showNodesToggle = new Toggle("Show Nodes") { value = nodeEditController.showNodes };
            showNodesToggle.RegisterValueChangedCallback(evt => nodeEditController.showNodes = evt.newValue);
            Style(showNodesToggle);
            section.Add(showNodesToggle);
        }

        VisualElement exportRow = new VisualElement();
        exportRow.style.flexDirection = FlexDirection.Row;
        exportRow.style.marginTop = 6;

        Button exportObjButton = new Button(ExportCreatureObj) { text = "Mesh (OBJ)" };
        StyleButton(exportObjButton);
        exportObjButton.style.flexGrow = 1;
        exportObjButton.style.marginTop = 0;
        exportObjButton.style.marginRight = 4;
        exportRow.Add(exportObjButton);

        Button exportJsonButton = new Button(ExportCreatureJson) { text = "Creature (JSON)" };
        StyleButton(exportJsonButton);
        exportJsonButton.style.flexGrow = 1;
        exportJsonButton.style.marginTop = 0;
        exportRow.Add(exportJsonButton);

        section.Add(exportRow);

        Button importButton = new Button(ImportCreature) { text = "Import Creature" };
        StyleButton(importButton);
        section.Add(importButton);

        Button mutateButton = new Button(MutateCreature) { text = "Mutate a Limb" };
        StyleButton(mutateButton);
        section.Add(mutateButton);
    }

    private void BuildMultipleModeSection(VisualElement section)
    {
        batchCountField = new IntegerField("Count") { value = batchGenerator != null ? batchGenerator.count : 6 };
        Style(batchCountField);
        section.Add(batchCountField);

        Label hint = new Label("Complexity/Add Eyes/Animation above apply to every creature in the batch.");
        hint.style.color = new Color(0.8f, 0.8f, 0.8f);
        hint.style.fontSize = 11;
        hint.style.whiteSpace = WhiteSpace.Normal;
        hint.style.marginTop = 4;
        section.Add(hint);
    }

    // ------ Generation bias tuning sections ------
    //
    // Directly expose CreatureBodyGenerator's public static tuning ranges --
    // there's only ever one "current" set of generation biases (not per
    // creature), so these read/write the static fields straight away with no
    // extra state to keep in sync.

    private VisualElement BuildShapeSection()
    {
        Foldout section = new Foldout { text = "Shape", value = false };
        Style(section);
        section.Add(BuildRangeRow("Segment Count", 1, 6, () => CreatureBodyGenerator.SegmentCountRange, v => CreatureBodyGenerator.SegmentCountRange = v));
        section.Add(BuildRangeRow("Segment Length", 0.1f, 3f, () => CreatureBodyGenerator.SegmentLengthRange, v => CreatureBodyGenerator.SegmentLengthRange = v));
        section.Add(BuildFloatRow("Root Wobble", 0f, 1f, () => CreatureBodyGenerator.RootDirectionSpread, v => CreatureBodyGenerator.RootDirectionSpread = v));
        section.Add(BuildFloatRow("Limb Wobble", 0f, 1f, () => CreatureBodyGenerator.LimbDirectionSpread, v => CreatureBodyGenerator.LimbDirectionSpread = v));
        return section;
    }

    private VisualElement BuildSizeSection()
    {
        Foldout section = new Foldout { text = "Size", value = false };
        Style(section);
        section.Add(BuildRangeRow("Root Start Size", 0.1f, 1.5f, () => CreatureBodyGenerator.RootStartSizeRange, v => CreatureBodyGenerator.RootStartSizeRange = v));
        section.Add(BuildRangeRow("Limb Start Size", 0.05f, 1f, () => CreatureBodyGenerator.LimbStartSizeRange, v => CreatureBodyGenerator.LimbStartSizeRange = v));
        section.Add(BuildRangeRow("Taper", 0.1f, 1f, () => CreatureBodyGenerator.TaperRange, v => CreatureBodyGenerator.TaperRange = v));
        section.Add(BuildRangeRow("Jitter", 0f, 0.4f, () => CreatureBodyGenerator.JitterRange, v => CreatureBodyGenerator.JitterRange = v));
        return section;
    }

    private VisualElement BuildBranchingSection()
    {
        Foldout section = new Foldout { text = "Branching", value = false };
        Style(section);
        section.Add(BuildRangeRow("Root Attachments", 0, 5, () => CreatureBodyGenerator.RootAttachmentCountRange, v => CreatureBodyGenerator.RootAttachmentCountRange = v));
        section.Add(BuildFloatRow("Continue Chance", 0f, 1f, () => CreatureBodyGenerator.ContinueChance, v => CreatureBodyGenerator.ContinueChance = v));
        section.Add(BuildFloatRow("Root Ring Chance", 0f, 1f, () => CreatureBodyGenerator.RootRingChance, v => CreatureBodyGenerator.RootRingChance = v));
        section.Add(BuildRangeRow("Child Scale", 0.1f, 1f, () => CreatureBodyGenerator.ChildScaleRange, v => CreatureBodyGenerator.ChildScaleRange = v));
        section.Add(BuildRangeRow("Continue Scale", 0.1f, 1f, () => CreatureBodyGenerator.ContinueScaleRange, v => CreatureBodyGenerator.ContinueScaleRange = v));
        section.Add(BuildRangeRow("Radial Ring Count", 3, 16, () => CreatureBodyGenerator.RadialRingCountRange, v => CreatureBodyGenerator.RadialRingCountRange = v));
        return section;
    }

    private VisualElement BuildRangeRow(string label, float lowLimit, float highLimit, System.Func<Vector2> getter, System.Action<Vector2> setter)
    {
        Vector2 current = getter();
        MinMaxSlider slider = new MinMaxSlider(label, current.x, current.y, lowLimit, highLimit);
        slider.RegisterValueChangedCallback(evt => setter(evt.newValue));
        Style(slider);
        return slider;
    }

    private VisualElement BuildFloatRow(string label, float lowLimit, float highLimit, System.Func<float> getter, System.Action<float> setter)
    {
        Slider slider = new Slider(label, lowLimit, highLimit) { value = getter() };
        slider.RegisterValueChangedCallback(evt => setter(evt.newValue));
        Style(slider);
        return slider;
    }

    private VisualElement BuildKeybindingsPanel()
    {
        Foldout foldout = new Foldout { text = "Shortcuts", value = false };
        StylePanel(foldout, bottom: 16, right: 16);

        foreach ((string key, string description) in Shortcuts)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 2;

            Label keyLabel = new Label(key);
            keyLabel.style.color = new Color(1f, 0.82f, 0.3f);
            keyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            keyLabel.style.fontSize = 11;
            keyLabel.style.minWidth = 120;
            keyLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(keyLabel);

            Label descriptionLabel = new Label(description);
            descriptionLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
            descriptionLabel.style.fontSize = 11;
            descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
            descriptionLabel.style.flexShrink = 1;
            row.Add(descriptionLabel);

            foldout.Add(row);
        }

        return foldout;
    }

    private VisualElement BuildNameDisplay()
    {
        VisualElement container = new VisualElement();
        container.style.position = Position.Absolute;
        container.style.top = 16;
        container.style.left = 0;
        container.style.right = 0;
        container.style.flexDirection = FlexDirection.Row;
        container.style.justifyContent = Justify.Center;
        container.pickingMode = PickingMode.Ignore;

        creatureNameDisplay = new Label(target != null ? target.creatureName : string.Empty);
        creatureNameDisplay.style.color = Color.white;
        creatureNameDisplay.style.fontSize = 22;
        creatureNameDisplay.style.unityFontStyleAndWeight = FontStyle.Bold;
        creatureNameDisplay.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
        creatureNameDisplay.style.paddingLeft = 18;
        creatureNameDisplay.style.paddingRight = 18;
        creatureNameDisplay.style.paddingTop = 4;
        creatureNameDisplay.style.paddingBottom = 4;
        SetBorderRadius(creatureNameDisplay, 14);

        container.Add(creatureNameDisplay);
        return container;
    }

    // Bottom-center Single/Multiple switch -- a distinct display mode rather
    // than just another button: Single shows/edits one creature the way this
    // panel always has, Multiple generates a whole grid of independent
    // creatures at once (see CreatureBatchGenerator) with a simplified panel
    // (no Name/Seed/Export -- none of those make sense for a batch).
    private VisualElement BuildModeSelector()
    {
        VisualElement container = new VisualElement();
        container.style.position = Position.Absolute;
        container.style.bottom = 16;
        container.style.left = 0;
        container.style.right = 0;
        container.style.flexDirection = FlexDirection.Row;
        container.style.justifyContent = Justify.Center;
        container.pickingMode = PickingMode.Ignore;

        VisualElement pill = new VisualElement();
        pill.style.flexDirection = FlexDirection.Row;
        pill.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
        pill.style.paddingLeft = 4;
        pill.style.paddingRight = 4;
        pill.style.paddingTop = 4;
        pill.style.paddingBottom = 4;
        SetBorderRadius(pill, 8);

        singleModeButton = new Button(() => SetMode(GenerationMode.Single)) { text = "Single" };
        multipleModeButton = new Button(() => SetMode(GenerationMode.Multiple)) { text = "Multiple" };
        StyleButton(singleModeButton);
        StyleButton(multipleModeButton);
        singleModeButton.style.marginTop = 0;
        multipleModeButton.style.marginTop = 0;
        singleModeButton.style.marginRight = 4;
        singleModeButton.style.minWidth = 90;
        multipleModeButton.style.minWidth = 90;

        pill.Add(singleModeButton);
        pill.Add(multipleModeButton);
        container.Add(pill);

        RefreshModeButtons();
        return container;
    }

    private void SetMode(GenerationMode newMode)
    {
        if (mode == newMode)
        {
            return;
        }

        mode = newMode;
        RefreshModeButtons();

        if (mode == GenerationMode.Multiple)
        {
            if (target != null)
            {
                target.Clear();
            }
        }
        else
        {
            batchGenerator?.ClearBatch();
        }

        if (singleModeSection != null)
        {
            singleModeSection.style.display = mode == GenerationMode.Single ? DisplayStyle.Flex : DisplayStyle.None;
        }
        if (multipleModeSection != null)
        {
            multipleModeSection.style.display = mode == GenerationMode.Multiple ? DisplayStyle.Flex : DisplayStyle.None;
        }

        statusLabel.text = mode == GenerationMode.Single ? "Switched to Single mode." : "Switched to Multiple mode.";
    }

    private void RefreshModeButtons()
    {
        Color active = new Color(0.25f, 0.45f, 0.85f);
        Color inactive = new Color(0.25f, 0.25f, 0.25f);
        if (singleModeButton != null)
        {
            singleModeButton.style.backgroundColor = mode == GenerationMode.Single ? active : inactive;
        }
        if (multipleModeButton != null)
        {
            multipleModeButton.style.backgroundColor = mode == GenerationMode.Multiple ? active : inactive;
        }
    }

    private VisualElement BuildErrorToastContainer()
    {
        errorToastContainer = new VisualElement();
        errorToastContainer.style.position = Position.Absolute;
        errorToastContainer.style.top = 16;
        errorToastContainer.style.right = 16;
        errorToastContainer.style.width = 320;
        errorToastContainer.style.flexDirection = FlexDirection.Column;
        errorToastContainer.pickingMode = PickingMode.Ignore;
        return errorToastContainer;
    }

    private void HandleLogMessage(string message, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Warning)
        {
            return;
        }

        Label toast = new Label(message);
        toast.style.color = Color.white;
        toast.style.backgroundColor = type == LogType.Warning ? new Color(0.55f, 0.45f, 0f, 0.9f) : new Color(0.6f, 0.1f, 0.1f, 0.9f);
        toast.style.whiteSpace = WhiteSpace.Normal;
        toast.style.fontSize = 11;
        toast.style.marginBottom = 4;
        toast.style.paddingLeft = 8;
        toast.style.paddingRight = 8;
        toast.style.paddingTop = 6;
        toast.style.paddingBottom = 6;
        SetBorderRadius(toast, 4);

        errorToastContainer.Add(toast);
        StartCoroutine(RemoveToastAfterDelay(toast));
    }

    private IEnumerator RemoveToastAfterDelay(VisualElement toast)
    {
        yield return new WaitForSeconds(errorToastLifetime);
        toast.RemoveFromHierarchy();
    }

    private static void SetBorderRadius(VisualElement element, float radius)
    {
        element.style.borderTopLeftRadius = radius;
        element.style.borderTopRightRadius = radius;
        element.style.borderBottomLeftRadius = radius;
        element.style.borderBottomRightRadius = radius;
    }

    private static void StylePanel(VisualElement element, float? top = null, float? left = null, float? bottom = null, float? right = null)
    {
        element.style.position = Position.Absolute;
        if (top.HasValue) element.style.top = top.Value;
        if (left.HasValue) element.style.left = left.Value;
        if (bottom.HasValue) element.style.bottom = bottom.Value;
        if (right.HasValue) element.style.right = right.Value;
        element.style.width = 320;
        element.style.paddingLeft = 8;
        element.style.paddingRight = 10;
        element.style.paddingTop = 8;
        element.style.paddingBottom = 12;
        element.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
        SetBorderRadius(element, 6);
        // `color` is inherited in USS, so this also covers the Foldout's own
        // header label plus every control added inside it.
        element.style.color = Color.white;

        // Foldout indents its content by default (unity-foldout__content has
        // its own left margin on top of this panel's own padding) -- between
        // the two, field labels/values had noticeably less room than the
        // panel's width would suggest.
        VisualElement content = element.Q(className: "unity-foldout__content");
        if (content != null)
        {
            content.style.marginLeft = 0;
            content.style.paddingLeft = 0;
        }
    }

    // Without an explicit PanelSettings theme (see panelSettingsOverride), a
    // control's own root doesn't reliably propagate `color` down to every
    // internal sub-element -- composite controls like EnumField/TextField have
    // their own nested text-display elements that didn't consistently inherit
    // it, which is why the Animation dropdown's and Name field's text were
    // invisible. Explicitly forcing color on every descendant is redundant for
    // controls that already inherited it correctly, and harmless otherwise.
    private static void Style(VisualElement element)
    {
        element.style.marginTop = 4;
        element.style.fontSize = 12;
        element.style.color = Color.white;
        element.Query<VisualElement>().ForEach(descendant => descendant.style.color = Color.white);

        // A fixed, narrower label leaves more of the panel's width for the
        // value/dropdown itself, which is what was getting clipped.
        Label label = element switch
        {
            Toggle field => field.labelElement,
            TextField field => field.labelElement,
            IntegerField field => field.labelElement,
            SliderInt field => field.labelElement,
            EnumField field => field.labelElement,
            Slider field => field.labelElement,
            MinMaxSlider field => field.labelElement,
            _ => null
        };
        if (label != null)
        {
            label.style.width = 80;
            label.style.fontSize = 12;
            label.style.overflow = Overflow.Visible;
            label.style.textOverflow = TextOverflow.Clip;
        }

        // TextField/IntegerField's own internal input box renders with a
        // light background by default (unlike Toggle/EnumField's chrome) --
        // forcing text white without addressing that left "Name" literally
        // white-on-white. Give just that inner box an explicit dark,
        // translucent background so white text has contrast regardless of the
        // control's own default chrome.
        if (element is TextField || element is IntegerField)
        {
            VisualElement input = element.Q(className: "unity-base-text-field__input")
                                ?? element.Q(className: "unity-text-field__input")
                                ?? element.Q(className: "unity-integer-field__input");
            if (input != null)
            {
                input.style.backgroundColor = new Color(1f, 1f, 1f, 0.15f);
                input.style.color = Color.white;
                SetBorderRadius(input, 3);
            }
        }
    }

    private static void StyleButton(Button button, bool destructive = false)
    {
        Style(button);
        button.style.backgroundColor = destructive ? new Color(0.55f, 0.25f, 0.2f) : new Color(0.25f, 0.45f, 0.85f);
        button.style.color = Color.white;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.paddingTop = 6;
        button.style.paddingBottom = 6;
        button.style.marginTop = 6;
        SetBorderRadius(button, 4);
        button.style.borderTopWidth = 0;
        button.style.borderBottomWidth = 0;
        button.style.borderLeftWidth = 0;
        button.style.borderRightWidth = 0;
    }

    private void GenerateCreature()
    {
        if (mode == GenerationMode.Multiple)
        {
            GenerateBatch();
            return;
        }

        if (target == null)
        {
            target = CreateCreature();
        }

        target.Generate();
        seedField.SetValueWithoutNotify(target.seed);
        nameField.SetValueWithoutNotify(target.creatureName);
        if (creatureNameDisplay != null)
        {
            creatureNameDisplay.text = target.creatureName;
        }
        statusLabel.text = $"Generated (seed {target.seed})";

        Bounds bounds = OrbitCamera.ComputeNodeBounds(target.body != null ? target.body : target.gameObject);
        FrameCamera(bounds.center, bounds.extents.magnitude, target.transform);
    }

    private void GenerateBatch()
    {
        CreatureBatchGenerator batch = EnsureBatchGenerator();
        if (batchCountField != null)
        {
            batch.count = batchCountField.value;
        }

        // Complexity/Add Eyes/Animation are shared settings, read straight off
        // `target` even though it has no body of its own while in Multiple mode.
        batch.GenerateBatch(target.complexity, target.addEyes, target.animationMode, target.nodePrefab, target.eyePrefab);
        statusLabel.text = $"Generated {batch.Creatures.Count} creatures";

        Bounds bounds = batch.ComputeBounds();
        FrameCamera(bounds.center, bounds.extents.magnitude, null);
    }

    private void MutateCreature()
    {
        if (target == null || target.body == null)
        {
            statusLabel.text = "Generate a creature first.";
            return;
        }

        target.MutatePart();
        statusLabel.text = "Mutated a limb.";
    }

    // Clear is the one destructive action in this panel that previously had
    // no way back -- captures the body (Node hierarchy: position/size/
    // hierarchy, not eyes/skeleton, same limitation as NodeEditController's
    // DeleteSelected undo) before clearing and pushes it onto
    // NodeEditController's shared undo/redo stack, so Ctrl+Z restores it.
    private void ClearCreature()
    {
        if (mode == GenerationMode.Multiple)
        {
            batchGenerator?.ClearBatch();
            statusLabel.text = "Cleared";
            return;
        }

        if (target == null || target.body == null)
        {
            if (target != null)
            {
                target.Clear();
            }
            statusLabel.text = "Cleared";
            return;
        }

        CreatureGenerator generatorRef = target;
        CreatureData snapshot = CreatureIO.CaptureCreature(target.gameObject, target.creatureName);
        string previousName = target.creatureName;

        generatorRef.Clear();

        NodeEditController nodeEditController = GetMainCameraComponent<NodeEditController>();
        if (nodeEditController == null)
        {
            statusLabel.text = "Cleared";
            return;
        }

        nodeEditController.RecordAction(
            () => // undo: rebuild the body from the snapshot
            {
                if (generatorRef == null)
                {
                    return;
                }
                List<GameObject> roots = CreatureIO.BuildCreature(snapshot, generatorRef.transform, generatorRef.nodePrefab);
                if (roots.Count == 0)
                {
                    return;
                }
                generatorRef.RestoreBody(roots[0], previousName);
                if (nameField != null)
                {
                    nameField.SetValueWithoutNotify(generatorRef.creatureName);
                }
                if (creatureNameDisplay != null)
                {
                    creatureNameDisplay.text = generatorRef.creatureName;
                }
            },
            () => // redo: clear again
            {
                generatorRef?.Clear();
            });

        statusLabel.text = "Cleared (Ctrl+Z to undo)";
    }

    private void ExportCreatureObj()
    {
        if (target == null || target.body == null)
        {
            statusLabel.text = "Generate a creature first.";
            return;
        }

        string path = PickSavePath($"Creature_{target.seed}", "obj");
        if (path == null)
        {
            return; // cancelled, or no dialog available outside the Editor (see PickSavePath)
        }

        BMesh bmesh = target.GetComponent<BMesh>();
        MeshExportData data = bmesh.PrepareExport();
        MeshExporter.ExportToOBJ(data, path);
        statusLabel.text = $"Exported to {path}";
    }

    // Unlike the OBJ export (a one-way geometry dump for other 3D software),
    // this uses CreatureIO's Node-hierarchy format -- the one format that can
    // actually be reloaded back into an editable creature (see
    // ImportCreature()), which is what a "save/load a creature" pair needs.
    // Eyes/skeleton aren't captured, only body shape (position/size/hierarchy).
    private void ExportCreatureJson()
    {
        if (target == null || target.body == null)
        {
            statusLabel.text = "Generate a creature first.";
            return;
        }

        string path = PickSavePath($"Creature_{target.seed}", "json");
        if (path == null)
        {
            return;
        }

        CreatureIO.ExportToFile(target.gameObject, target.creatureName, path);
        statusLabel.text = $"Exported to {path}";
    }

    private void ImportCreature()
    {
        if (target == null)
        {
            target = CreateCreature();
        }

        string path = PickOpenPath("json");
        if (path == null)
        {
            statusLabel.text = "Import cancelled or no exported creature found.";
            return;
        }

        target.ImportFromFile(path);
        nameField.SetValueWithoutNotify(target.creatureName);
        if (creatureNameDisplay != null)
        {
            creatureNameDisplay.text = target.creatureName;
        }
        statusLabel.text = $"Imported {Path.GetFileName(path)}";

        Bounds bounds = OrbitCamera.ComputeNodeBounds(target.body != null ? target.body : target.gameObject);
        FrameCamera(bounds.center, bounds.extents.magnitude, target.transform);
    }

    // A native save dialog needs the UnityEditor assembly, which only exists
    // when running inside the Editor (Play mode) -- there's no built-in,
    // plugin-free way to show one from an actual (standalone/browser) build,
    // and WebGL specifically can't show OS file dialogs at all. Outside the
    // Editor this falls back to a fixed location instead of failing; returns
    // null only when the user actively cancels the Editor dialog.
    private static string PickSavePath(string defaultName, string extension)
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.SaveFilePanel("Export Creature", Application.persistentDataPath, defaultName, extension);
        return string.IsNullOrEmpty(path) ? null : path;
#else
        return Path.Combine(Application.persistentDataPath, $"{defaultName}.{extension}");
#endif
    }

    // Outside the Editor, falls back to the most recently written matching
    // export in persistentDataPath instead of a picker -- see PickSavePath.
    private static string PickOpenPath(string extension)
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Import Creature", Application.persistentDataPath, extension);
        return string.IsNullOrEmpty(path) ? null : path;
#else
        string[] files = Directory.Exists(Application.persistentDataPath)
            ? Directory.GetFiles(Application.persistentDataPath, $"*.{extension}")
            : System.Array.Empty<string>();
        return files.Length > 0 ? files.OrderByDescending(File.GetLastWriteTimeUtc).First() : null;
#endif
    }

    private CreatureBatchGenerator EnsureBatchGenerator()
    {
        if (batchGenerator == null)
        {
            GameObject go = new GameObject("CreatureBatch");
            batchGenerator = go.AddComponent<CreatureBatchGenerator>();
        }
        return batchGenerator;
    }

    private static void FrameCamera(Vector3 center, float radius, Transform followTarget)
    {
        OrbitCamera orbitCamera = GetMainCameraComponent<OrbitCamera>();
        if (orbitCamera != null)
        {
            orbitCamera.Frame(center, radius, followTarget);
        }
    }

    private CreatureGenerator CreateCreature()
    {
        GameObject go = new GameObject("Creature");
        go.transform.position = DefaultSpawnPosition();
        go.AddComponent<BMesh>();
        CreatureGenerator generator = go.AddComponent<CreatureGenerator>();

        OrbitCamera orbitCamera = GetMainCameraComponent<OrbitCamera>();
        if (orbitCamera != null)
        {
            orbitCamera.target = go.transform;
        }

        return generator;
    }

    // Camera.main is only ever null-checked here for this one purpose --
    // fetching a component off it -- across the whole file.
    private static T GetMainCameraComponent<T>() where T : Component
    {
        return Camera.main != null ? Camera.main.GetComponent<T>() : null;
    }

    private static Vector3 DefaultSpawnPosition()
    {
        if (Camera.main != null)
        {
            return Camera.main.transform.position + Camera.main.transform.forward * 5f;
        }
        return Vector3.zero;
    }

    private static PanelSettings CreateDefaultPanelSettings()
    {
        return ScriptableObject.CreateInstance<PanelSettings>();
    }
}
