using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

[Serializable]
public class ConversionProfile
{
    public Palette C64Palette { get; private set; }

    public PaletteMappingMode Mode;

    public int Brightness;
    public int Contrast;
    public int Saturation;

    public Color32[] SourceColors = new Color32[0];
    public int[] TargetIndices = new int[0];

    public bool SupportsManualMapping => SourceColors.Length > 0;

    public void SetPalette(Palette palette)
    {
        C64Palette = palette;
    }

    public Color32 GetTargetColor(int index) => C64Palette.Colors[TargetIndices[index]];

    public void SetAdjustmentValue(string key, int value)
    {
        switch (key)
        {
            case "brightness":
                Brightness = value;
                break;
            case "contrast":
                Contrast = value;
                break;
            case "saturation":
                Saturation = value;
                break;
        }
    }

    public void InitPalettes(Texture2D inputImage)
    {
        var pixels = inputImage.GetPixels32();
        var colors = new HashSet<Color32>(pixels);
        var count = colors.Count;
        if (colors.Count > 64)
        {
            SourceColors = new Color32[0];
            TargetIndices = new int[0];
            Mode = PaletteMappingMode.Automatic;
            return;
        }
        if (colors.All(c => SourceColors.Contains(c)))
        {
            return;
        }
        var ordering = colors.ToDictionary(col => col, col =>
        {
            var lab = Palette.RgbToOkLab(col);
            return Mathf.Round(lab.x * 5) * 10 + Mathf.Atan2(lab.y, lab.z);
        }
        );
        var existingSourceColors = SourceColors.ToList();
        var existingTargetIndices = TargetIndices;
        SourceColors = new Color32[count];
        TargetIndices = new int[count];
        var i = 0;
        foreach (var (color, _) in ordering.OrderBy(entry => entry.Value))
        {
            SourceColors[i] = color;
            var existingIndex = existingSourceColors.IndexOf(color);
            var index = existingIndex < 0 ? GetAdjustedC64Color(color) : existingTargetIndices[existingIndex];
            TargetIndices[i] = index;
            i++;
        }
    }

    public void RefreshTargetPalette()
    {
        for (var i = 0; i < TargetIndices.Length; i++)
        {
            TargetIndices[i] = GetAdjustedC64Color(SourceColors[i]);
        }
    }

    public int GetAdjustedC64Color(Color32 color)
    {
        Palette.RgbToOkLab(color, out float L, out float a, out float b);
        var brightness = Brightness / 100f;
        return GetClosestC64Color(Palette.OkLabToRgb(L * (Contrast / 100f + 1) + brightness * brightness * brightness, a * (Saturation / 100f + 1), b * (Saturation / 100f + 1)));
    }

    public int GetClosestC64Color(Color32 color)
    {
        var dMin = int.MaxValue;
        var result = -1;
        for (var i = 0; i < C64Palette.Colors.Count; i++)
        {
            var d = Palette.Distance(color, C64Palette.Colors[i]);
            if (d < dMin)
            {
                dMin = d;
                result = i;
            }
        }
        return result;
    }

    public static int GetAdjustedC64Color(Color32 color, NativeArray<float3> palette, float brightness, float contrast, float saturation)
    {
        Palette.RgbToOkLab(color, out float L, out float a, out float b);
        return GetClosestC64Color(new float3(L * (contrast + 1) + brightness * brightness * brightness, a * (saturation + 1), b * (saturation + 1)), palette);
    }

    public static int GetClosestC64Color(float3 color, NativeArray<float3> palette)
    {
        var dMin = float.MaxValue;
        var result = -1;
        for (var i = 0; i < palette.Length; i++)
        {
            var d = distance(color, palette[i]);
            if (d < dMin)
            {
                dMin = d;
                result = i;
            }
        }
        return result;
    }
}

public enum PaletteMappingMode
{
    Automatic = 0,
    Manual = 1
}