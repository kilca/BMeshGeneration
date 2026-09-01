// ============================================================================
// Runtime Creature Generator Panel (UI Toolkit)
// ============================================================================
// A small in-game panel to generate/customize/export a single creature, a
// second collapsible panel listing NodeEditController's shortcuts, a top-center
// creature name display, and a top-right temporary toast log. Meant to work
// both while testing in the Editor's Play mode and in an actual build (no
// UnityEditor dependency anywhere in this file, unlike CreatureMenu.cs which is
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

    [Tooltip("Seconds a toast (export result, error, warning) stays on screen.")]
    public float errorToastLifetime = 3.5f;

    public enum ToastKind { Info, Success, Warning, Error }

    private enum ButtonKind { Primary, Destructive, Positive }

    // ------ design tokens ------
    private static readonly Color Accent = new Color(0.545f, 0.361f, 0.965f);       // #8B5CF6
    private static readonly Color AccentDim = new Color(0.36f, 0.24f, 0.68f);
    private static readonly Color PanelBg = new Color(0.055f, 0.055f, 0.072f, 0.94f);
    private static readonly Color PanelBorder = new Color(1f, 1f, 1f, 0.07f);
    private static readonly Color ControlBg = new Color(1f, 1f, 1f, 0.045f);
    private static readonly Color ControlBorder = new Color(1f, 1f, 1f, 0.09f);
    private static readonly Color TextPrimary = new Color(0.92f, 0.92f, 0.94f);
    private static readonly Color TextMuted = new Color(0.55f, 0.55f, 0.61f);

    private BMesh.ShowMode shownShowMode = (BMesh.ShowMode)(-1);
    private readonly Dictionary<BMesh.ShowMode, VisualElement> showModeButtons = new Dictionary<BMesh.ShowMode, VisualElement>();
    private VisualElement editButton;

    // True whenever the pointer is over one of the panels -- read by OrbitCamera
    // so the scroll wheel scrolls the panel instead of also zooming the camera.
    public static bool PointerOverPanel { get; private set; }

    private static readonly (string key, string description)[] Shortcuts =
    {
        ("Edit Nodes", "the toggle above -- required for everything below"),
        ("Click", "select a node"),
        ("Click + drag", "move the selected node"),
        ("Tab", "select next node"),
        ("+ / -", "resize selected node"),
        ("N", "add a child node"),
        ("E", "add an eye at the selected node"),
        ("Delete / Backspace", "delete selected node"),
        ("G", "force a mesh refresh"),
        ("Ctrl+Z / Ctrl+Y", "undo / redo"),
        ("R", "generate a new creature"),
        ("Right-click + drag", "orbit camera"),
        ("Scroll wheel", "zoom"),
    };

    private Label statusLabel;
    private Label creatureNameDisplay;
    private IntegerField seedField;
    private TextField nameField;
    private VisualElement errorToastContainer;

    private bool overGeneratorPanel;
    private bool overShortcutsPanel;

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

        // Covers both a true zero-setup start (no target at all) and a scene
        // that already wires up a target CreatureGenerator in the Inspector
        // but hasn't generated a body for it yet (e.g. AutoGeneration.unity) --
        // either way, there's nothing to show yet unless we generate one now.
        bool needsInitialGeneration = target == null || target.body == null;
        if (target == null)
        {
            target = CreateCreature();
        }

        VisualElement root = document.rootVisualElement;
        root.Clear();
        root.style.unityFontStyleAndWeight = FontStyle.Normal;
        root.Add(BuildGeneratorPanel());
        root.Add(BuildKeybindingsPanel());
        root.Add(BuildNameDisplay());
        root.Add(BuildShowModeBar());
        root.Add(BuildErrorToastContainer());

        Application.logMessageReceived += HandleLogMessage;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Stop the browser page/canvas from also scrolling when the wheel is
        // used over the Unity view.
        WebFileBridge.PreventCanvasScroll();
#endif

        if (needsInitialGeneration)
        {
            // Default startup creature: fixed and reproducible rather than
            // blank or randomly seeded. Only overrides the seed for this one
            // generation -- randomizeSeedOnGenerate is restored right after,
            // so every later Generate() still randomizes normally instead of
            // getting stuck reproducing this same creature forever.
            bool randomizeAfterwards = target.randomizeSeedOnGenerate;
            target.randomizeSeedOnGenerate = false;
            target.seed = 2075170032;
            GenerateCreature();
            target.randomizeSeedOnGenerate = randomizeAfterwards;
        }
    }

    void OnDisable()
    {
        Application.logMessageReceived -= HandleLogMessage;
        overGeneratorPanel = false;
        overShortcutsPanel = false;
        PointerOverPanel = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R) && !IsTextInputFocused())
        {
            GenerateCreature();
        }

        // Keep the top-right bar in sync with state changed in code (ClearSkeleton
        // resets showMode; editNodes can toggle off on its own).
        if (target != null && showModeButtons.Count > 0)
        {
            BMesh bmesh = target.GetComponent<BMesh>();
            if (bmesh != null && bmesh.showMode != shownShowMode)
            {
                SetShowMode(bmesh.showMode);
            }
        }
        NodeEditController nec = GetMainCameraComponent<NodeEditController>();
        if (nec != null && editButton != null)
        {
            bool editOn = nec.editNodes;
            if ((editButton.resolvedStyle.backgroundColor.a > 0.5f) != editOn)
            {
                RefreshEditButton(editOn);
            }
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
        Foldout foldout = new Foldout { text = "CREATURE GENERATOR", value = true };
        StylePanel(foldout, top: 16, left: 16);
        foldout.RegisterCallback<PointerEnterEvent>(_ => { overGeneratorPanel = true; RefreshPointerOverPanel(); });
        foldout.RegisterCallback<PointerLeaveEvent>(_ => { overGeneratorPanel = false; RefreshPointerOverPanel(); });

        // DNA glyph left of the title.
        Label headerLbl = foldout.Q<Toggle>(className: "unity-foldout__toggle")?.Q<Label>();
        if (headerLbl != null && headerLbl.parent != null)
        {
            VisualElement dna = SvgIcon.Create(SvgIcon.Dna, Accent, 17f);
            dna.style.marginRight = 8;
            dna.style.marginLeft = 2;
            headerLbl.parent.Insert(headerLbl.parent.IndexOf(headerLbl), dna);
        }

        // The panel's own content can get taller than the viewport -- especially
        // the Shape/Size/Branching tuning foldouts expanded together, or a short
        // WebGL canvas -- so it scrolls inside a capped-height ScrollView instead
        // of the Foldout just growing off the bottom of the screen. The Foldout's
        // own header stays outside the ScrollView, so it's always visible.
        ScrollView scrollView = CreatePanelScrollView();
        foldout.Add(scrollView);

        Label subtitle = new Label("Design procedural creatures");
        subtitle.style.color = TextMuted;
        subtitle.style.fontSize = 11;
        subtitle.style.marginBottom = 8;
        scrollView.Add(subtitle);

        // ------ Customization ------

        SliderInt complexitySlider = new SliderInt("Complexity", 1, 4) { value = target.complexity };
        complexitySlider.RegisterValueChangedCallback(evt => target.complexity = evt.newValue);
        Style(complexitySlider);
        scrollView.Add(complexitySlider);

        Toggle eyesToggle = new Toggle("Add Eyes") { value = target.addEyes };
        eyesToggle.RegisterValueChangedCallback(evt =>
        {
            target.addEyes = evt.newValue;
            target.SetEyesVisible(evt.newValue); // hide/show the eyes already on this creature
        });
        Style(eyesToggle);
        scrollView.Add(eyesToggle);

        // Skin is always applied by Generate(); this picks whether/how the
        // skeleton is animated on top of it (see CreatureIdleSway).
        EnumField animationModeField = new EnumField("Animation", target.animationMode);
        animationModeField.RegisterValueChangedCallback(evt =>
        {
            target.animationMode = (CreatureGenerator.AnimationMode)evt.newValue;
            target.ApplyAnimationMode();
        });
        Style(animationModeField);
        scrollView.Add(animationModeField);

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
        scrollView.Add(nameField);

        // (Show Mode lives in the top-right icon bar -- see BuildShowModeBar.)

        Toggle randomSeedToggle = new Toggle("Random Seed") { value = target.randomizeSeedOnGenerate };
        Style(randomSeedToggle);
        scrollView.Add(randomSeedToggle);

        seedField = new IntegerField("Seed") { value = target.seed };
        seedField.SetEnabled(!target.randomizeSeedOnGenerate);
        seedField.RegisterValueChangedCallback(evt => target.seed = evt.newValue);
        Style(seedField);
        seedField.style.minWidth = 170; // the label + a full Ticks-based seed otherwise gets clipped
        scrollView.Add(seedField);

        randomSeedToggle.RegisterValueChangedCallback(evt =>
        {
            target.randomizeSeedOnGenerate = evt.newValue;
            seedField.SetEnabled(!evt.newValue);
        });

        // (Edit Nodes lives in the top-right icon bar -- see BuildShowModeBar.)

        // ------ Generation bias tuning ------

        scrollView.Add(BuildShapeSection());
        scrollView.Add(BuildSizeSection());
        scrollView.Add(BuildBranchingSection());

        // ------ Actions ------

        Button generateButton = new Button(GenerateCreature) { text = "Generate (R)" };
        StyleButton(generateButton);
        scrollView.Add(generateButton);

        scrollView.Add(BuildExportGroup());

        Button clearButton = new Button(ClearCreature) { text = "Clear" };
        StyleButton(clearButton, ButtonKind.Destructive);
        scrollView.Add(clearButton);

        statusLabel = new Label(string.Empty);
        statusLabel.style.color = TextMuted;
        statusLabel.style.fontSize = 11;
        statusLabel.style.marginTop = 8;
        statusLabel.style.whiteSpace = WhiteSpace.Normal;
        scrollView.Add(statusLabel);

        return foldout;
    }

    // Save / load a creature, kept together just above Clear and in green so
    // they read as "the way stuff leaves / enters" rather than another edit.
    private VisualElement BuildExportGroup()
    {
        VisualElement group = new VisualElement();
        group.style.marginTop = 6;

        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;

        Button exportGltfButton = IconButton(SvgIcon.DownloadSimple, "Export (glb)", ButtonKind.Positive, ExportCreatureGltf);
        exportGltfButton.style.flexGrow = 1;
        exportGltfButton.style.flexBasis = 0f;
        exportGltfButton.style.flexShrink = 1;
        exportGltfButton.style.minWidth = 0;
        exportGltfButton.style.marginRight = 4;
        row.Add(exportGltfButton);

        Button exportJsonButton = IconButton(SvgIcon.DownloadSimple, "Export (json)", ButtonKind.Positive, ExportCreatureJson);
        exportJsonButton.style.flexGrow = 1;
        exportJsonButton.style.flexBasis = 0f;
        exportJsonButton.style.flexShrink = 1;
        exportJsonButton.style.minWidth = 0;
        row.Add(exportJsonButton);

        group.Add(row);

        Button importButton = IconButton(SvgIcon.UploadSimple, "Import Creature", ButtonKind.Positive, ImportCreature);
        group.Add(importButton);

        return group;
    }

    // A Button laid out as [icon] [label] rather than a plain text button.
    private Button IconButton(string iconPath, string text, ButtonKind kind, System.Action onClick)
    {
        Button button = new Button(onClick);
        StyleButton(button, kind);
        button.style.flexDirection = FlexDirection.Row;
        button.style.alignItems = Align.Center;
        button.style.justifyContent = Justify.Center;

        VisualElement icon = SvgIcon.Create(iconPath, Color.white, 14f);
        icon.style.marginRight = 6;
        button.Add(icon);

        Label label = new Label(text);
        label.style.color = Color.white;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.fontSize = 12;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.overflow = Overflow.Hidden;
        label.pickingMode = PickingMode.Ignore;
        button.Add(label);

        return button;
    }

    // Top-right icon bar: the mutually-exclusive preview modes (Mesh / Wireframe
    // / Gizmo) plus an independent "Edit" toggle for node editing. Icons come
    // from Assets/UI/Textures (embedded in SvgIcon).
    private VisualElement BuildShowModeBar()
    {
        showModeButtons.Clear();

        VisualElement container = new VisualElement();
        container.style.position = Position.Absolute;
        container.style.top = 16;
        container.style.right = 16;
        container.style.flexDirection = FlexDirection.Row;
        container.style.alignItems = Align.Center;
        container.style.backgroundColor = PanelBg;
        container.style.paddingLeft = 4;
        container.style.paddingRight = 4;
        container.style.paddingTop = 4;
        container.style.paddingBottom = 4;
        SetBorderRadius(container, 12);
        SetBorder(container, PanelBorder, 1f);
        container.RegisterCallback<PointerEnterEvent>(_ => { overGeneratorPanel = true; RefreshPointerOverPanel(); });
        container.RegisterCallback<PointerLeaveEvent>(_ => { overGeneratorPanel = false; RefreshPointerOverPanel(); });

        (BMesh.ShowMode mode, string icon, string tip)[] modes =
        {
            (BMesh.ShowMode.Mesh, SvgIcon.Cube, "Mesh"),
            (BMesh.ShowMode.Wireframe, SvgIcon.CubeTransparent, "Wireframe"),
            (BMesh.ShowMode.Gizmo, SvgIcon.Aperture, "Structure"),
        };

        foreach ((BMesh.ShowMode mode, string iconPath, string tip) in modes)
        {
            BMesh.ShowMode captured = mode;
            Button b = MakeBarButton(iconPath, tip, () => SetShowMode(captured));
            showModeButtons[mode] = b;
            container.Add(b);
        }

        // divider
        VisualElement divider = new VisualElement();
        divider.style.width = 1;
        divider.style.height = 20;
        divider.style.marginLeft = 4;
        divider.style.marginRight = 4;
        divider.style.backgroundColor = PanelBorder;
        container.Add(divider);

        editButton = MakeBarButton(SvgIcon.CubeFocus, "Edit nodes", () =>
        {
            NodeEditController nec = GetMainCameraComponent<NodeEditController>();
            if (nec != null)
            {
                nec.editNodes = !nec.editNodes;
                RefreshEditButton(nec.editNodes);
            }
        });
        container.Add(editButton);

        BMesh initial = target != null ? target.GetComponent<BMesh>() : null;
        RefreshShowModeBar(initial != null ? initial.showMode : BMesh.ShowMode.Mesh);
        NodeEditController controller = GetMainCameraComponent<NodeEditController>();
        RefreshEditButton(controller != null && controller.editNodes);

        return container;
    }

    private Button MakeBarButton(string iconPath, string tip, System.Action onClick)
    {
        Button b = new Button(onClick) { tooltip = tip };
        b.style.width = 34;
        b.style.height = 34;
        b.style.marginLeft = 2;
        b.style.marginRight = 2;
        b.style.marginTop = 0;
        b.style.marginBottom = 0;
        b.style.paddingLeft = 0;
        b.style.paddingRight = 0;
        b.style.paddingTop = 0;
        b.style.paddingBottom = 0;
        b.style.backgroundColor = Color.clear;
        b.style.alignItems = Align.Center;
        b.style.justifyContent = Justify.Center;
        SetBorderRadius(b, 8);
        SetBorder(b, Color.clear, 0f);

        VisualElement icon = SvgIcon.Create(iconPath, TextMuted, 18f);
        b.Add(icon);
        b.userData = icon;

        // Subtle hover only while inactive (an active button is already purple).
        b.RegisterCallback<PointerEnterEvent>(_ =>
        {
            if (b.resolvedStyle.backgroundColor.a < 0.05f)
            {
                b.style.backgroundColor = new Color(1f, 1f, 1f, 0.07f);
            }
        });
        b.RegisterCallback<PointerLeaveEvent>(_ =>
        {
            if (b.resolvedStyle.backgroundColor.a < 0.12f)
            {
                b.style.backgroundColor = Color.clear;
            }
        });
        return b;
    }

    private void SetShowMode(BMesh.ShowMode mode)
    {
        BMesh bmesh = target != null ? target.GetComponent<BMesh>() : null;
        if (bmesh != null)
        {
            bmesh.showMode = mode;
        }

        // The "Structure" (Gizmo) view hides the mesh and shows node markers.
        NodeEditController nec = GetMainCameraComponent<NodeEditController>();
        if (nec != null)
        {
            nec.showNodes = mode == BMesh.ShowMode.Gizmo;
        }

        RefreshShowModeBar(mode);
    }

    private void RefreshShowModeBar(BMesh.ShowMode active)
    {
        shownShowMode = active;
        foreach (KeyValuePair<BMesh.ShowMode, VisualElement> kv in showModeButtons)
        {
            bool on = kv.Key == active;
            kv.Value.style.backgroundColor = on ? Accent : Color.clear;
            if (kv.Value.userData is VisualElement icon)
            {
                SvgIcon.Recolor(icon, on ? Color.white : TextMuted);
            }
        }
    }

    private void RefreshEditButton(bool on)
    {
        if (editButton == null)
        {
            return;
        }
        editButton.style.backgroundColor = on ? Accent : Color.clear;
        if (editButton.userData is VisualElement icon)
        {
            SvgIcon.Recolor(icon, on ? Color.white : TextMuted);
        }
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
        Foldout foldout = new Foldout { text = "SHORTCUTS", value = false };
        StylePanel(foldout, bottom: 16, right: 16);
        foldout.RegisterCallback<PointerEnterEvent>(_ => { overShortcutsPanel = true; RefreshPointerOverPanel(); });
        foldout.RegisterCallback<PointerLeaveEvent>(_ => { overShortcutsPanel = false; RefreshPointerOverPanel(); });

        ScrollView scrollView = CreatePanelScrollView();
        foldout.Add(scrollView);

        foreach ((string key, string description) in Shortcuts)
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 2;

            Label keyLabel = new Label(key);
            keyLabel.style.color = Accent;
            keyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            keyLabel.style.fontSize = 11;
            keyLabel.style.minWidth = 120;
            keyLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(keyLabel);

            Label descriptionLabel = new Label(description);
            descriptionLabel.style.color = TextMuted;
            descriptionLabel.style.fontSize = 11;
            descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
            descriptionLabel.style.flexShrink = 1;
            row.Add(descriptionLabel);

            scrollView.Add(row);
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
        container.style.alignItems = Align.Center;
        container.pickingMode = PickingMode.Ignore;

        VisualElement pill = new VisualElement();
        pill.style.alignItems = Align.Center;
        pill.style.backgroundColor = PanelBg;
        pill.style.paddingLeft = 28;
        pill.style.paddingRight = 28;
        pill.style.paddingTop = 7;
        pill.style.paddingBottom = 8;
        SetBorderRadius(pill, 14);
        SetBorder(pill, PanelBorder, 1f);

        creatureNameDisplay = new Label(target != null ? target.creatureName : string.Empty);
        creatureNameDisplay.style.color = TextPrimary;
        creatureNameDisplay.style.fontSize = 20;
        creatureNameDisplay.style.unityFontStyleAndWeight = FontStyle.Bold;
        pill.Add(creatureNameDisplay);

        container.Add(pill);
        return container;
    }

    // Mirrors the two per-panel hover flags into the static OrbitCamera reads.
    private void RefreshPointerOverPanel()
    {
        PointerOverPanel = overGeneratorPanel || overShortcutsPanel;
    }

    private VisualElement BuildErrorToastContainer()
    {
        errorToastContainer = new VisualElement();
        errorToastContainer.style.position = Position.Absolute;
        errorToastContainer.style.top = 64; // below the show-mode bar
        errorToastContainer.style.right = 16;
        errorToastContainer.style.width = 300;
        errorToastContainer.style.flexDirection = FlexDirection.Column;
        errorToastContainer.pickingMode = PickingMode.Ignore;
        return errorToastContainer;
    }

    private void HandleLogMessage(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Warning)
        {
            ShowToast(message, ToastKind.Warning);
        }
        else if (type == LogType.Error || type == LogType.Exception)
        {
            ShowToast(message, ToastKind.Error);
        }
    }

    // The one place a transient message reaches the user -- errors/warnings from
    // the Unity log stream (see HandleLogMessage) and export/import results.
    public void ShowToast(string message, ToastKind kind = ToastKind.Info)
    {
        if (errorToastContainer == null || string.IsNullOrEmpty(message))
        {
            return;
        }

        Color bg = kind switch
        {
            ToastKind.Success => new Color(0.14f, 0.44f, 0.28f, 0.96f),
            ToastKind.Warning => new Color(0.5f, 0.42f, 0.08f, 0.96f),
            ToastKind.Error => new Color(0.56f, 0.15f, 0.17f, 0.96f),
            _ => new Color(0.13f, 0.13f, 0.16f, 0.96f),
        };

        Label toast = new Label(message);
        toast.style.color = Color.white;
        toast.style.backgroundColor = bg;
        toast.style.whiteSpace = WhiteSpace.Normal;
        toast.style.fontSize = 11;
        toast.style.marginBottom = 6;
        toast.style.paddingLeft = 12;
        toast.style.paddingRight = 12;
        toast.style.paddingTop = 9;
        toast.style.paddingBottom = 9;
        SetBorderRadius(toast, 10);
        SetBorder(toast, new Color(1f, 1f, 1f, 0.1f), 1f);

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

    private static void SetBorder(VisualElement element, Color color, float width)
    {
        element.style.borderTopColor = color;
        element.style.borderBottomColor = color;
        element.style.borderLeftColor = color;
        element.style.borderRightColor = color;
        element.style.borderTopWidth = width;
        element.style.borderBottomWidth = width;
        element.style.borderLeftWidth = width;
        element.style.borderRightWidth = width;
    }

    private static void StylePanel(VisualElement element, float? top = null, float? left = null, float? bottom = null, float? right = null)
    {
        element.style.position = Position.Absolute;
        if (top.HasValue) element.style.top = top.Value;
        if (left.HasValue) element.style.left = left.Value;
        if (bottom.HasValue) element.style.bottom = bottom.Value;
        if (right.HasValue) element.style.right = right.Value;
        element.style.width = 320;
        element.style.paddingLeft = 14;
        element.style.paddingRight = 14;
        element.style.paddingTop = 12;
        element.style.paddingBottom = 14;
        element.style.backgroundColor = PanelBg;
        SetBorderRadius(element, 16);
        SetBorder(element, PanelBorder, 1f);
        // `color` is inherited in USS, so this also covers the Foldout's own
        // header label plus every control added inside it.
        element.style.color = TextPrimary;

        // Cap the panel to a share of the screen instead of letting it grow
        // past the bottom -- both the generator panel (every tuning foldout
        // expanded) and the shortcuts list can get taller than a short WebGL
        // canvas. `maxHeight` resolves against the actual viewport, not an
        // auto-sized ancestor, because this element itself is
        // Position.Absolute -- same reasoning as the top/left/bottom/right
        // offsets above. The ScrollView added inside (see
        // CreatePanelScrollView) is what actually scrolls once this cap
        // kicks in.
        element.style.maxHeight = Length.Percent(80);
        element.style.overflow = Overflow.Hidden;

        // Foldout indents its content by default (unity-foldout__content has
        // its own left margin on top of this panel's own padding) -- between
        // the two, field labels/values had noticeably less room than the
        // panel's width would suggest.
        VisualElement content = element.Q(className: "unity-foldout__content");
        if (content != null)
        {
            content.style.marginLeft = 0;
            content.style.paddingLeft = 0;
            // Let the content row (and the ScrollView inside it) shrink
            // below its natural size instead of pushing the panel past
            // maxHeight -- flexbox's default min-height:auto would otherwise
            // keep it at full content size and defeat the cap above.
            content.style.flexGrow = 1;
            content.style.minHeight = 0;
        }

        // The header toggle (the foldout's clickable title bar) must keep
        // its own size always -- only the scrollable content below it should
        // give up space when the panel is capped.
        Toggle header = element.Q<Toggle>(className: "unity-foldout__toggle");
        if (header != null)
        {
            header.style.flexShrink = 0;
            Label headerLabel = header.Q<Label>();
            if (headerLabel != null)
            {
                headerLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                headerLabel.style.fontSize = 14;
                headerLabel.style.color = TextPrimary;
            }
            VisualElement chevron = header.Q(className: "unity-foldout__checkmark");
            if (chevron != null)
            {
                chevron.style.unityBackgroundImageTintColor = Accent;
            }
        }
    }

    // Used inside both BuildGeneratorPanel and BuildKeybindingsPanel so their
    // (potentially long) content scrolls within StylePanel's maxHeight cap
    // instead of the panel growing past the bottom of the screen.
    private static ScrollView CreatePanelScrollView()
    {
        ScrollView scrollView = new ScrollView(ScrollViewMode.Vertical);
        scrollView.style.flexGrow = 1;
        scrollView.style.minHeight = 0;
        // Never a horizontal scrollbar -- content that's a couple of px too wide
        // just gets clipped by the panel's overflow:hidden.
        scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        scrollView.mode = ScrollViewMode.Vertical;
        scrollView.contentContainer.style.maxWidth = Length.Percent(100);

        // The default runtime scroller is a wide track with chunky up/down
        // step buttons -- slim it down to a plain thin thumb, more in
        // keeping with this panel's minimal styling and its 320px width.
        VisualElement verticalScroller = scrollView.Q(className: "unity-scroller--vertical");
        if (verticalScroller != null)
        {
            verticalScroller.style.width = 9;
            verticalScroller.style.marginLeft = 2;
            verticalScroller.style.backgroundColor = Color.clear;

            verticalScroller.Q(className: "unity-scroller__low-button")?.RemoveFromHierarchy();
            verticalScroller.Q(className: "unity-scroller__high-button")?.RemoveFromHierarchy();

            // The track behind the thumb: barely-there so the thumb reads.
            foreach (string trackClass in new[] { "unity-scroller__slider", "unity-base-slider--vertical", "unity-base-slider__tracker" })
            {
                VisualElement track = verticalScroller.Q(className: trackClass);
                if (track != null)
                {
                    track.style.backgroundColor = new Color(1f, 1f, 1f, 0.05f);
                    SetBorder(track, Color.clear, 0f);
                    track.style.marginLeft = 0;
                    track.style.marginRight = 0;
                }
            }

            // The thumb: purple, clearly on top of the dark panel + faint track.
            VisualElement dragger = verticalScroller.Q(className: "unity-base-slider__dragger");
            if (dragger != null)
            {
                dragger.style.backgroundColor = Accent;
                SetBorder(dragger, Color.clear, 0f);
                SetBorderRadius(dragger, 4);
                dragger.style.marginLeft = 0;
                dragger.style.marginRight = 0;
            }
        }

        return scrollView;
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
        element.style.marginTop = 7;
        element.style.fontSize = 12;
        element.style.color = TextPrimary;
        element.Query<VisualElement>().ForEach(descendant => descendant.style.color = TextPrimary);

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
            label.style.width = 90;
            label.style.fontSize = 12;
            label.style.color = TextMuted;
            label.style.overflow = Overflow.Visible;
            label.style.textOverflow = TextOverflow.Clip;
        }

        // Dark rounded input box for text / integer fields.
        if (element is TextField || element is IntegerField)
        {
            VisualElement input = element.Q(className: "unity-base-text-field__input")
                                ?? element.Q(className: "unity-text-field__input")
                                ?? element.Q(className: "unity-integer-field__input");
            if (input != null)
            {
                input.style.backgroundColor = ControlBg;
                input.style.color = TextPrimary;
                SetBorder(input, ControlBorder, 1f);
                SetBorderRadius(input, 7);
                input.style.paddingLeft = 6;
                input.style.paddingRight = 6;
                input.style.paddingTop = 3;
                input.style.paddingBottom = 3;
            }
        }

        // EnumField dropdown chrome.
        if (element is EnumField)
        {
            VisualElement input = element.Q(className: "unity-enum-field__input") ?? element.Q(className: "unity-base-field__input");
            if (input != null)
            {
                input.style.backgroundColor = ControlBg;
                SetBorder(input, ControlBorder, 1f);
                SetBorderRadius(input, 7);
                input.style.paddingLeft = 6;
                input.style.paddingRight = 6;
                input.style.paddingTop = 3;
                input.style.paddingBottom = 3;
            }
        }

        // Purple sliders.
        if (element is Slider || element is SliderInt || element is MinMaxSlider)
        {
            element.Query(className: "unity-base-slider__dragger").ForEach(d =>
            {
                d.style.backgroundColor = Accent;
                SetBorder(d, Color.clear, 0f);
                SetBorderRadius(d, 7);
            });
            element.Query(className: "unity-base-slider__tracker").ForEach(t =>
            {
                t.style.backgroundColor = new Color(1f, 1f, 1f, 0.22f);
                SetBorder(t, Color.clear, 0f);
                SetBorderRadius(t, 2);
            });
        }

        // Nested tuning foldouts (Shape / Size / Branching): purple chevron,
        // slightly bolder header.
        if (element is Foldout fold)
        {
            Toggle ft = fold.Q<Toggle>(className: "unity-foldout__toggle");
            if (ft != null)
            {
                Label fl = ft.Q<Label>();
                if (fl != null)
                {
                    fl.style.color = TextPrimary;
                    fl.style.fontSize = 12;
                    fl.style.unityFontStyleAndWeight = FontStyle.Bold;
                }
                VisualElement fc = ft.Q(className: "unity-foldout__checkmark");
                if (fc != null)
                {
                    fc.style.unityBackgroundImageTintColor = Accent;
                }
            }
        }

        // Purple check for toggles (a rounded box, filled when on).
        if (element is Toggle toggle)
        {
            VisualElement checkmark = toggle.Q(className: "unity-toggle__checkmark");
            if (checkmark != null)
            {
                checkmark.style.width = 18;
                checkmark.style.height = 18;
                SetBorderRadius(checkmark, 5);
                SetBorder(checkmark, ControlBorder, 1f);
                checkmark.style.unityBackgroundImageTintColor = Color.white;

                void Paint(bool on) => checkmark.style.backgroundColor = on ? Accent : ControlBg;
                Paint(toggle.value);
                toggle.RegisterValueChangedCallback(e => Paint(e.newValue));
            }
        }
    }

    private static void StyleButton(Button button, ButtonKind kind = ButtonKind.Primary)
    {
        button.style.marginTop = 7;
        button.style.fontSize = 12;

        Color baseColor = kind switch
        {
            ButtonKind.Destructive => new Color(0.62f, 0.24f, 0.26f),
            ButtonKind.Positive => Accent,
            _ => Accent,
        };
        button.userData = baseColor;
        button.style.backgroundColor = baseColor;

        button.style.color = Color.white;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.paddingTop = 9;
        button.style.paddingBottom = 9;
        SetBorderRadius(button, 10);
        SetBorder(button, Color.clear, 0f);

        button.RegisterCallback<PointerEnterEvent>(_ =>
        {
            if (button.userData is Color c)
            {
                button.style.backgroundColor = Color.Lerp(c, Color.white, 0.14f);
            }
        });
        button.RegisterCallback<PointerLeaveEvent>(_ =>
        {
            if (button.userData is Color c)
            {
                button.style.backgroundColor = c;
            }
        });
    }

    private void GenerateCreature()
    {
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

    // No button any more, but the logic (CreatureGenerator.MutatePart) is kept
    // for future use -- wire this back to a Button to re-expose it.
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

    // glTF (.glb) carries the skinned mesh + skeleton + bind poses + the looping
    // idle animation (see GltfExporter / CreatureMotion) -- the format to hand a
    // rigged creature to Blender / three.js / etc. Falls back to a static-mesh
    // .glb when the creature has no skeleton (Animation = None).
    private void ExportCreatureGltf()
    {
        if (target == null || target.body == null)
        {
            ShowToast("Generate a creature first.", ToastKind.Warning);
            return;
        }

        try
        {
            BMesh bmesh = target.GetComponent<BMesh>();
            Color color = MaterialColor(bmesh != null ? bmesh.normalMaterial : null);

            SkinnedMeshRenderer smr = target.skinnedMeshObject != null
                ? target.skinnedMeshObject.GetComponent<SkinnedMeshRenderer>()
                : null;

            Material skinMat = bmesh != null ? bmesh.normalMaterial : null;

            byte[] glb;
            if (smr != null && smr.sharedMesh != null)
            {
                CreatureIdleSway sway = target.GetComponent<CreatureIdleSway>();

                // The exported skeleton must be at its rest pose so it matches the
                // mesh's bind poses -- the PlayableGraph re-poses the bones next frame.
                if (sway != null)
                {
                    sway.ApplyRestPose();
                }

                Color[] vcol = GltfExporter.BakeTriplanarVertexColors(smr.sharedMesh, skinMat);
                glb = GltfExporter.BuildGlb(smr.sharedMesh, smr.bones, smr.sharedMesh.bindposes,
                                            sway != null ? sway.MotionData : null, vcol, color, target.creatureName);
            }
            else
            {
                MeshFilter mf = target.GetComponent<MeshFilter>();
                Mesh staticMesh = mf != null ? mf.sharedMesh : null;
                Color[] vcol = GltfExporter.BakeTriplanarVertexColors(staticMesh, skinMat);
                glb = GltfExporter.BuildGlb(staticMesh, null, null, null, vcol, color, target.creatureName);
            }

            string fileName = $"Creature_{target.seed}.glb";
#if UNITY_WEBGL && !UNITY_EDITOR
            WebFileBridge.DownloadBytes(fileName, glb, "model/gltf-binary");
            ShowToast($"Downloading {fileName}", ToastKind.Success);
#else
            string path = PickSavePath($"Creature_{target.seed}", "glb");
            if (path == null)
            {
                return; // user cancelled the editor dialog
            }
            File.WriteAllBytes(path, glb);
            ShowToast($"Exported to {path}", ToastKind.Success);
#endif
        }
        catch (System.Exception e)
        {
            ShowToast($"glTF export failed: {e.Message}", ToastKind.Error);
        }
    }

    private static Color MaterialColor(Material m)
    {
        if (m != null)
        {
            foreach (string prop in new[] { "_BaseColor", "_Color", "_MainColor" })
            {
                if (m.HasProperty(prop))
                {
                    return m.GetColor(prop);
                }
            }
        }
        return new Color(0.8f, 0.8f, 0.8f);
    }

    // Unlike the mesh export, this uses CreatureIO's Node-hierarchy format -- the
    // one format that can
    // actually be reloaded back into an editable creature (see ImportCreature).
    // Eyes/skeleton aren't captured, only body shape (position/size/hierarchy).
    private void ExportCreatureJson()
    {
        if (target == null || target.body == null)
        {
            ShowToast("Generate a creature first.", ToastKind.Warning);
            return;
        }

        try
        {
            string json = CreatureIO.ExportToString(target.gameObject, target.creatureName);
            string fileName = $"Creature_{target.seed}.json";
#if UNITY_WEBGL && !UNITY_EDITOR
            WebFileBridge.Download(fileName, json, "application/json");
            ShowToast($"Downloading {fileName}", ToastKind.Success);
#else
            string path = PickSavePath($"Creature_{target.seed}", "json");
            if (path == null)
            {
                return;
            }
            File.WriteAllText(path, json);
            ShowToast($"Exported to {path}", ToastKind.Success);
#endif
        }
        catch (System.Exception e)
        {
            ShowToast($"JSON export failed: {e.Message}", ToastKind.Error);
        }
    }

    private void ImportCreature()
    {
        if (target == null)
        {
            target = CreateCreature();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // The browser file picker is async -- OnCreatureFileUploaded (below)
        // runs when the user has chosen a file.
        WebFileBridge.RequestUpload(gameObject.name, nameof(OnCreatureFileUploaded), ".json");
#else
        string path = PickOpenPath("json");
        if (path == null)
        {
            ShowToast("Import cancelled or no exported creature found.", ToastKind.Warning);
            return;
        }

        try
        {
            target.ImportFromFile(path);
            AfterImport(Path.GetFileName(path));
        }
        catch (System.Exception e)
        {
            ShowToast($"Import failed: {e.Message}", ToastKind.Error);
        }
#endif
    }

    // SendMessage target for FileBridge.jslib's BMeshUploadFile -- the string is
    // the picked file's text content, or empty if the user cancelled.
    public void OnCreatureFileUploaded(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            ShowToast("Import cancelled.", ToastKind.Warning);
            return;
        }

        if (target == null)
        {
            target = CreateCreature();
        }

        try
        {
            target.ImportFromJson(json);
            AfterImport("uploaded file");
        }
        catch (System.Exception e)
        {
            ShowToast($"Import failed: {e.Message}", ToastKind.Error);
        }
    }

    private void AfterImport(string label)
    {
        if (nameField != null)
        {
            nameField.SetValueWithoutNotify(target.creatureName);
        }
        if (creatureNameDisplay != null)
        {
            creatureNameDisplay.text = target.creatureName;
        }
        ShowToast($"Imported {label}", ToastKind.Success);

        Bounds bounds = OrbitCamera.ComputeNodeBounds(target.body != null ? target.body : target.gameObject);
        FrameCamera(bounds.center, bounds.extents.magnitude, target.transform);
    }

    // A native save dialog needs the UnityEditor assembly, which only exists
    // when running inside the Editor (Play mode). WebGL exports go through
    // WebFileBridge (a browser download) instead; a plain standalone build
    // falls back to persistentDataPath.
    private static string PickSavePath(string defaultName, string extension)
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.SaveFilePanel("Export Creature", Application.persistentDataPath, defaultName, extension);
        return string.IsNullOrEmpty(path) ? null : path;
#else
        return Path.Combine(Application.persistentDataPath, $"{defaultName}.{extension}");
#endif
    }

    // Editor uses a real open dialog; a plain standalone build falls back to the
    // most recently written matching export. WebGL uses WebFileBridge instead.
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
        // World origin -- the skinned-mesh bind poses are captured in world
        // space, so an off-origin creature skins incorrectly. The camera is
        // framed onto the creature afterwards regardless of where it sits.
        GameObject go = new GameObject("Creature");
        go.transform.position = Vector3.zero;
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

    private static PanelSettings CreateDefaultPanelSettings()
    {
        return ScriptableObject.CreateInstance<PanelSettings>();
    }
}
