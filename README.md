# NUFLIX Studio

NUFLIX Studio is a system to convert images into C64 executables that try to reproduce the original as closely as possible given the limitations of the graphics hardware. The name stands for NUFLI eXtended, because it's an incremental improvement over [NUFLI](https://www.c64-wiki.com/wiki/NUFLI). The system is implemented in Unity, and this repository contains the full source code under the `NuflixStudio` directory. Further information:

- [Technical deep-dive into NUFLI and NUFLIX](https://cobbpg.github.io/articles/nuflix.html)
- [NUFLIX Studio Video demonstration](https://www.youtube.com/watch?v=8amfX50ubeE)
- [Manual](manual/manual.md)

## Editor Setup

The UI is implemented using [UI Toolkit](https://docs.unity3d.com/Documentation/Manual/UIElements.html), and it is set up in such a way that it can be used within the editor without having to press play. The window can be opened from the menu via `Window → NUFLIX Studio`, and it's ready to use straight away. It is recommended to right click the title of the tab and turn on `UI Toolkit Live Reload`, which will cause the window to pick up any changes to the UI structure immediately. The window is defined in `Assets/UI/MainWindow.uxml`.

The main window is implemented in the `MainWindowLogic` class. In builds it is driven via the `RuntimeMainWindow` wrapper, while the editor window is set up by the `EditorMainWindow` class. There's limited support for retaining some state during hot reloading, so whenever the code is modified, the editor will reload the same file and remember conversion settings. However, it doesn't retain edits to the picture. See the `MainWindowLogic.RestoreAfterHotswapping()` method for details.

The two main screens are defined by the `ConverterPane` and `EditorPane` classes, while `MainWindowLogic` acts as the glue between them, and manages the image conversion pipeline.