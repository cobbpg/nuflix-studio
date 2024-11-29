using UnityEngine;
using UnityEngine.UIElements;

public class RuntimeMainWindow : MonoBehaviour
{
    [SerializeField] private MainWindowLogic _logic;

    private void Awake()
    {
        Application.runInBackground = true;
        _logic.Init(GetComponent<UIDocument>().rootVisualElement);
    }

    private void Update()
    {
        _logic.OnUpdate();
    }
}
