using SFB;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;
using static Constants;

[CreateAssetMenu(menuName = "Custom/Main Window Logic")]
public class MainWindowLogic : ScriptableObject
{
    [SerializeField] private VisualTreeAsset _paletteMappingEntryTemplate;
    [SerializeField] private EditorPaneAssets _editorPaneAssets;

    public const string Version = "1.1";
    public const string SettingsDir = "Settings";
    private const string LogFile = "nuflix-log.txt";

    public TabView MainTabView { get; private set; }
    private VisualElement _root;
    private ConverterPane _converterPane = new();
    private EditorPane _editorPane = new();
    private Label _errorLabel;

    public Palette Palette { get; private set; }
    public MonitorConnection MonitorConnection { get; private set; }
    private bool _viceBridgeEnabled;
    public bool ViceBridgeEnabled
    {
        get => _viceBridgeEnabled;
        set
        {
            if (_viceBridgeEnabled == value)
            {
                return;
            }
            _viceBridgeEnabled = value;
            OnViceBridgeToggled?.Invoke(_viceBridgeEnabled);
        }
    }
    public Action<bool> OnViceBridgeToggled;

    public NuflixFormat NuflixFormat = new();

    [Header("Editor State")]

#if UNITY_EDITOR
    private string _lastDirectory = "Images";
#else
    private string _lastDirectory = "";
#endif

    public string InputImagePath => _converterPane.InputImagePath;

    public Texture2D InputImage;
    public Texture2D PreparedImage;
    public Texture2D ResultImage;
    public Texture2D ErrorImage;

    public int[] PreparedPixels;

    private IEnumerator _conversionPipeline;
    private ConversionPipelineStage _pipelineStage;

    public LayeredImage WorkImage;
    public byte[] NufliBytes;
    public List<int> FreeCycles;

    private VideoStandard? ActiveViceStandard;
    private bool _queryingStandard;

    private readonly List<byte[]> _undoStack = new();
    private int _undoStackIndex;

    public void Init(VisualElement root)
    {
        File.Delete(LogFile);
        KeyBindings.Load();
        MonitorConnection = new();
        Palette = Palette.ReadFromVpl($"{SettingsDir}/palette.vpl");

        _root = root;
        root.focusable = true;
        root.Focus();
        MainTabView = root.Q<TabView>("tab-main");
        MainTabView.activeTabChanged += OnActiveTabChanged;

        root.Q<Button>("open-manual").clicked += () => Application.OpenURL($"https://github.com/cobbpg/nuflix-studio/blob/v{Version}/manual/manual.md");

        _errorLabel = root.Q<Label>("error-label");
        _conversionPipeline = ImageConversionPipeline().GetEnumerator();
        _converterPane.Init(this, root, _paletteMappingEntryTemplate);
        _editorPane.Init(this, root, _editorPaneAssets);

#if UNITY_EDITOR
        RestoreAfterHotswapping();
#endif
    }

    public static void Log(string message)
    {
        Debug.Log(message);
        File.AppendAllText(LogFile, message + "\n");
    }

    private void OnActiveTabChanged(Tab oldTab, Tab newTab)
    {
        switch (newTab.name)
        {
            case ConverterPane.TabName:
                RefreshResultImage(false);
                _converterPane.RefreshResultImage(PreparedPixels);
                break;
            case EditorPane.TabName:
                RefreshResultImage(true);
                break;
        }
    }

    public void OnUpdate()
    {
        _conversionPipeline.MoveNext();
        _converterPane.OnUpdate();
        _editorPane.OnUpdate();
    }

#if UNITY_EDITOR
    private void RestoreAfterHotswapping()
    {
        if (Application.isPlaying)
        {
            return;
        }
        ClearUndoStack();
        _converterPane.Refresh();
    }

    public void ResetBeforeBuild()
    {
        ResetConverter();
        DestroyImage(ref InputImage);
        DestroyImage(ref PreparedImage);
        DestroyImage(ref ResultImage);
        DestroyImage(ref ErrorImage);
        PreparedPixels = null;
        NufliBytes = null;
        FreeCycles = null;
    }

    private static void DestroyImage(ref Texture2D image)
    {
        if (image != null)
        {
            DestroyImmediate(image);
        }
        image = null;
    }
#endif

    public string OpenFileBrowser(string title, params ExtensionFilter[] filter)
    {
        var paths = StandaloneFileBrowser.OpenFilePanel(title, _lastDirectory, filter, false);
        if (paths.Length == 0)
        {
            return null;
        }
        var path = paths[0];
        if (!File.Exists(path))
        {
            return null;
        }
        _lastDirectory = Path.GetDirectoryName(path);
        return path;
    }

    public string SaveFileBrowser(string title, string defaultName, string extension)
    {
        var path = StandaloneFileBrowser.SaveFilePanel(title, _lastDirectory, defaultName, extension);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }
        _lastDirectory = Path.GetDirectoryName(path);
        return path;
    }

    private IEnumerable ImageConversionPipeline()
    {
        while (true)
        {
            if (_pipelineStage == ConversionPipelineStage.Idle)
            {
                yield return null;
                continue;
            }
            var stage = _pipelineStage;
            _pipelineStage = ConversionPipelineStage.Idle;
            var workImage = WorkImage;

            if (stage == ConversionPipelineStage.ExtractLayers || workImage == null)
            {
                workImage = new LayeredImage(PreparedPixels, Palette);
                var gen = workImage.GenerateColors(false).GetEnumerator();
                var success = false;
                while (true)
                {
                    try
                    {
                        if (!gen.MoveNext())
                        {
                            success = true;
                            break;
                        }
                    }
                    catch
                    {
                        break;
                    }
#if UNITY_EDITOR
                    _root.MarkDirtyRepaint();
#endif
                    yield return null;
                }
                if (!success)
                {
                    continue;
                }
                stage = ConversionPipelineStage.GenerateLayers;
            }

            if (stage == ConversionPipelineStage.GenerateLayers)
            {
                workImage.GenerateLayers();
                stage = ConversionPipelineStage.Refresh;
            }

            if (stage == ConversionPipelineStage.Refresh)
            {
                Profiler.BeginSample("Refresh");
                Profiler.BeginSample("Export");
                var result = NuflixFormat.Export(workImage, ActiveViceStandard == VideoStandard.Ntsc);
                Profiler.EndSample();
                WorkImage = workImage;
                NufliBytes = result.Bytes;
                FreeCycles = result.FreeCycles;

                Profiler.BeginSample("Send");
                SendBinaryToVice(NufliBytes, workImage.BorderColors[0], workImage.TopBackgroundColor);
                Profiler.EndSample();

                Profiler.BeginSample("Render");
                RefreshResultImage(_editorPane.Active);
                Profiler.EndSample();

                Profiler.BeginSample("ResultImage");
                _converterPane.RefreshResultImage(WorkImage.ReferencePixels.ToArray());
                Profiler.EndSample();
                Profiler.BeginSample("EditorTexture");
                _editorPane.RefreshEditorTexture(true);
                Profiler.EndSample();
                if (result.Error != null)
                {
                    _errorLabel.text = result.Error;
                    _errorLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _errorLabel.style.display = DisplayStyle.None;
                }

                Profiler.EndSample();
#if UNITY_EDITOR
                _root.MarkDirtyRepaint();
#endif
            }
        }
    }

    private void RefreshResultImage(bool forceAllLayers)
    {
        if (forceAllLayers)
        {
            NuflixFormat.Image?.Render(ref ResultImage, true, true, true, true);
        }
        else
        {
            NuflixFormat.Image?.Render(ref ResultImage, _converterPane.ShowInkLayer, _converterPane.ShowSpriteLayer, _converterPane.ShowSpriteLayer, _converterPane.ShowPaperLayer);
        }
    }

    private void SendBinaryToVice(byte[] nufliBytes, int topBorderColor, int topBackgroundColor)
    {
        if (!ViceBridgeEnabled)
        {
            return;
        }

        void send()
        {
            var xReg = ActiveViceStandard == VideoStandard.Pal ? 0x01 : 0x00;
            MonitorConnection.SendCommand(ViceMonitorCommand.MemorySet(0xffe, nufliBytes));
            MonitorConnection.SendCommand(ViceMonitorCommand.RegistersSet(MemSpace.MainMemory, new() { { RegisterId.SP, 0xff }, { RegisterId.X, xReg }, { RegisterId.PC, 0x3003 } }));
            MonitorConnection.SendCommand(ViceMonitorCommand.MemorySet(0xd020, new byte[] { (byte)topBorderColor, (byte)topBackgroundColor }));
            MonitorConnection.SendCommand(ViceMonitorCommand.Exit());
        }

        if (ActiveViceStandard == null)
        {
            _queryingStandard = false;
            QueryActiveVideoStandard(send);
        }
        else
        {
            send();
            QueryActiveVideoStandard();
        }
    }

    private void QueryActiveVideoStandard(Action runAfter = null)
    {
        if (_queryingStandard)
        {
            return;
        }
        _queryingStandard = true;
        MonitorConnection.SendCommand(
            ViceMonitorCommand.ResourceGet("MachineVideoStandard"),
            response =>
            {
                _queryingStandard = false;
                var standardCode = response.ResourceGetInt();
                ActiveViceStandard = standardCode switch
                {
                    1 => VideoStandard.Pal,
                    2 => VideoStandard.Ntsc,
                    3 => VideoStandard.Ntsc,
                    4 => VideoStandard.Pal, // Drean, not supported
                    _ => null
                };
                if (ActiveViceStandard == null)
                {
                    Debug.LogWarning($"Unsupported video standard {standardCode}");
                    return;
                }
                if (runAfter != null)
                {
                    runAfter();
                }
                else
                {
                    MonitorConnection.SendCommand(ViceMonitorCommand.Exit());
                }
            },
            () =>
            {
                _queryingStandard = false;
            });
    }

    public void ConvertPreparedImage()
    {
        _pipelineStage = ConversionPipelineStage.ExtractLayers;
#if UNITY_EDITOR
        _root.MarkDirtyRepaint();
#endif
    }

    public void GenerateLayers()
    {
        _pipelineStage = ConversionPipelineStage.GenerateLayers;
#if UNITY_EDITOR
        _root.MarkDirtyRepaint();
#endif
    }

    public void RefreshWorkImage()
    {
        _pipelineStage = ConversionPipelineStage.Refresh;
#if UNITY_EDITOR
        _root.MarkDirtyRepaint();
#endif
    }

    public string GetPathWithoutExtension(string path) => Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));

    public void SaveResults()
    {
        var inputImagePath = _converterPane.InputImagePath;
        var basePath = GetPathWithoutExtension(inputImagePath);
        var result = NuflixFormat.Export(WorkImage, false, $"{basePath}-speedcode.txt");
        File.WriteAllBytes($"{basePath}.prg", result.Bytes);
        if (PreparedImage != null)
        {
            File.WriteAllBytes($"{basePath}-prepared.png", PreparedImage.EncodeToPNG());
        }
        if (ResultImage != null)
        {
            File.WriteAllBytes($"{basePath}-result.png", ResultImage.EncodeToPNG());
        }
    }

    public void SetupViceBridgeToggle(Toggle toggle)
    {
        toggle.value = ViceBridgeEnabled;
        toggle.RegisterValueChangedCallback(evt => ViceBridgeEnabled = evt.newValue);
        OnViceBridgeToggled += (enabled) => toggle.value = enabled;
    }

    public void ClearUndoStack()
    {
        _undoStack.Clear();
        _undoStackIndex = 0;
    }

    public void MakeUndoCheckpoint()
    {
        if (WorkImage == null)
        {
            return;
        }
        _undoStack.RemoveRange(_undoStackIndex, _undoStack.Count - _undoStackIndex);
        _undoStack.Add(WorkImage.Write());
        _undoStackIndex++;
    }

    public void PerformUndoRedo(bool isRedo)
    {
        if (isRedo)
        {
            if (_undoStackIndex < _undoStack.Count - 1)
            {
                _undoStackIndex++;
                WorkImage.Read(_undoStack[_undoStackIndex]);
                RefreshAfterUndoRedo();
            }
        }
        else
        {
            if (_undoStackIndex > 0)
            {
                if (_undoStackIndex == _undoStack.Count)
                {
                    _undoStack.Add(WorkImage.Write());
                }
                _undoStackIndex--;
                WorkImage.Read(_undoStack[_undoStackIndex]);
                RefreshAfterUndoRedo();
            }
        }
    }

    private void RefreshAfterUndoRedo()
    {
        _editorPane.RefreshEditorTexture(true);
        _editorPane.SetEditMode(WorkImage.DirectlyEditing);
        WorkImage.ReferencePixels.AsSpan().CopyTo(PreparedPixels);
        RefreshPreparedImage();
        RefreshWorkImage();
    }

    public void RefreshPreparedImage()
    {
        if (PreparedImage == null)
        {
            PreparedImage = new Texture2D(ScreenWidth, ScreenHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
        }
        var newPixels = new Color32[ScreenWidth * ScreenHeight];
        for (var y = 0; y < ScreenHeight; y++)
        {
            for (var x = 0; x < ScreenWidth; x++)
            {
                newPixels[y * ScreenWidth + x] = Palette.Colors[PreparedPixels[(ScreenHeight - 1 - y) * ScreenWidth + x]];
            }
        }
        PreparedImage.SetPixels32(newPixels);
        PreparedImage.Apply();
    }

    public void ResetConverter()
    {
        _converterPane.ResetPath();
    }
}

enum ConversionPipelineStage
{
    Idle, ExtractLayers, GenerateLayers, Refresh
}

enum VideoStandard
{
    Pal, Ntsc
}