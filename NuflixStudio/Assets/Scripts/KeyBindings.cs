using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class KeyBindings
{
    public static readonly Dictionary<KeyCommand, Command> Mapping = new Dictionary<KeyCommand, Command>();

    public static void Load()
    {
        Mapping.Clear();
        foreach (var configLine in File.ReadAllLines($"{MainWindowLogic.SettingsDir}/keyboard.conf"))
        {
            if (configLine.StartsWith('#') || string.IsNullOrWhiteSpace(configLine))
            {
                continue;
            }
            var parts = configLine.Split('=');
            if (parts.Length < 2)
            {
                MainWindowLogic.Log($"Malformed keyboard config line: {configLine}");
                continue;
            }
            var commandString = parts[0].Trim();
            if (!Enum.TryParse<Command>(commandString, out var command))
            {
                MainWindowLogic.Log($"Unknown keyboard command: {commandString}");
                continue;
            }
            var keyCommand = new KeyCommand();
            foreach (var keyPart in parts[1].Split('+'))
            {
                var part = keyPart.Trim().ToLowerInvariant();
                switch (part)
                {
                    case "shift":
                        keyCommand.Shift = true;
                        break;
                    case "control":
                        keyCommand.Control = true;
                        break;
                    case "alt":
                        keyCommand.Alt = true;
                        break;
                    default:
                        if (Enum.TryParse<KeyCode>(part, true, out var keyCode))
                        {
                            keyCommand.Code = keyCode;
                        }
                        break;
                }
            }
            if (keyCommand.Code == KeyCode.None)
            {
                MainWindowLogic.Log($"Unknown keycode: {parts[1]}");
                continue;
            }
            Mapping[keyCommand] = command;
        }
    }

    public static Command GetCommand(KeyCode keyCode, bool shift, bool control, bool alt)
    {
        var keyCommand = new KeyCommand { Code = keyCode, Shift = shift, Control = control, Alt = alt };
        if (Mapping.TryGetValue(keyCommand, out var command))
        {
            return command;
        }
        // Handling the modifier agnostic colour keys
        var plainKeyCommand = new KeyCommand { Code = keyCode };
        if (Mapping.TryGetValue(plainKeyCommand, out var plainCommand))
        {
            return plainCommand;
        }
        return Command.None;
    }
}

public struct KeyCommand
{
    public KeyCode Code;
    public bool Shift;
    public bool Control;
    public bool Alt;
}

public enum Command
{
    None,

    PerformUndo,
    PerformRedo,

    SetViewModeSplit,
    SetViewModeFree,
    SetViewModeLayers,
    SetViewModeResult,

    ZoomIn,
    ZoomOut,

    ToggleInkLayer,
    ToggleHiresSpriteLayer,
    ToggleLoresSpriteLayer,
    TogglePaperLayer,
    ToggleErrorComparison,

    SelectPenInk,
    SelectPenPaper,
    SelectPenSprite,
    SelectPenMulti1,
    SelectPenMulti2,
    SelectPenMulti3,

    SetColor00,
    SetColor01,
    SetColor02,
    SetColor03,
    SetColor04,
    SetColor05,
    SetColor06,
    SetColor07,
    SetColor08,
    SetColor09,
    SetColor10,
    SetColor11,
    SetColor12,
    SetColor13,
    SetColor14,
    SetColor15,
}