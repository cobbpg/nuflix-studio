using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class EditorMainWindow : EditorWindow
{
    [SerializeField] private VisualTreeAsset _asset;
    [SerializeField] private ThemeStyleSheet _stylesheet;
    [SerializeField] private MainWindowLogic _logic;

    [MenuItem("Window/NUFLIX Studio")]
    private static void ShowWindow()
    {
        GetWindow<EditorMainWindow>().Show();
    }

    private void CreateGUI()
    {
        titleContent = new GUIContent("NUFLIX Studio");
        var root = _asset.Instantiate();
        _logic.Init(root);
        rootVisualElement.styleSheets.Clear();
        rootVisualElement.styleSheets.Add(_stylesheet);
        rootVisualElement.Add(root);
        root.style.flexGrow = 1;
        EditorApplication.update -= _logic.OnUpdate;
        EditorApplication.update += _logic.OnUpdate;
    }
}
