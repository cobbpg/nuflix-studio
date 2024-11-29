using SFB;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using static Constants;
using static Unity.Mathematics.math;

[Serializable]
public class ConverterPane
{
    public const string TabName = "converter-tab";
    public const string ProfileExtension = "nfxprof";

    private MainWindowLogic _main;
    private VisualElement _root;
    private VisualTreeAsset _paletteMappingEntryTemplate;
    public bool Active => _main.MainTabView.activeTab.name == TabName;

    private ConversionProfile _conversionProfile = new();
    private ImagePreparation _imagePreparation = new();

    private Label _inputImageNameLabel;

    private VisualElement _cropSettingsContainer;
    private SliderInt _cropXSlider;
    private SliderInt _cropYSlider;

    private RadioButtonGroup _paletteMappingModeSelector;

    private VisualElement _automaticPaletteMapper;
    private SliderInt _brightnessSlider;
    private SliderInt _contrastSlider;
    private SliderInt _saturationSlider;

    private VisualElement _manualPaletteMapper;
    private List<VisualElement> _paletteMappingTargets;
    private VisualElement _palettePicker;
    private VisualElement _paletteContainer;
    private int _selectedMappingEntryIndex;

    private VisualElement _imageViews;

    private string _conversionProfilePath;
    private string _inputImagePath;
    public string InputImagePath => _inputImagePath;

    private bool _showInkLayer = true;
    private bool _showSpriteLayer = true;
    private bool _showPaperLayer = true;

    public bool ShowInkLayer => _showInkLayer;
    public bool ShowSpriteLayer => _showSpriteLayer;
    public bool ShowPaperLayer => _showPaperLayer;

    private bool _changeWatcherEnabled;
    private long _nextCheckTicks;
    private long _lastWriteTicks;

    public void Init(MainWindowLogic main, VisualElement root, VisualTreeAsset paletteMappingEntryTemplate)
    {
        _main = main;
        _root = root;
        _paletteMappingEntryTemplate = paletteMappingEntryTemplate;

        _conversionProfile.SetPalette(_main.Palette);

        root.Q<Button>("input-image-load-button").clicked += OnImageLoadButtonClicked;
        root.Q<Button>("save-output-button").clicked += OnSaveOutputButtonClicked;
        _inputImageNameLabel = root.Q<Label>("input-image-name-label");

        root.Q<Button>("conversion-profile-new-button").clicked += OnConversionProfileNewButtonClicked;
        root.Q<Button>("conversion-profile-load-button").clicked += OnConversionProfileLoadButtonClicked;
        root.Q<Button>("conversion-profile-save-button").clicked += OnConversionProfileSaveButtonClicked;
        root.Q<Button>("conversion-profile-save-as-button").clicked += OnConversionProfileSaveAsButtonClicked;

        _cropSettingsContainer = root.Q<VisualElement>("crop-settings");
        _cropXSlider = _cropSettingsContainer.Q<SliderInt>("crop-x-slider");
        _cropYSlider = _cropSettingsContainer.Q<SliderInt>("crop-y-slider");
        _cropXSlider.RegisterValueChangedCallback(OnCropChanged);
        _cropYSlider.RegisterValueChangedCallback(OnCropChanged);

        _paletteMappingModeSelector = root.Q<RadioButtonGroup>("palette-mapping-mode");

        _automaticPaletteMapper = root.Q<VisualElement>("automatic-palette-mapper");
        _brightnessSlider = _automaticPaletteMapper.Q<SliderInt>("brightness-slider");
        _contrastSlider = _automaticPaletteMapper.Q<SliderInt>("contrast-slider");
        _saturationSlider = _automaticPaletteMapper.Q<SliderInt>("saturation-slider");
        _brightnessSlider.RegisterValueChangedCallback(OnPaletteMapperSliderChanged);
        _contrastSlider.RegisterValueChangedCallback(OnPaletteMapperSliderChanged);
        _saturationSlider.RegisterValueChangedCallback(OnPaletteMapperSliderChanged);

        _manualPaletteMapper = root.Q<VisualElement>("manual-palette-mapper");
        _paletteMappingModeSelector.RegisterValueChangedCallback(OnPaletteMappingModeChanged);
        _palettePicker = root.Q<VisualElement>("palette-picker");
        _paletteContainer = _palettePicker.Q<VisualElement>("palette-container");
        var entryIndex = 0;
        foreach (var entry in _paletteContainer.Children())
        {
            var intensity = (entryIndex >> 3) == 0 ? 0 : 0.5f;
            entry.style.backgroundColor = (Color)_main.Palette.Colors[entryIndex];
            var index = entryIndex;
            entry.AddManipulator(new Clickable(() => OnPickerEntryClicked(index)));
            entryIndex++;
        }
        _palettePicker.AddManipulator(new Clickable(OnPalettePickerAreaClicked));
        _paletteMappingTargets = new List<VisualElement>();

        _imageViews = root.Q<VisualElement>("image-views");

        var displaySettings = root.Q<VisualElement>("display-settings");
        var showInkToggle = displaySettings.Q<Toggle>("show-ink-toggle");
        var showSpritesToggle = displaySettings.Q<Toggle>("show-sprites-toggle");
        var showPaperToggle = displaySettings.Q<Toggle>("show-paper-toggle");
        showInkToggle.value = _showInkLayer;
        showSpritesToggle.value = _showSpriteLayer;
        showPaperToggle.value = _showPaperLayer;
        showInkToggle.RegisterValueChangedCallback(OnShowInkToggle);
        showSpritesToggle.RegisterValueChangedCallback(OnShowSpritesToggle);
        showPaperToggle.RegisterValueChangedCallback(OnShowPaperToggle);

        var pipelineSettings = root.Q<VisualElement>("pipeline-settings");
        _main.SetupViceBridgeToggle(pipelineSettings.Q<Toggle>("vice-bridge-toggle"));
        var changeWatcherToggle = pipelineSettings.Q<Toggle>("change-watcher-toggle");
        changeWatcherToggle.value = _changeWatcherEnabled;
        changeWatcherToggle.RegisterValueChangedCallback(OnChangeWatcherToggle);
    }

    public void ResetPath()
    {
        _inputImagePath = null;
        _inputImageNameLabel.text = "-";
    }

    public void OnUpdate()
    {
        ReloadImageIfChanged();
    }

    private void ReloadImageIfChanged()
    {
        if (!_changeWatcherEnabled)
        {
            return;
        }
        if (DateTime.UtcNow.Ticks <= _nextCheckTicks)
        {
            return;
        }
        _nextCheckTicks = DateTime.UtcNow.Ticks + 100 * TimeSpan.TicksPerMillisecond;
        if (string.IsNullOrEmpty(_inputImagePath) || _main.InputImage == null)
        {
            return;
        }
        var lastWriteTicks = File.GetLastWriteTimeUtc(_inputImagePath).Ticks;
        if (_lastWriteTicks >= lastWriteTicks)
        {
            return;
        }
        _lastWriteTicks = lastWriteTicks;
        LoadImage(true);
    }

    public void Refresh()
    {
        var x = _imagePreparation.X;
        var y = _imagePreparation.Y;
        if (_main.InputImage == null)
        {
            LoadImage();
        }
        else
        {
            RefreshInputImageNameLabel();
            ShowImage("view-input", _main.InputImage);
            RefreshCropSettings(x, y);
            SyncConversionProfileToImage();
        }
    }

    public void RefreshResultImage(int[] referencePixels)
    {
        if (!Active || _main.ResultImage == null)
        {
            return;
        }
        ShowImage("view-result", _main.ResultImage);
        var resultPixels = _main.ResultImage.GetPixels32();
        var errorPixels = new Color32[ScreenWidth * ScreenHeight];
        for (var i = 0; i < errorPixels.Length; i++)
        {
            var p = _main.Palette.Colors[referencePixels[(ScreenHeight - 1 - i / ScreenWidth) * ScreenWidth + i % ScreenWidth]];
            var r = resultPixels[i];
            errorPixels[i] = new Color32((byte)abs(p.r - r.r), (byte)abs(p.g - r.g), (byte)abs(p.b - r.b), 0xff);
        }
        if (_main.ErrorImage == null)
        {
            _main.ErrorImage = new Texture2D(ScreenWidth, ScreenHeight, TextureFormat.RGBA32, false);
        }
        _main.ErrorImage.SetPixels32(errorPixels);
        _main.ErrorImage.Apply();
        ShowImage("view-error", _main.ErrorImage);
    }

    private void OnShowInkToggle(ChangeEvent<bool> evt)
    {
        _showInkLayer = evt.newValue;
        RefreshConvertedImage();
    }

    private void OnShowSpritesToggle(ChangeEvent<bool> evt)
    {
        _showSpriteLayer = evt.newValue;
        RefreshConvertedImage();
    }

    private void OnShowPaperToggle(ChangeEvent<bool> evt)
    {
        _showPaperLayer = evt.newValue;
        RefreshConvertedImage();
    }

    private void OnChangeWatcherToggle(ChangeEvent<bool> evt)
    {
        _changeWatcherEnabled = evt.newValue;
        _lastWriteTicks = 0;
    }

    private void OnCropChanged(ChangeEvent<int> evt)
    {
        RefreshPreparedImage();
        RefreshConvertedImage();
    }

    private void OnPaletteMapperSliderChanged(ChangeEvent<int> evt)
    {
        var sliderName = ((SliderInt)evt.target).name;
        _conversionProfile.SetAdjustmentValue(sliderName.Replace("-slider", ""), evt.newValue);
        RefreshPalette();
    }

    private void OnPalettePickerAreaClicked(EventBase evt)
    {
        _palettePicker.style.display = DisplayStyle.None;
    }

    private void OnConversionProfileNewButtonClicked()
    {
        ResetConversionProfile();
    }

    private void OnConversionProfileLoadButtonClicked()
    {
        var path = _main.OpenFileBrowser("Select Profile", new ExtensionFilter("NUFLIX Conversion Profiles ", ProfileExtension));
        if (path == null)
        {
            return;
        }
        _conversionProfilePath = path;
        LoadConversionProfile();
    }

    private void OnConversionProfileSaveButtonClicked()
    {
        SaveConversionProfile(false);
    }

    private void OnConversionProfileSaveAsButtonClicked()
    {
        SaveConversionProfile(true);
    }

    private void ResetConversionProfile()
    {
        _conversionProfilePath = "";
        _conversionProfile = new ConversionProfile();
        _conversionProfile.SetPalette(_main.Palette);
        SyncConversionProfileToImage();
    }

    private void LoadConversionProfile()
    {
        if (string.IsNullOrEmpty(_conversionProfilePath) || !File.Exists(_conversionProfilePath))
        {
            return;
        }
        JsonUtility.FromJsonOverwrite(File.ReadAllText(_conversionProfilePath), _conversionProfile);
        SyncConversionProfileToImage();
    }

    private void SaveConversionProfile(bool saveAs)
    {
        var path = _conversionProfilePath;
        if (saveAs || string.IsNullOrEmpty(path))
        {
            var defaultName = string.IsNullOrEmpty(_inputImagePath) ? "profile" : Path.GetFileNameWithoutExtension(_inputImagePath);
            path = _main.SaveFileBrowser("Save Profile As", defaultName, ProfileExtension);
        }
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        _conversionProfilePath = path;
        File.WriteAllText(path, JsonUtility.ToJson(_conversionProfile, true));
    }

    private void RefreshConversionProfileControls()
    {
        _brightnessSlider.value = _conversionProfile.Brightness;
        _contrastSlider.value = _conversionProfile.Contrast;
        _saturationSlider.value = _conversionProfile.Saturation;
        _paletteMappingModeSelector.value = (int)_conversionProfile.Mode;
        _paletteMappingModeSelector.SetEnabled(_conversionProfile.SupportsManualMapping);
        RefreshPaletteMappingMode();
        RefreshPalette();
    }

    private void OnImageLoadButtonClicked()
    {
        var path = _main.OpenFileBrowser("Select Image", new ExtensionFilter("Images ", "png", "jpg"));
        if (path == null)
        {
            return;
        }
        _inputImagePath = path;
        LoadImage();
    }

    private void OnSaveOutputButtonClicked()
    {
        _main.SaveResults();
    }

    private void LoadImage(bool keepProfile = false)
    {
        if (string.IsNullOrEmpty(_inputImagePath) || !File.Exists(_inputImagePath))
        {
            return;
        }
        var bytes = File.ReadAllBytes(_inputImagePath);
        if (_main.InputImage != null)
        {
            UnityEngine.Object.DestroyImmediate(_main.InputImage);
        }
        _main.InputImage = new Texture2D(1, 1);
        if (!ImageConversion.LoadImage(_main.InputImage, bytes))
        {
            Debug.LogWarning($"Image at {_inputImagePath} couldn't be loaded.");
            return;
        }
        _main.ClearUndoStack();
        RefreshInputImageNameLabel();
        ShowImage("view-input", _main.InputImage);
        RefreshCropSettings();
        if (keepProfile)
        {
            SyncConversionProfileToImage();
        }
        else
        {
            ResetConversionProfile();
        }
    }

    private void RefreshInputImageNameLabel()
    {
        _inputImageNameLabel.text = Path.GetFileName(_inputImagePath);
    }

    private void SyncConversionProfileToImage()
    {
        if (_main.InputImage == null)
        {
            return;
        }
        _conversionProfile.InitPalettes(_main.InputImage);
        InitPaletteMappingEntries();
        _imagePreparation.Reset();
        RefreshConversionProfileControls();
    }

    private void RefreshCropSettings(int x = 0, int y = 0)
    {
        _cropSettingsContainer.style.display = _main.InputImage.width > ScreenWidth || _main.InputImage.height > ScreenHeight ? DisplayStyle.Flex : DisplayStyle.None;
        _cropXSlider.highValue = max(_main.InputImage.width - ScreenWidth, 0);
        _cropYSlider.highValue = max(_main.InputImage.height - ScreenHeight, 0);
        _cropXSlider.value = x;
        _cropYSlider.value = y;
    }

    public void ShowImage(string viewName, Texture2D tex)
    {
        if (tex == null)
        {
            return;
        }
        tex.filterMode = FilterMode.Point;
        var image = _imageViews.Q<VisualElement>(viewName).Q<VisualElement>("image");
        image.style.backgroundImage = tex;
    }

    private void RefreshPreparedImage()
    {
        _imagePreparation.Prepare(_main.InputImage, ref _main.PreparedImage, ref _main.PreparedPixels, _conversionProfile, _cropXSlider.value, _cropYSlider.value);
        ShowImage("view-prepared", _main.PreparedImage);
    }

    private void RefreshConvertedImage()
    {
        _main.ConvertPreparedImage();
    }

    private void RefreshPalette()
    {
        if (_conversionProfile.Mode == PaletteMappingMode.Automatic)
        {
            _conversionProfile.RefreshTargetPalette();
        }
        if (_paletteMappingTargets == null)
        {
            return;
        }
        for (var i = 0; i < _paletteMappingTargets.Count; i++)
        {
            _paletteMappingTargets[i].style.backgroundColor = (Color)_conversionProfile.GetTargetColor(i);
        }
        if (_main.InputImage == null)
        {
            return;
        }
        RefreshPreparedImage();
        RefreshConvertedImage();
    }

    private void InitPaletteMappingEntries()
    {
        _manualPaletteMapper.Clear();
        _paletteMappingTargets.Clear();
        for (var i = 0; i < _conversionProfile.SourceColors.Length; i++)
        {
            var color = _conversionProfile.SourceColors[i];
            var entry = _paletteMappingEntryTemplate.Instantiate();
            _manualPaletteMapper.Add(entry);
            entry.Q<VisualElement>("source").style.backgroundColor = (Color)color;
            var target = entry.Q<VisualElement>("target");
            target.style.backgroundColor = (Color)_conversionProfile.GetTargetColor(i);
            var index = i;
            target.AddManipulator(new Clickable(() => OnPaletteEntryClicked(target, index)));
            _paletteMappingTargets.Add(target);
        }
    }

    private void OnPaletteMappingModeChanged(ChangeEvent<int> evt)
    {
        _conversionProfile.Mode = (PaletteMappingMode)evt.newValue;
        RefreshPaletteMappingMode();
    }

    private void RefreshPaletteMappingMode()
    {
        _automaticPaletteMapper.style.display = _conversionProfile.Mode == PaletteMappingMode.Automatic ? DisplayStyle.Flex : DisplayStyle.None;
        _manualPaletteMapper.style.display = _conversionProfile.Mode == PaletteMappingMode.Manual ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnPaletteEntryClicked(VisualElement target, int index)
    {
        if (_conversionProfile.Mode == PaletteMappingMode.Automatic)
        {
            return;
        }
        _selectedMappingEntryIndex = index;
        var bounds = target.worldBound;
        var parentBounds = _palettePicker.worldBound;
        var viewBounds = _palettePicker.parent.worldBound;
        var paletteHeight = _paletteContainer.resolvedStyle.maxHeight.value;
        _paletteContainer.style.left = bounds.xMax + 4;
        _paletteContainer.style.top = min(bounds.center.y - parentBounds.yMin - paletteHeight / 2, viewBounds.height - paletteHeight);
        _palettePicker.style.display = DisplayStyle.Flex;
    }

    private void OnPickerEntryClicked(int index)
    {
        _palettePicker.style.display = DisplayStyle.None;
        _conversionProfile.TargetIndices[_selectedMappingEntryIndex] = index;
        _paletteMappingTargets[_selectedMappingEntryIndex].style.backgroundColor = (Color)_main.Palette.Colors[index];
        RefreshPreparedImage();
        RefreshConvertedImage();
    }
}
