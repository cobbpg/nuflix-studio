using System;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.Mathematics.math;
using static Constants;
using System.Collections.Generic;
using System.IO;
using SFB;
using UnityEngine.Profiling;
using Unity.Collections;

[Serializable]
public class EditorPane
{
    public const string TabName = "editor-tab";
    public const string ProjectExtension = "nfxproj";
    public const string NufliExtension = "nuf";

    private MainWindowLogic _main;
    private VisualElement _root;
    private EditorPaneAssets _assets;
    public bool Active => _main.MainTabView.activeTab.name == TabName;

    private Material _simpleImageMaterial;
    private Material _gridImageMaterial;
    private Material _highlightMaterial;

    private VisualElement _imageEditor;
    private RenderTexture _editorTexture;

    private Vector2 _viewPos;
    private Vector2 _lastWheelDelta;
    private Vector2 _sourcePixelPos;
    private Vector2 _targetPixelPos;

    private Vector2 _dragStartViewPos;
    private Vector2 _dragStartPixelPos;

    private string _projectPath;

    private RadioButtonGroup _editModeSelector;
    private bool DirectlyEditing
    {
        get => _workImage?.DirectlyEditing ?? false;
        set
        {
            if (_workImage != null)
            {
                _workImage.DirectlyEditing = value;
            }
        }
    }

    private RadioButtonGroup _viewModeSelector;
    private EditorViewMode _viewMode;
    private bool _showBorder = true;
    private SliderInt _viewScaleSlider;
    private int _viewScale;
    private float ViewScale => _viewScale < 0 ? (4 + _viewScale) / 4f : _viewScale + 1;
    private const float RightHeaderWidth = 48 * 3;

    private Button _toggleInkButton;
    private Button _toggleHiresButton;
    private Button _toggleLoresButton;
    private Button _togglePaperButton;
    private Button _toggleErrorsButton;

    private VisualElement _penLayersContainer;
    private VisualElement _penPaletteContainer;

    private bool _showInkLayer = true;
    private bool _showLoresSpriteLayer = true;
    private bool _showHiresSpriteLayer = true;
    private bool _showPaperLayer = true;
    private bool _showErrors;

    private VisualElement _sourcePixelColor;
    private Button _setPenInkButton;
    private Button _setPenPaperButton;
    private Button _setPenSpriteButton;
    private Button _setPenMulti1Button;
    private Button _setPenMulti2Button;
    private Button _setPenMulti3Button;

    private List<Label> _penPalette;
    private Pen _activePen;
    private int _penColorPrimary;
    private int _penColorSecondary;

    private LayeredImage _workImage;
    private LayeredImage _exportedImage;
    private bool _workImageDirty;

    public Texture2D BugColors;
    public Texture2D UnderlayColors;
    public Texture2D ExportedBugColors;
    public Texture2D ExportedUnderlayColors;
    public Texture2D BorderColors;

    public Texture2D InkLayer;
    public Texture2D LoresSpriteLayer;
    public Texture2D HiresSpriteLayer;
    public Texture2D PaperLayer;
    public Texture2D ErrorMap;

    public Texture2D CyclesHistogram;

    internal void Init(MainWindowLogic main, VisualElement root, EditorPaneAssets editorPaneAssets)
    {
        _main = main;
        _root = root;
        _assets = editorPaneAssets;

        _simpleImageMaterial = new Material(_assets.SimpleImageShader);
        _gridImageMaterial = new Material(_assets.GridImageShader);
        _highlightMaterial = new Material(_assets.HighlightShader);
        SetBlitTint(Color.white);

        root.RegisterCallback<KeyDownEvent>(OnKeyDown);

        _imageEditor = root.Q<VisualElement>("image-editor");
        _imageEditor.RegisterCallback<GeometryChangedEvent>(OnEditorGeometryChanged);
        _imageEditor.RegisterCallback<WheelEvent>(OnEditorWheel);
        _imageEditor.RegisterCallback<MouseDownEvent>(OnEditorMouseDown);
        _imageEditor.RegisterCallback<MouseMoveEvent>(OnEditorMouseMove);
        _imageEditor.RegisterCallback<MouseUpEvent>(OnEditorMouseUp);
        _imageEditor.RegisterCallback<MouseLeaveEvent>(OnEditorMouseLeave);

        var projectToolbar = root.Q<VisualElement>("project-toolbar");

        projectToolbar.Q<Button>("project-load-button").clicked += LoadProject;
        projectToolbar.Q<Button>("project-save-button").clicked += SaveProject;
        projectToolbar.Q<Button>("project-save-as-button").clicked += SaveProjectAs;
        projectToolbar.Q<Button>("project-export-button").clicked += ExportProject;
        _main.SetupViceBridgeToggle(projectToolbar.Q<Toggle>("vice-bridge-toggle"));

        _editModeSelector = projectToolbar.Q<RadioButtonGroup>("edit-mode-selector");
        _editModeSelector.value = DirectlyEditing ? 1 : 0;
        _editModeSelector.RegisterValueChangedCallback((evt) => SetEditMode(evt.newValue == 1));

        _viewModeSelector = projectToolbar.Q<RadioButtonGroup>("active-view-selector");
        _viewModeSelector.value = (int)_viewMode;
        _viewModeSelector.RegisterValueChangedCallback((evt) => SetViewMode((EditorViewMode)evt.newValue));

        var showBorderToggle = projectToolbar.Q<Toggle>("show-border-toggle");
        showBorderToggle.value = _showBorder;
        showBorderToggle.RegisterValueChangedCallback((evt) =>
        {
            _showBorder = evt.newValue;
            RefreshEditorTexture();
        });

        _viewScaleSlider = projectToolbar.Q<SliderInt>("view-scale-slider");
        _viewScaleSlider.value = _viewScale;
        _viewScaleSlider.RegisterValueChangedCallback((evt) => SetViewScale(evt.newValue));

        var imageToolbar = root.Q<VisualElement>("image-toolbar");
        _toggleInkButton = imageToolbar.Q<Button>("toggle-ink-button");
        _toggleHiresButton = imageToolbar.Q<Button>("toggle-hires-button");
        _toggleLoresButton = imageToolbar.Q<Button>("toggle-lores-button");
        _togglePaperButton = imageToolbar.Q<Button>("toggle-paper-button");
        _toggleErrorsButton = imageToolbar.Q<Button>("toggle-errors-button");
        _toggleInkButton.clicked += () => ToggleLayer(ref _showInkLayer);
        _toggleHiresButton.clicked += () => ToggleLayer(ref _showHiresSpriteLayer);
        _toggleLoresButton.clicked += () => ToggleLayer(ref _showLoresSpriteLayer);
        _togglePaperButton.clicked += () => ToggleLayer(ref _showPaperLayer);
        _toggleErrorsButton.clicked += () => ToggleLayer(ref _showErrors);
        RefreshToggleButtonStyles();

        _penLayersContainer = imageToolbar.Q<VisualElement>("pen-layers");
        _penPaletteContainer = imageToolbar.Q<VisualElement>("pen-palette");
        RefreshPenTools();

        _sourcePixelColor = imageToolbar.Q<VisualElement>("source-pixel");
        _setPenInkButton = imageToolbar.Q<Button>("set-pen-ink-button");
        _setPenPaperButton = imageToolbar.Q<Button>("set-pen-paper-button");
        _setPenSpriteButton = imageToolbar.Q<Button>("set-pen-sprite-button");
        _setPenMulti1Button = imageToolbar.Q<Button>("set-pen-m1-button");
        _setPenMulti2Button = imageToolbar.Q<Button>("set-pen-m2-button");
        _setPenMulti3Button = imageToolbar.Q<Button>("set-pen-m3-button");
        _setPenInkButton.clicked += () => SetPen(Pen.Ink);
        _setPenPaperButton.clicked += () => SetPen(Pen.Paper);
        _setPenSpriteButton.clicked += () => SetPen(Pen.Sprite);
        _setPenMulti1Button.clicked += () => SetPen(Pen.Multi1);
        _setPenMulti2Button.clicked += () => SetPen(Pen.Multi2);
        _setPenMulti3Button.clicked += () => SetPen(Pen.Multi3);
        RefreshPenButtonStyles();

        _penPalette = new List<Label>();
        foreach (var entry in _penPaletteContainer.Children())
        {
            var entryIndex = _penPalette.Count;
            var intensity = (entryIndex >> 3) == 0 ? 0 : 0.5f;
            entry.style.backgroundColor = (Color)_main.Palette.Colors[entryIndex];
            var index = entryIndex;
            entry.RegisterCallback<MouseDownEvent>(evt => SetPenColor(index, evt.button == 0));
            entryIndex++;
            var label = (Label)entry;
            label.text = "";
            _penPalette.Add(label);
        }
        SetPenColor(_penColorPrimary, true);
        SetPenColor(_penColorSecondary, false);
    }

    private void LoadProject()
    {
        var path = _main.OpenFileBrowser(
            "Select Project",
            new ExtensionFilter("All Supported Formats ", ProjectExtension, NufliExtension),
            new ExtensionFilter("NUFLIX Projects ", ProjectExtension),
            new ExtensionFilter("NUFLI Images ", NufliExtension)
        );
        if (path == null)
        {
            return;
        }
        _projectPath = path;
        if (string.IsNullOrEmpty(_projectPath) || !File.Exists(_projectPath))
        {
            return;
        }
        _main.ResetConverter();
        switch (Path.GetExtension(_projectPath).Substring(1))
        {
            case ProjectExtension:
                _main.WorkImage = new LayeredImage(_main.Palette);
                _main.WorkImage.Read(File.ReadAllBytes(_projectPath));
                _main.InputImage = null;
                _main.PreparedPixels = new int[ScreenWidth * ScreenHeight];
                _main.WorkImage.ReferencePixels.CopyTo(_main.PreparedPixels);
                break;
            case NufliExtension:
                _main.WorkImage = _main.NuflixFormat.ImportNufli(File.ReadAllBytes(_projectPath), _main.Palette);
                _main.InputImage = null;
                _main.PreparedPixels = new int[ScreenWidth * ScreenHeight];
                _main.WorkImage.RenderLayers(_main.WorkImage.ReferencePixels);
                _main.WorkImage.ReferencePixels.CopyTo(_main.PreparedPixels);
                _projectPath = $"{_main.GetPathWithoutExtension(_projectPath)}.{ProjectExtension}";
                break;
        }
        _main.RefreshPreparedImage();
        _main.RefreshWorkImage();
    }

    private void SaveProject()
    {
        SaveProject(false);
    }

    private void SaveProjectAs()
    {
        SaveProject(true);
    }

    private void SaveProject(bool saveAs)
    {
        var path = _projectPath;
        if (saveAs || string.IsNullOrEmpty(path))
        {
            path = _main.SaveFileBrowser("Save Project As", DefaultProjectFileName, ProjectExtension);
        }
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        _projectPath = path;
        File.WriteAllBytes(path, _workImage.Write());
    }

    private void ExportProject()
    {
        var path = _projectPath;
        if (string.IsNullOrEmpty(_projectPath))
        {
            path = _main.SaveFileBrowser("Export Project As", DefaultProjectFileName, "prg");
        }
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        var result = _main.NuflixFormat.Export(_workImage, false);
        File.WriteAllBytes($"{_main.GetPathWithoutExtension(path)}.prg", result.Bytes);
    }

    private string DefaultProjectFileName => string.IsNullOrEmpty(_main.InputImagePath) ? "project" : Path.GetFileNameWithoutExtension(_main.InputImagePath);

    public void SetEditMode(bool directlyEditing)
    {
        var needsReferenceUpdate = DirectlyEditing && !directlyEditing;
        DirectlyEditing = directlyEditing;
        var value = DirectlyEditing ? 1 : 0;
        if (_editModeSelector.value != value)
        {
            _editModeSelector.value = value;
        }
        RefreshPenTools();
        if (needsReferenceUpdate)
        {
            _workImage.BackportLayerChangesToReference();
            _workImage.ReferencePixels.CopyTo(_main.PreparedPixels);
            _main.RefreshPreparedImage();
            RefreshWorkImage(true);
        }
    }

    private void SetViewMode(EditorViewMode mode)
    {
        if (_viewMode == mode)
        {
            return;
        }
        _viewMode = mode;
        if (_viewModeSelector.value != (int)mode)
        {
            _viewModeSelector.value = (int)mode;
        }
        RefreshEditorTexture();
        _root.MarkDirtyRepaint();
    }

    private void RefreshPenTools()
    {
        var overBorder = _targetPixelPos.x < 0 || _targetPixelPos.x >= ScreenWidth || _targetPixelPos.y < 0 || _targetPixelPos.y >= ScreenHeight;
        var showPalette = !DirectlyEditing || overBorder;
        _penLayersContainer.style.display = showPalette ? DisplayStyle.None : DisplayStyle.Flex;
        _penPaletteContainer.style.display = showPalette ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void ToggleLayer(ref bool flag)
    {
        flag = !flag;
        RefreshToggleButtonStyles();
        RefreshEditorTexture(true);
    }

    private void RefreshToggleButtonStyles()
    {
        _toggleInkButton.EnableInClassList("activated", _showInkLayer);
        _toggleHiresButton.EnableInClassList("activated", _showHiresSpriteLayer);
        _toggleLoresButton.EnableInClassList("activated", _showLoresSpriteLayer);
        _togglePaperButton.EnableInClassList("activated", _showPaperLayer);
        _toggleErrorsButton.EnableInClassList("activated", _showErrors);
    }

    private void SetPen(Pen pen)
    {
        _activePen = pen;
        RefreshPenButtonStyles();
    }

    private void RefreshPenButtonStyles()
    {
        _setPenInkButton.EnableInClassList("selectedPen", _activePen == Pen.Ink);
        _setPenPaperButton.EnableInClassList("selectedPen", _activePen == Pen.Paper);
        _setPenSpriteButton.EnableInClassList("selectedPen", _activePen == Pen.Sprite);
        _setPenMulti1Button.EnableInClassList("selectedPen", _activePen == Pen.Multi1);
        _setPenMulti2Button.EnableInClassList("selectedPen", _activePen == Pen.Multi2);
        _setPenMulti3Button.EnableInClassList("selectedPen", _activePen == Pen.Multi3);
    }

    private void RefreshPenButtonColors()
    {
        _sourcePixelColor.style.backgroundColor = Color.clear;
        SetPenButtonColor(_setPenInkButton, -1);
        SetPenButtonColor(_setPenPaperButton, -1);
        SetPenButtonColor(_setPenSpriteButton, -1);
        SetPenButtonColor(_setPenMulti1Button, -1);
        SetPenButtonColor(_setPenMulti2Button, -1);
        SetPenButtonColor(_setPenMulti3Button, -1);
        _setPenSpriteButton.text = "S";
        var px = Mathf.FloorToInt(_targetPixelPos.x);
        var py = Mathf.FloorToInt(_targetPixelPos.y);
        if (px < 0 || px >= ScreenWidth || py < 0 || py >= ScreenHeight)
        {
            return;
        }
        _sourcePixelColor.style.backgroundColor = (Color)_main.Palette.Colors[_workImage.ReferencePixels[py * ScreenWidth + px]];
        var attrX = px >> 3;
        var attrY = py >> 1;
        var attr = _workImage.BitmapColors[attrY * AttributeWidth + attrX];
        SetPenButtonColor(_setPenInkButton, (attr >> 4) & 0xf);
        SetPenButtonColor(_setPenPaperButton, attr & 0xf);
        switch (_workImage.GetSection(px, py))
        {
            case ImageSection.Bug:
                if ((attrY & 3) > 0)
                {
                    SetPenButtonColor(_setPenInkButton, -1);
                    SetPenButtonColor(_setPenPaperButton, -1);
                }
                _setPenSpriteButton.text = "H";
                SetPenButtonColor(_setPenSpriteButton, _workImage.BugColors[attrY * BugColorSlots] & 0xf);
                SetPenButtonColor(_setPenMulti1Button, _workImage.BugColors[attrY * BugColorSlots + 1] & 0xf);
                SetPenButtonColor(_setPenMulti2Button, _workImage.BugColors[attrY * BugColorSlots + 2] & 0xf);
                SetPenButtonColor(_setPenMulti3Button, _workImage.BugColors[attrY * BugColorSlots + 3] & 0xf);
                break;
            case ImageSection.Mid:
                var column = (attrX - BugBlockWidth) / UnderlayBlockWidth;
                SetPenButtonColor(_setPenSpriteButton, _workImage.UnderlayColors[(py + (px >= MidEndX - 8 && py < ScreenHeight - 1 ? py & 1 : 0)) * UnderlayColumns + column] & 0xf);
                break;
        }
    }

    private void SetPenButtonColor(Button button, int color)
    {
        if (color < 0)
        {
            button.style.backgroundColor = Color.clear;
            button.style.color = Color.black;
            button.SetEnabled(false);
            return;
        }
        button.style.backgroundColor = (Color)_main.Palette.Colors[color];
        button.style.color = color == 0 ? Color.white : Color.black;
        button.SetEnabled(true);
    }

    private void SetActiveColor(int index, Pen pen)
    {
        if (index < 0)
        {
            return;
        }
        var px = Mathf.FloorToInt(_targetPixelPos.x);
        var py = Mathf.FloorToInt(_targetPixelPos.y);
        var attrX = px >> 3;
        var attrY = py >> 1;
        var section = _workImage.GetSection(px, py);
        switch (section)
        {
            case ImageSection.SideBorder:
            case ImageSection.TopBorder:
            case ImageSection.BottomBorder:
                SetPenColor(index, pen == Pen.Primary);
                return;
        }
        switch (pen)
        {
            case Pen.Ink:
            case Pen.Paper:
                if (section != ImageSection.Bug || (attrY & 3) == 0)
                {
                    var attrI = attrY * AttributeWidth + attrX;
                    var oldAttr = _workImage.BitmapColors[attrI];
                    _workImage.BitmapColors[attrI] = pen == Pen.Ink ? (oldAttr & 0xf) | (index << 4) : (oldAttr & 0xf0) | index;
                    _main.RefreshWorkImage();
                    RefreshPenButtonColors();
                }
                break;
            case Pen.Sprite:
                switch (section)
                {
                    case ImageSection.Bug:
                        _workImage.BugColors[attrY * BugColorSlots] = index;
                        _main.RefreshWorkImage();
                        RefreshPenButtonColors();
                        break;
                    case ImageSection.Mid:
                        var column = (attrX - BugBlockWidth) / UnderlayBlockWidth;
                        _workImage.UnderlayColors[(py + (px >= MidEndX - 8 && py < ScreenHeight - 1 ? py & 1 : 0)) * UnderlayColumns + column] = index;
                        _main.RefreshWorkImage();
                        RefreshPenButtonColors();
                        break;
                }
                break;
            case Pen.Multi1:
                if (section == ImageSection.Bug)
                {
                    var pix = py * BugSprites * SpriteWidth + SpriteWidth + (px & ~1);
                    var mc = (_workImage.Bug[pix] ? 2 : 0) | (_workImage.Bug[pix + 1] ? 1 : 0);
                    if (mc > 0)
                    {
                        _workImage.BugColors[attrY * BugColorSlots + mc] = index;
                        _main.RefreshWorkImage();
                        RefreshPenButtonColors();
                    }
                }
                break;
            case Pen.Primary:
                SetPenColor(index, true);
                break;
            case Pen.Secondary:
                SetPenColor(index, false);
                break;
        }
    }

    private void SetPenColor(int index, bool primary)
    {
        var oldIndex = primary ? _penColorPrimary : _penColorSecondary;
        if (primary)
        {
            _penColorPrimary = index;
        }
        else
        {
            _penColorSecondary = index;
        }
        RefreshPenPaletteEntry(oldIndex);
        RefreshPenPaletteEntry(index);
    }

    private void RefreshPenPaletteEntry(int index)
    {
        var label = _penPalette[index];
        var style = label.style;
        var color = index == _penColorPrimary || index == _penColorSecondary ? Color.white : Color.black;
        label.text = index == _penColorPrimary ? "L" : index == _penColorSecondary ? "R" : "";
        style.borderTopColor = color;
        style.borderRightColor = color;
        style.borderBottomColor = color;
        style.borderLeftColor = color;
    }

    private void OnKeyDown(KeyDownEvent evt)
    {
        if (!Active)
        {
            return;
        }
        var targetPen = DirectlyEditing && _viewMode == EditorViewMode.Layers ? evt.shiftKey ? Pen.Ink : evt.ctrlKey ? Pen.Paper : evt.altKey ? Pen.Multi1 : Pen.Sprite : evt.shiftKey ? Pen.Secondary : Pen.Primary;
        switch (KeyBindings.GetCommand(evt.keyCode, evt.shiftKey, evt.actionKey, evt.altKey))
        {
            case Command.PerformUndo:
                _main.PerformUndoRedo(false);
                break;
            case Command.PerformRedo:
                _main.PerformUndoRedo(true);
                break;
            case Command.SetViewModeSplit:
                SetViewMode(EditorViewMode.Split);
                break;
            case Command.SetViewModeFree:
                SetViewMode(EditorViewMode.Free);
                break;
            case Command.SetViewModeLayers:
                SetViewMode(EditorViewMode.Layers);
                break;
            case Command.SetViewModeResult:
                SetViewMode(EditorViewMode.Result);
                break;
            case Command.ZoomIn:
                if (_viewScale < _viewScaleSlider.highValue)
                {
                    SetViewScale(_viewScale + 1);
                }
                break;
            case Command.ZoomOut:
                if (_viewScale > _viewScaleSlider.lowValue)
                {
                    SetViewScale(_viewScale - 1);
                }
                break;
            case Command.ToggleInkLayer:
                ToggleLayer(ref _showInkLayer);
                break;
            case Command.ToggleHiresSpriteLayer:
                ToggleLayer(ref _showHiresSpriteLayer);
                break;
            case Command.ToggleLoresSpriteLayer:
                ToggleLayer(ref _showLoresSpriteLayer);
                break;
            case Command.TogglePaperLayer:
                ToggleLayer(ref _showPaperLayer);
                break;
            case Command.ToggleErrorComparison:
                ToggleLayer(ref _showErrors);
                break;
            case Command.SelectPenInk:
                SetPen(Pen.Ink);
                break;
            case Command.SelectPenPaper:
                SetPen(Pen.Paper);
                break;
            case Command.SelectPenSprite:
                SetPen(Pen.Sprite);
                break;
            case Command.SelectPenMulti1:
                SetPen(Pen.Multi1);
                break;
            case Command.SelectPenMulti2:
                SetPen(Pen.Multi2);
                break;
            case Command.SelectPenMulti3:
                SetPen(Pen.Multi3);
                break;
            case Command.SetColor00:
                SetActiveColor(0, targetPen);
                break;
            case Command.SetColor01:
                SetActiveColor(1, targetPen);
                break;
            case Command.SetColor02:
                SetActiveColor(2, targetPen);
                break;
            case Command.SetColor03:
                SetActiveColor(3, targetPen);
                break;
            case Command.SetColor04:
                SetActiveColor(4, targetPen);
                break;
            case Command.SetColor05:
                SetActiveColor(5, targetPen);
                break;
            case Command.SetColor06:
                SetActiveColor(6, targetPen);
                break;
            case Command.SetColor07:
                SetActiveColor(7, targetPen);
                break;
            case Command.SetColor08:
                SetActiveColor(8, targetPen);
                break;
            case Command.SetColor09:
                SetActiveColor(9, targetPen);
                break;
            case Command.SetColor10:
                SetActiveColor(10, targetPen);
                break;
            case Command.SetColor11:
                SetActiveColor(11, targetPen);
                break;
            case Command.SetColor12:
                SetActiveColor(12, targetPen);
                break;
            case Command.SetColor13:
                SetActiveColor(13, targetPen);
                break;
            case Command.SetColor14:
                SetActiveColor(14, targetPen);
                break;
            case Command.SetColor15:
                SetActiveColor(15, targetPen);
                break;
        }
        evt.StopPropagation();
    }

    private void OnEditorWheel(WheelEvent evt)
    {
        var delta = (Vector2)evt.delta;
        // Debouncing
        if (delta.x * _lastWheelDelta.x < 0)
        {
            delta.x = 0;
        }
        if (delta.y * _lastWheelDelta.y < 0)
        {
            delta.y = 0;
        }
        _lastWheelDelta = evt.delta;
        if (evt.ctrlKey)
        {
            SetViewScale(clamp(_viewScale - (int)sign(delta.y), _viewScaleSlider.lowValue, _viewScaleSlider.highValue));
            return;
        }
        _viewPos += delta * 16;
        RefreshPixelPosition(evt.localMousePosition);
        RefreshEditorTexture();
        _imageEditor.MarkDirtyRepaint();
    }

    private void OnEditorMouseDown(MouseDownEvent evt)
    {
        _main.MakeUndoCheckpoint();
        RefreshPixelPosition(evt.localMousePosition);
        var leftPressed = (evt.pressedButtons & 1) != 0;
        var rightPressed = (evt.pressedButtons & 2) != 0;
        var midPressed = (evt.pressedButtons & 4) != 0;
        if (leftPressed || rightPressed)
        {
            if (evt.ctrlKey)
            {
                Pick(leftPressed ? Pen.Primary : Pen.Secondary);
            }
            else
            {
                Plot(leftPressed, evt.shiftKey, true);
            }
        }
        if (midPressed)
        {
            _dragStartViewPos = _viewPos;
            _dragStartPixelPos = evt.localMousePosition;
        }
    }

    private void OnEditorMouseMove(MouseMoveEvent evt)
    {
        if (_workImage?.Valid != true)
        {
            return;
        }
        var midPressed = (evt.pressedButtons & 4) != 0;
        if (midPressed)
        {
            _viewPos = _dragStartViewPos + (_dragStartPixelPos - evt.localMousePosition);
            RefreshEditorTexture();
            RefreshPixelPosition(evt.localMousePosition);
            _imageEditor.MarkDirtyRepaint();
            return;
        }
        var movedPixel = RefreshPixelPosition(evt.localMousePosition);
        var leftPressed = (evt.pressedButtons & 1) != 0;
        var rightPressed = (evt.pressedButtons & 2) != 0;
        if (leftPressed || rightPressed)
        {
            Plot(leftPressed, evt.shiftKey, false);
        }
        if (movedPixel)
        {
            RefreshEditorTexture();
            RefreshPenButtonColors();
            RefreshPenTools();
            _imageEditor.MarkDirtyRepaint();
        }
    }

    private void OnEditorMouseUp(MouseUpEvent evt)
    {
        RefreshPixelPosition(evt.localMousePosition);
        RefreshEditorTexture();
        _imageEditor.MarkDirtyRepaint();
    }

    private void OnEditorMouseLeave(MouseLeaveEvent evt)
    {
        if (_targetPixelPos.x < 0)
        {
            return;
        }
        _targetPixelPos = new Vector2(-1, -1);
        RefreshEditorTexture();
        _imageEditor.MarkDirtyRepaint();
    }

    private bool RefreshPixelPosition(Vector2 pos)
    {
        _sourcePixelPos = pos;
        var scale = ViewScale;
        var split = _viewMode == EditorViewMode.Split;
        var xmin = ((split ? 0 : _assets.LeftHeader.width) + _assets.LeftRuler.width) * scale;
        var ymin = _assets.LeftHeader.height * scale;
        var xmax = split ? _editorTexture.width / 2 : _editorTexture.width - RightHeaderWidth;
        if (pos.y < ymin || pos.x < xmin || pos.x >= xmax)
        {
            var result = _targetPixelPos.x >= 0;
            _targetPixelPos = new Vector2(-1, -1);
            return result;
        }
        var oldX = Mathf.FloorToInt(_targetPixelPos.x);
        var oldY = Mathf.FloorToInt(_targetPixelPos.y);
        _targetPixelPos = (pos - new Vector2(xmin, ymin) + _viewPos) / scale / 8;
        return Mathf.FloorToInt(_targetPixelPos.x) != oldX || Mathf.FloorToInt(_targetPixelPos.y) != oldY;
    }

    private void Pick(Pen pen)
    {
        var px = Mathf.FloorToInt(_targetPixelPos.x);
        var py = Mathf.FloorToInt(_targetPixelPos.y);
        var section = _workImage.GetSection(px, py);
        switch (section)
        {
            case ImageSection.SideBorder:
            case ImageSection.TopBorder:
            case ImageSection.BottomBorder:
                return;
        }
        switch (_viewMode)
        {
            case EditorViewMode.Split:
            case EditorViewMode.Free:
                SetActiveColor(_workImage.ReferencePixels[py * ScreenWidth + px], pen);
                break;
            case EditorViewMode.Layers:
                SetActiveColor(_workImage.GetPixel(px, py, _showInkLayer, _showLoresSpriteLayer, _showHiresSpriteLayer, _showPaperLayer), pen);
                break;
            case EditorViewMode.Result:
                if (_exportedImage != null)
                {
                    SetActiveColor(_exportedImage.GetPixel(px, py, true, true, true, true), pen);
                }
                break;
        }
    }

    private void Plot(bool leftPressed, bool shiftPressed, bool forced)
    {
        var px = Mathf.FloorToInt(_targetPixelPos.x);
        var py = Mathf.FloorToInt(_targetPixelPos.y);
        var section = _workImage.GetSection(px, py);
        var penColor = leftPressed ? _penColorPrimary : _penColorSecondary;
        switch (section)
        {
            case ImageSection.SideBorder:
            case ImageSection.TopBorder:
            case ImageSection.BottomBorder:
                _workImage.SetBorderColor(px, py, penColor, shiftPressed);
                _main.RefreshWorkImage();
                return;
        }
        if (!forced)
        {
            // When dragging, we ignore the corners of the pixels to make it easier to draw diagonals
            var midVector = frac(_targetPixelPos) - 0.5f;
            if (ViewScale >= 1 && dot(midVector, midVector) > 0.2f)
            {
                return;
            }
        }
        if (!DirectlyEditing)
        {
            var pixels = _main.PreparedPixels;
            var pi = py * ScreenWidth + px;
            if (pixels[pi] == penColor)
            {
                return;
            }
            _workImage.ReferencePixels[pi] = pixels[pi] = penColor;
            _main.RefreshPreparedImage();
            _workImage.ReoptimiseForPixel(px, py);
            _main.GenerateLayers();
            return;
        }
        if (_viewMode != EditorViewMode.Layers)
        {
            return;
        }
        if (_activePen == Pen.Paper)
        {
            if (_showInkLayer)
            {
                _workImage.SetPixel(Pen.Ink, px, py, false);
            }
            if ((section == ImageSection.Bug && _showHiresSpriteLayer) || (section == ImageSection.Mid && _showLoresSpriteLayer))
            {
                _workImage.SetPixel(Pen.Sprite, px, py, false);
            }
            if (_showLoresSpriteLayer)
            {
                _workImage.SetPixel(Pen.Multi1, px, py, false);
            }
        }
        else
        {
            switch (_activePen)
            {
                case Pen.Ink:
                    if (!_showInkLayer)
                    {
                        return;
                    }
                    break;
                case Pen.Sprite:
                    if ((section == ImageSection.Bug && !_showHiresSpriteLayer) || (section == ImageSection.Mid && !_showLoresSpriteLayer))
                    {
                        return;
                    }
                    break;
                case Pen.Multi1:
                case Pen.Multi2:
                case Pen.Multi3:
                    if (!_showLoresSpriteLayer)
                    {
                        return;
                    }
                    break;
            }
            _workImage.SetPixel(_activePen, px, py, leftPressed);
        }
        _main.RefreshWorkImage();
    }

    public void OnUpdate()
    {
        if (!Active)
        {
            return;
        }
        var px = Mathf.FloorToInt(_targetPixelPos.x);
        var py = Mathf.FloorToInt(_targetPixelPos.y);
        var showHighlight = px >= 0 && py >= 0 && px < ScreenWidth && py < ScreenHeight;
        if (showHighlight && AnimateHighlightPattern())
        {
            RefreshEditorTexture();
#if UNITY_EDITOR
            _imageEditor.MarkDirtyRepaint();
#endif
        }
        if (_workImage == _main.WorkImage)
        {
            return;
        }
        _workImage = _main.WorkImage;
        _workImageDirty = true;
        _editModeSelector.value = DirectlyEditing ? 1 : 0;
        RefreshPenTools();
    }

    private bool AnimateHighlightPattern()
    {
        var offset = Mathf.FloorToInt((float)(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond) / TimeSpan.TicksPerSecond * 8);
        if (_highlightMaterial.GetFloat("_PatternOffset") == offset)
        {
            return false;
        }
        _highlightMaterial.SetFloat("_PatternOffset", offset);
        return true;
    }

    private void RefreshWorkImage(bool force = false)
    {
        _workImage ??= _main.WorkImage;
        if (_workImage?.Valid != true || (!force && (!_workImageDirty || _viewMode == EditorViewMode.Split)))
        {
            return;
        }
        _workImageDirty = false;
        Profiler.BeginSample("RenderBugColors");
        _workImage.RenderBugColors(ref BugColors, true);
        Profiler.EndSample();
        Profiler.BeginSample("RenderMidColors");
        _workImage.RenderMidColors(ref UnderlayColors, true);
        Profiler.EndSample();
        Profiler.BeginSample("RenderBorderColors");
        _workImage.RenderBorderColors(ref BorderColors);
        Profiler.EndSample();
        Profiler.BeginSample("RenderInkLayer");
        _workImage.Render(ref InkLayer, true, false, false, false);
        Profiler.EndSample();
        Profiler.BeginSample("RenderLoresSpriteLayer");
        _workImage.Render(ref LoresSpriteLayer, false, true, false, false);
        Profiler.EndSample();
        Profiler.BeginSample("RenderHiresSpriteLayer");
        _workImage.Render(ref HiresSpriteLayer, false, false, true, false);
        Profiler.EndSample();
        Profiler.BeginSample("RenderPaperLayer");
        _workImage.Render(ref PaperLayer, false, false, false, true);
        Profiler.EndSample();
        var exportedImage = _main.NuflixFormat?.Image?.Clone();
        if (exportedImage != null)
        {
            _exportedImage = exportedImage;
        }
        Profiler.BeginSample("RenderExportedBugColors");
        _exportedImage.RenderBugColors(ref ExportedBugColors, true);
        Profiler.EndSample();
        Profiler.BeginSample("RenderExportedMidColors");
        _exportedImage.RenderMidColors(ref ExportedUnderlayColors, true);
        Profiler.EndSample();
        Profiler.BeginSample("RefreshCyclesHistogram");
        RefreshCyclesHistogram();
        Profiler.EndSample();
        Profiler.BeginSample("RefreshErrorMap");
        RefreshErrorMap();
        Profiler.EndSample();
    }

    private void RefreshCyclesHistogram()
    {
        const int histogramWidth = 48;
        if (CyclesHistogram == null)
        {
            CyclesHistogram = new Texture2D(histogramWidth, AttributeHeight, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Point
            };
        }
        var pixels = new Color32[histogramWidth * AttributeHeight];
        for (var y = 0; y < AttributeHeight; y++)
        {
            for (var x = 0; x < histogramWidth; x++)
            {
                var c = new Color32(0, 0, 0, 255);
                if (x >= 1 && x < 47)
                {
                    var i = (byte)(((x - 1) & 4) != 0 ? 255 : 223);
                    var fc = _main.FreeCycles[y];
                    if (fc < 0)
                    {
                        c = new Color32(i, 0, 0, 255);
                    }
                    else if (x < 47 - (fc & 0xff))
                    {
                        c = fc < 0x100 ? new Color32(0, i, 0, 255) : new Color32(i, (byte)((i * 5) >> 3), 0, 255);
                    }
                }
                pixels[(AttributeHeight - 1 - y) * histogramWidth + x] = c;
            }
        }
        CyclesHistogram.SetPixels32(pixels);
        CyclesHistogram.Apply();
    }

    private void RefreshErrorMap()
    {
        if (!_showErrors || _exportedImage == null)
        {
            return;
        }

        if (ErrorMap == null)
        {
            ErrorMap = new Texture2D(ScreenWidth, ScreenHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
        }

        var workIndices = new NativeArray<int>(ScreenWidth * ScreenHeight, Allocator.TempJob);
        var resultIndices = new NativeArray<int>(ScreenWidth * ScreenHeight, Allocator.TempJob);
        _workImage.RenderLayers(workIndices);
        _exportedImage.RenderLayers(resultIndices);
        var errorMap = new Color32[workIndices.Length];
        var i = 0;
        for (var y = ScreenHeight - 1; y >= 0; y--)
        {
            for (var x = 0; x < ScreenWidth; x++)
            {
                var pi = y * ScreenWidth + x;
                errorMap[i++] = workIndices[pi] == resultIndices[pi] ? new Color32(0x00, 0x00, 0x00, 0xbf) : new Color32(0xff, 0xff, 0xff, 0xff);
            }
        }
        ErrorMap.SetPixels32(errorMap);
        ErrorMap.Apply();
        workIndices.Dispose();
        resultIndices.Dispose();
    }

    public void OnEditorGeometryChanged(GeometryChangedEvent evt)
    {
        var scale = Screen.dpi / 96;
        var width = Mathf.RoundToInt(_imageEditor.resolvedStyle.width * scale);
        var height = Mathf.RoundToInt(_imageEditor.resolvedStyle.height * scale);
        if (_editorTexture != null)
        {
            _editorTexture.Release();
            _editorTexture = null;
        }
        if (width <= 0 || height <= 0)
        {
            return;
        }
        _editorTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        _imageEditor.style.backgroundImage = Background.FromRenderTexture(_editorTexture);
        RefreshEditorTexture();
    }

    private void SetViewScale(int newScale)
    {
        var oldScale = ViewScale;
        if (_viewScale == newScale)
        {
            return;
        }
        _viewScale = newScale;
        _viewPos = (_viewPos + _sourcePixelPos) * ViewScale / oldScale - _sourcePixelPos;
        _viewScaleSlider.value = newScale;
        RefreshPixelPosition(_sourcePixelPos);
        RefreshEditorTexture();
    }

    public void RefreshEditorTexture(bool forceWorkImageUpdate = false)
    {
        _workImageDirty |= forceWorkImageUpdate;
        if (!Active)
        {
            return;
        }
        if (_editorTexture == null)
        {
            Profiler.BeginSample("CreateEditorTexture");
            OnEditorGeometryChanged(null);
            Profiler.EndSample();
            if (_editorTexture == null)
            {
                return;
            }
        }
        Profiler.BeginSample("RefreshWorkImage");
        RefreshWorkImage(forceWorkImageUpdate);
        Profiler.EndSample();
        if (_workImage?.Valid != true)
        {
            return;
        }
        RenderTexture.active = _editorTexture;
        GL.Clear(true, true, Color.clear);
        Profiler.BeginSample("DrawEditor");
        if (_viewMode == EditorViewMode.Split)
        {
            DrawSplitEditor();
        }
        else
        {
            DrawSingleEditor();
        }
        Profiler.EndSample();
        RenderTexture.active = null;
    }

    private void DrawSingleEditor()
    {
        var borderWidth = _showBorder ? 16 : 0;
        var scale = ViewScale;
        var pscale = scale * 8;
        var headerHeight = _assets.TopRuler.height * scale;
        var cyclesX = _editorTexture.width - RightHeaderWidth;
        var bottomY = _editorTexture.height - headerHeight;
        var clipRect = Rect.MinMaxRect((_assets.LeftHeader.width + _assets.LeftRuler.width) * scale, 0, cyclesX - _assets.RightRuler.width * scale, bottomY);
        _viewPos = Vector2.Min(_viewPos, new Vector2((ScreenWidth + borderWidth) * pscale - clipRect.width, (ScreenHeight + borderWidth) * pscale - clipRect.height));
        _viewPos = Vector2.Max(_viewPos, new Vector2(-borderWidth * pscale, -borderWidth * pscale));
        var viewPos = new Vector2(Mathf.Round(_viewPos.x), Mathf.Round(_viewPos.y));
        var viewPosH = Vector2.right * viewPos.x;
        var viewPosV = Vector2.up * viewPos.y;
        Blit(Texture2D.whiteTexture, null, new Rect(0, 0, _editorTexture.width, headerHeight), new Rect(0, 0, 1, 1));
        Blit(Texture2D.whiteTexture, null, new Rect(_assets.LeftHeader.width * scale, 0, _assets.LeftRuler.width * scale, _editorTexture.height), new Rect(0, 0, 1, 1));
        Blit(Texture2D.whiteTexture, null, new Rect(clipRect.xMax, 0, _assets.RightRuler.width * scale, _editorTexture.height), new Rect(0, 0, 1, 1));
        Blit(Texture2D.whiteTexture, null, new Rect(0, bottomY, _editorTexture.width, headerHeight), new Rect(0, 0, 1, 1));
        var bottomRulerClipRect = clipRect;
        bottomRulerClipRect.yMax = _editorTexture.height;
        var rightRulerClipRect = clipRect;
        rightRulerClipRect.min = new Vector2(_assets.LeftHeader.width, _assets.LeftHeader.height) * scale;
        rightRulerClipRect.xMax = cyclesX;
        if (scale >= 1)
        {
            Blit(_assets.LeftHeader, null, new Vector2(0, 0), scale);
            Blit(_assets.TopRuler, null, new Vector2((_assets.LeftHeader.width + _assets.LeftRuler.width) * scale, 0) - viewPosH, scale, clipRect);
            Blit(_assets.BottomRuler, null, new Vector2((_assets.LeftHeader.width + _assets.LeftRuler.width) * scale, bottomY) - viewPosH, scale, bottomRulerClipRect);
            clipRect.min = new Vector2(_assets.LeftHeader.width, _assets.LeftHeader.height) * scale;
            Blit(_assets.LeftRuler, null, new Vector2(_assets.LeftHeader.width, _assets.LeftHeader.height) * scale - viewPosV, scale, clipRect);
            Blit(_assets.RightRuler, null, new Vector2(clipRect.xMax, _assets.LeftHeader.height * scale) - viewPosV, scale, rightRulerClipRect);
        }
        else
        {
            Blit(_assets.LeftHeaderSmall, null, new Vector2(0, 0), scale);
            Blit(_assets.HorizontalRulerSmall, null, new Vector2((_assets.LeftHeader.width + _assets.LeftRuler.width) * scale, 0) - viewPosH, pscale, clipRect);
            Blit(_assets.HorizontalRulerSmall, null, new Vector2((_assets.LeftHeader.width + _assets.LeftRuler.width) * scale, bottomY) - viewPosH, pscale, bottomRulerClipRect);
            clipRect.min = new Vector2(_assets.LeftHeader.width, _assets.LeftHeader.height) * scale;
            Blit(_assets.VerticalRulerSmall, null, new Vector2(_assets.LeftHeader.width, _assets.LeftHeader.height) * scale - viewPosV, scale, clipRect);
            Blit(_assets.VerticalRulerSmall, null, new Vector2(clipRect.xMax, _assets.LeftHeader.height * scale) - viewPosV, scale, rightRulerClipRect);
        }
        clipRect.xMin = 0;
        Blit(_assets.RightHeader, null, new Vector2(cyclesX, 0), new Vector2(RightHeaderWidth / _assets.RightHeader.width, headerHeight / _assets.RightHeader.height));
        Blit(_viewMode == EditorViewMode.Result ? ExportedBugColors : BugColors, _assets.BugColorOverlay, new Vector2(0, _assets.TopRuler.height) * scale - viewPosV, pscale, clipRect);
        Blit(_viewMode == EditorViewMode.Result ? ExportedUnderlayColors : UnderlayColors, _assets.UnderlayColorOverlay, new Vector2(32, _assets.TopRuler.height) * scale - viewPosV, pscale, clipRect);
        var rightClipRect = new Rect(cyclesX, headerHeight, RightHeaderWidth, _editorTexture.height - headerHeight);
        Blit(CyclesHistogram, null, new Vector2(cyclesX, headerHeight) - viewPosV, new Vector2(RightHeaderWidth / CyclesHistogram.width, pscale * 2), rightClipRect);
        clipRect.xMin = (_assets.LeftHeader.width + _assets.LeftRuler.width) * scale;
        var pos = new Vector2(_assets.LeftHeader.width + _assets.LeftRuler.width, _assets.TopRuler.height) * scale - viewPos;
        if (_showBorder)
        {
            SetBlitTint(_workImage.TopBackgroundColor);
            Blit(Texture2D.whiteTexture, null, pos - borderWidth * pscale * Vector2.up, new Vector2(ScreenWidth * pscale / Texture2D.whiteTexture.width, borderWidth * pscale / Texture2D.whiteTexture.height), clipRect);
            SetBlitTint(_workImage.BottomBackgroundColor);
            Blit(Texture2D.whiteTexture, null, pos - ScreenHeight * pscale * Vector2.down, new Vector2(ScreenWidth * pscale / Texture2D.whiteTexture.width, borderWidth * pscale / Texture2D.whiteTexture.height), clipRect);
            SetBlitTint(_workImage.BorderColors[0]);
            Blit(Texture2D.whiteTexture, null, pos - borderWidth * pscale * Vector2.one, new Vector2(borderWidth * pscale / Texture2D.whiteTexture.width, borderWidth * pscale / Texture2D.whiteTexture.height), clipRect);
            Blit(Texture2D.whiteTexture, null, pos + new Vector2(ScreenWidth, -borderWidth) * pscale, new Vector2(borderWidth * pscale / Texture2D.whiteTexture.width, borderWidth * pscale / Texture2D.whiteTexture.height), clipRect);
            SetBlitTint(_workImage.BorderColors[AttributeHeight]);
            Blit(Texture2D.whiteTexture, null, pos + new Vector2(-borderWidth, ScreenHeight) * pscale, new Vector2(borderWidth * pscale / Texture2D.whiteTexture.width, borderWidth * pscale / Texture2D.whiteTexture.height), clipRect);
            Blit(Texture2D.whiteTexture, null, pos + new Vector2(ScreenWidth, ScreenHeight) * pscale, new Vector2(borderWidth * pscale / Texture2D.whiteTexture.width, borderWidth * pscale / Texture2D.whiteTexture.height), clipRect);
            SetBlitTint(Color.white);
            Blit(BorderColors, null, pos + borderWidth * pscale * Vector2.left, new Vector2(borderWidth * pscale, 2 * pscale), clipRect);
            Blit(BorderColors, null, pos + new Vector2(ScreenWidth, -1) * pscale, new Vector2(borderWidth * pscale, 2 * pscale), clipRect);
        }
        switch (_viewMode)
        {
            case EditorViewMode.Free:
                Blit(_main.PreparedImage, null, pos, pscale, clipRect);
                break;
            case EditorViewMode.Layers:
                if (_showPaperLayer) Blit(PaperLayer, null, pos, pscale, clipRect);
                if (_showLoresSpriteLayer)
                {
                    var bugEdgeX = (_assets.LeftHeader.width + _assets.LeftRuler.width + SpriteWidth * 8) * scale - viewPos.x;
                    if (bugEdgeX > clipRect.xMin)
                    {
                        var xMax = clipRect.xMax;
                        var loresClipRect = clipRect;
                        loresClipRect.xMax = min(bugEdgeX, xMax);
                        Blit(LoresSpriteLayer, _assets.MultiPixelOverlay, pos, pscale, loresClipRect);
                        loresClipRect.xMax = xMax;
                        loresClipRect.xMin = bugEdgeX;
                        Blit(LoresSpriteLayer, _assets.SpritePixelOverlay, pos, pscale, loresClipRect);
                    }
                    else
                    {
                        Blit(LoresSpriteLayer, _assets.SpritePixelOverlay, pos, pscale, clipRect);
                    }
                }
                if (_showHiresSpriteLayer) Blit(HiresSpriteLayer, _assets.HiresPixelOverlay, pos, pscale, clipRect);
                if (_showInkLayer) Blit(InkLayer, _assets.InkPixelOverlay, pos, pscale, clipRect);
                if (_showErrors)
                {
                    BlitHighlight(ErrorMap, pos, pscale * Vector2.one, clipRect);
                }
                var px = Mathf.FloorToInt(_targetPixelPos.x);
                var py = Mathf.FloorToInt(_targetPixelPos.y);
                if (px < 0 || py < 0 || px >= ScreenWidth || py >= ScreenHeight)
                {
                    return;
                }
                var attrX = px & ~7;
                var attrY = py & ~1;
                const int attrW = 8;
                const int attrH = 2;
                if (px < MidEndX)
                {
                    var sprX = (px - SpriteWidth) / (SpriteWidth << 1) * (SpriteWidth << 1) + SpriteWidth;
                    var sprY = py + (px >= MidEndX - 8 && py != 0 && py != ScreenHeight - 1 ? py & 1 : 0);
                    var sprW = 48;
                    var sprH = 1;
                    if (px < MidStartX)
                    {
                        sprX = 0;
                        sprY = attrY;
                        sprW = SpriteWidth;
                        sprH = attrH;
                    }
                    SetBlitTint(Color.white);
                    var fullColumn = sprX < MidEndX - (SpriteWidth << 1) || sprY == 0 || sprY == ScreenHeight - 1;
                    if (fullColumn || (sprY & 1) != 0)
                    {
                        if (!fullColumn)
                        {
                            sprW -= attrW;
                        }
                        HighlightRectangle(clipRect, pos, pscale, sprX, sprY, sprW, sprH);
                    }
                    else
                    {
                        // Special case: the last 8 pixels of the last column have to take the next row's colour on odd rows due to timing limitations
                        HighlightRectangle(clipRect, pos, pscale, sprX, sprY - 1, sprW, attrH, 0b0101);
                        HighlightRectangle(clipRect, pos, pscale, sprX, sprY, sprW - attrW, sprH, 0b1010);
                        HighlightRectangle(clipRect, pos, pscale, sprX + sprW - attrW, sprY - 1, attrW, sprH, 0b1010);
                    }
                }
                SetBlitTint(Color.cyan);
                HighlightRectangle(clipRect, pos, pscale, attrX, attrY, attrW, attrH);
                SetBlitTint(Color.white);
                break;
            case EditorViewMode.Result:
                Blit(_main.ResultImage, null, pos, pscale, clipRect);
                if (_showErrors)
                {
                    BlitHighlight(ErrorMap, pos, pscale * Vector2.one, clipRect);
                }
                break;
        }
    }

    private void DrawSplitEditor()
    {
        var scale = ViewScale;
        var pscale = scale * 8;
        var splitWidth = _editorTexture.width / 2;
        var rulerWidth = _assets.LeftRuler.width * scale;
        var rulerHeight = _assets.TopRuler.height * scale;
        var clipRect = Rect.MinMaxRect(rulerWidth, 0, splitWidth, _editorTexture.height);
        _viewPos = Vector2.Min(_viewPos, new Vector2(ScreenWidth * pscale - clipRect.width, ScreenHeight * pscale - clipRect.height + rulerHeight));
        _viewPos = Vector2.Max(_viewPos, Vector2.zero);
        var viewPos = new Vector2(Mathf.Round(_viewPos.x), Mathf.Round(_viewPos.y));
        var viewPosH = Vector2.right * viewPos.x;
        var viewPosV = Vector2.up * viewPos.y;
        Blit(Texture2D.whiteTexture, null, new Rect(0, 0, rulerWidth, rulerHeight), new Rect(0, 0, 1, 1));
        Blit(Texture2D.whiteTexture, null, new Rect(splitWidth, 0, rulerWidth, rulerHeight), new Rect(0, 0, 1, 1));
        var smallScale = scale < 1;
        var rulerScale = smallScale ? pscale : scale;
        Blit(smallScale ? _assets.HorizontalRulerSmall : _assets.TopRuler, null, new Vector2(_assets.LeftRuler.width, 0) * scale - viewPosH, rulerScale, clipRect);
        clipRect.x += splitWidth;
        Blit(smallScale ? _assets.HorizontalRulerSmall : _assets.TopRuler, null, new Vector2(_assets.LeftRuler.width + splitWidth / scale, 0) * scale - viewPosH, rulerScale, clipRect);
        clipRect = Rect.MinMaxRect(0, rulerHeight, splitWidth, _editorTexture.height);
        Blit(smallScale ? _assets.VerticalRulerSmall : _assets.LeftRuler, null, new Vector2(0, _assets.TopRuler.height) * scale - viewPosV, scale, clipRect);
        clipRect.x += splitWidth;
        Blit(smallScale ? _assets.VerticalRulerSmall : _assets.LeftRuler, null, new Vector2(splitWidth / scale, _assets.TopRuler.height) * scale - viewPosV, scale, clipRect);
        clipRect = Rect.MinMaxRect(rulerWidth, rulerHeight, splitWidth, _editorTexture.height);
        var pos = new Vector2(rulerWidth, rulerHeight) - viewPos;
        Blit(_main.PreparedImage, null, pos, pscale, clipRect);
        clipRect.x += splitWidth;
        pos.x += splitWidth;
        Blit(_main.ResultImage, null, pos, pscale, clipRect);
        HighlightRectangle(clipRect, pos, pscale, Mathf.FloorToInt(_targetPixelPos.x), Mathf.FloorToInt(_targetPixelPos.y), 1, 1);
    }

    private void HighlightRectangle(Rect clipRect, Vector2 offset, float pixelScale, int x, int y, int w, int h, int sides = 0b1111)
    {
        // Sides bits in order: left, right, top, bottom
        if ((sides & 0b1000) != 0) BlitHighlight(offset + new Vector2(x, y) * pixelScale + new Vector2(-1, -1), new Vector2(1, pixelScale * h + 2), clipRect);
        if ((sides & 0b0100) != 0) BlitHighlight(offset + new Vector2(x + w, y) * pixelScale + new Vector2(0, -1), new Vector2(1, pixelScale * h + 2), clipRect);
        if ((sides & 0b0010) != 0) BlitHighlight(offset + new Vector2(x, y) * pixelScale + new Vector2(-1, -1), new Vector2(pixelScale * w + 2, 1), clipRect);
        if ((sides & 0b0001) != 0) BlitHighlight(offset + new Vector2(x, y + h) * pixelScale + new Vector2(-1, 0), new Vector2(pixelScale * w + 2, 1), clipRect);
    }

    private void BlitHighlight(Vector2 pos, Vector2 scale, Rect clipRect)
    {
        Blit(Texture2D.whiteTexture, null, pos, scale / Texture2D.whiteTexture.width, clipRect, true);
    }

    private void BlitHighlight(Texture2D image, Vector2 pos, Vector2 scale, Rect clipRect)
    {
        Blit(image, null, pos, scale, clipRect, true);
    }

    private void Blit(Texture2D image, Texture2D overlayImage, Vector2 pos, float scale)
    {
        Blit(image, overlayImage, pos, Vector2.one * scale);
    }

    private void Blit(Texture2D image, Texture2D overlayImage, Vector2 pos, float scale, Rect clipRect)
    {
        Blit(image, overlayImage, pos, Vector2.one * scale, clipRect);
    }

    private void Blit(Texture2D image, Texture2D overlayImage, Vector2 pos, Vector2 scale)
    {
        Blit(image, overlayImage, new Rect(pos.x, pos.y, image.width * scale.x, image.height * scale.y), new Rect(0, 0, image.width, image.height));
    }

    private void Blit(Texture2D image, Texture2D overlayImage, Vector2 pos, Vector2 scale, Rect clipRect, bool isHighlight = false)
    {
        if (image == null)
        {
            return;
        }
        var fullSourceRect = new Rect(0, 0, image.width, image.height);
        var fullTargetRect = new Rect(pos.x, pos.y, image.width * scale.x, image.height * scale.y);
        var targetMin = Vector2.Max(fullTargetRect.min, clipRect.min);
        var targetMax = Vector2.Min(fullTargetRect.max, clipRect.max);
        if (targetMin.x >= targetMax.x || targetMin.y >= targetMax.y)
        {
            return;
        }
        var targetRect = Rect.MinMaxRect(targetMin.x, targetMin.y, targetMax.x, targetMax.y);
        var sourceMin = Vector2.Scale(Rect.PointToNormalized(fullTargetRect, targetRect.min), fullSourceRect.size);
        var sourceMax = Vector2.Scale(Rect.PointToNormalized(fullTargetRect, targetRect.max), fullSourceRect.size);
        var sourceRect = Rect.MinMaxRect(sourceMin.x, sourceMin.y, sourceMax.x, sourceMax.y);
        Blit(image, overlayImage, targetRect, sourceRect, isHighlight);
    }

    private void Blit(Texture2D image, Texture2D overlayImage, Rect targetRect, Rect sourceRect, bool isHighlight = false)
    {
        if (image == null)
        {
            return;
        }
        var mat = overlayImage == null ? isHighlight ? _highlightMaterial : _simpleImageMaterial : _gridImageMaterial;
        mat.SetVector("_SourceRect", new Vector4(sourceRect.x / image.width, (image.height - sourceRect.y - sourceRect.height) / image.height, sourceRect.width / image.width, sourceRect.height / image.height));
        mat.SetVector("_TargetRect", new Vector4(targetRect.x / _editorTexture.width, targetRect.y / _editorTexture.height, targetRect.width / _editorTexture.width, targetRect.height / _editorTexture.height));
        if (overlayImage != null)
        {
            mat.SetTexture("_OverlayTex", overlayImage);
            var overlayScale = overlayImage.width >= overlayImage.height
                ? new Vector4((float)image.width * overlayImage.height / overlayImage.width, image.height)
                : new Vector4(image.width, (float)image.height * overlayImage.width / overlayImage.height);
            mat.SetVector("_OverlayScale", overlayScale);
        }
        Graphics.Blit(image, _editorTexture, mat);
    }

    private void SetBlitTint(int colorIndex)
    {
        SetBlitTint(((Color)_main.Palette.Colors[colorIndex]).linear);
    }

    private void SetBlitTint(Color color)
    {
        _simpleImageMaterial.SetColor("_Tint", color);
        _highlightMaterial.SetColor("_Tint", color);
    }
}

[Serializable]
public class EditorPaneAssets
{
    [Header("Image Elements")]
    public Shader SimpleImageShader;
    public Shader GridImageShader;
    public Shader HighlightShader;
    public Texture2D InkPixelOverlay;
    public Texture2D SpritePixelOverlay;
    public Texture2D HiresPixelOverlay;
    public Texture2D MultiPixelOverlay;
    public Texture2D BugColorOverlay;
    public Texture2D UnderlayColorOverlay;

    [Header("Control Elements")]
    public Texture2D TopRuler;
    public Texture2D BottomRuler;
    public Texture2D HorizontalRulerSmall;
    public Texture2D LeftRuler;
    public Texture2D RightRuler;
    public Texture2D VerticalRulerSmall;
    public Texture2D LeftHeader;
    public Texture2D LeftHeaderSmall;
    public Texture2D RightHeader;
}

public enum EditorViewMode
{
    Split, Free, Layers, Result
}

public enum Layer
{
    Ink, HiresSprite, LoresSprite, Paper
}

public enum Pen
{
    Primary, Secondary, Ink, Paper, Sprite, Multi1, Multi2, Multi3
}