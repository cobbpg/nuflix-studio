using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

[Serializable]
public class Palette
{
    public List<Color32> Colors;

    private List<List<int>> _distances;

    public List<List<int>> Distances => _distances;

    public void ResetDistances()
    {
        _distances = null;
    }

    public NativeArray<int> GetColorDistancesArray()
    {
        var result = new NativeArray<int>(0x100, Allocator.Persistent);
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Distance(i & 0xf, i >> 4);
        }
        return result;
    }

    public int Distance(int c1, int c2)
    {
        if (_distances == null || _distances.Count == 0)
        {
            _distances = new List<List<int>>();
            for (var i = 0; i < Colors.Count; i++)
            {
                var ds = new List<int>();
                var ci = Colors[i];
                for (var j = 0; j < Colors.Count; j++)
                {
                    ds.Add(Distance(ci, Colors[j]) << 3);
                }
                _distances.Add(ds);
            }
        }
        return _distances[c1][c2];
    }

    public static int Distance(Color32 c1, Color32 c2)
    {
        return (int)(distance(AdjustBlack(RgbToOkLab(c1)), AdjustBlack(RgbToOkLab(c2))) * 1024);
    }

    public static float3 AdjustBlack(float3 lab)
    {
        // Pure black has such a low L value that very dark colours end up getting matched with blue without this adjustment
        lab.x = max(lab.x, 0.25f);
        return lab;
    }

    public static void RgbToOkLab(Color c, out float L, out float a, out float b)
    {
        float l = 0.4122214708f * c.r + 0.5363325363f * c.g + 0.0514459929f * c.b;
        float m = 0.2119034982f * c.r + 0.6806995451f * c.g + 0.1073969566f * c.b;
        float s = 0.0883024619f * c.r + 0.2817188376f * c.g + 0.6299787005f * c.b;

        float l_ = pow(l, 1 / 3f);
        float m_ = pow(m, 1 / 3f);
        float s_ = pow(s, 1 / 3f);

        L = 0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_;
        a = 1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_;
        b = 0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_;
    }

    public static float3 RgbToOkLab(Color c)
    {
        RgbToOkLab(c, out var L, out var a, out var b);
        return new float3(L, a, b);
    }

    public static Color OkLabToRgb(float L, float a, float b)
    {
        float l_ = L + 0.3963377774f * a + 0.2158037573f * b;
        float m_ = L - 0.1055613458f * a - 0.0638541728f * b;
        float s_ = L - 0.0894841775f * a - 1.2914855480f * b;

        float l = l_ * l_ * l_;
        float m = m_ * m_ * m_;
        float s = s_ * s_ * s_;

        var r_ = +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
        var g_ = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
        var b_ = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

        return new Color(r_, g_, b_);
    }

    // This is needed to avoid garbage when using colours as dictionary keys
    public static int GetColorKey(Color32 color) => (color.r << 16) | (color.g << 8) | color.b;

    public static Palette ReadFromVpl(string path)
    {
        var colors = new List<Color32>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            var rgb = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var r = Convert.ToByte(rgb[0], 16);
            var g = Convert.ToByte(rgb[1], 16);
            var b = Convert.ToByte(rgb[2], 16);
            colors.Add(new Color32(r, g, b, 0xff));
        }
        return new Palette { Colors = colors };
    }

    public void WriteToVpl(string path)
    {
        var sb = new StringBuilder();
        foreach (var color in Colors)
        {
            sb.AppendLine($"{color.r:x2} {color.g:x2} {color.b:x2}");
        }
        File.WriteAllText(path, sb.ToString());
    }
}
