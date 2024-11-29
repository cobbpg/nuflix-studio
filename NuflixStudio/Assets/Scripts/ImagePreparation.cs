//#define AREA5150_HACK

using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Constants;

[Serializable]
public class ImagePreparation
{
    private bool _forceRefresh;
    private int _x;
    private int _y;
    private Color32[] _inputPixels = new Color32[ScreenWidth * ScreenHeight];
    private int[] _inputIndices = new int[ScreenWidth * ScreenHeight];

    public int X => _x;
    public int Y => _y;

    public void Reset()
    {
        _forceRefresh = true;
    }

    public void Prepare(Texture2D input, ref Texture2D output, ref int[] outputPixels, ConversionProfile profile, int x, int y)
    {
        if (output == null)
        {
            output = new Texture2D(ScreenWidth, ScreenHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
            outputPixels = new int[ScreenWidth * ScreenHeight];
        }
        if (_forceRefresh || x != _x || y != _y || !profile.SupportsManualMapping)
        {
            _forceRefresh = false;
            _x = x;
            _y = y;
#if AREA5150_HACK
            if (input.width == 704 && input.height == 448)
            {
                // Special case for Area 5150 screenshots
                var tempPixels = input.GetPixels32();
                _inputPixels = new Color32[ScreenWidth * ScreenHeight];
                for (var py = 0; py < ScreenHeight; py++)
                {
                    for (var px = 0; px < ScreenWidth >> 2; px++)
                    {
                        var si = ((py << 1) + 24) * input.width + (px << 3) + 32;
                        var ti = py * ScreenWidth + (px << 2);
                        var repeated = true;
                        for (var i = 0; i < 4; i++)
                        {
                            var c1 = tempPixels[si + i];
                            var c2 = tempPixels[si + i + 4];
                            repeated &= c1.r == c2.r;
                            repeated &= c1.g == c2.g;
                            repeated &= c1.b == c2.b;
                        }
                        for (var i = 0; i < 4; i++)
                        {
                            _inputPixels[ti + i] = repeated ? tempPixels[si + i] : tempPixels[si + (i << 1)];
                        }
                    }
                }
                output.SetPixels32(_inputPixels);
                output.Apply();
            }
            else
#endif
            {
                Graphics.CopyTexture(input, 0, 0, x, input.height - ScreenHeight - y, ScreenWidth, ScreenHeight, output, 0, 0, 0, 0);
                _inputPixels = output.GetPixels32();
            }
            if (profile.SupportsManualMapping)
            {
                var colorMapping = new Dictionary<int, int>();
                for (var i = 0; i < profile.SourceColors.Length; i++)
                {
                    colorMapping[Palette.GetColorKey(profile.SourceColors[i])] = i;
                }
                for (var i = 0; i < _inputPixels.Length; i++)
                {
                    _inputIndices[i] = colorMapping[Palette.GetColorKey(_inputPixels[i])];
                }
            }
        }
        if (profile.SupportsManualMapping)
        {
            for (var py = 0; py < ScreenHeight; py++)
            {
                var bi = py * ScreenWidth;
                var bo = (ScreenHeight - 1 - py) * ScreenWidth;
                for (var px = 0; px < ScreenWidth; px++)
                {
                    var pixel = _inputIndices[bi + px];
                    outputPixels[bo + px] = profile.TargetIndices[pixel];
                    _inputPixels[bi + px] = profile.GetTargetColor(pixel);
                }
            }
        }
        else
        {
            var paletteArray = new NativeArray<Color32>(profile.C64Palette.Colors.ToArray(), Allocator.TempJob);
            var oklabPaletteArray = new NativeArray<float3>(profile.C64Palette.Colors.Select(col => Palette.RgbToOkLab(col)).ToArray(), Allocator.TempJob);
            var inputPixelsArray = new NativeArray<Color32>(_inputPixels, Allocator.TempJob);
            var outputPixelsArray = new NativeArray<Color32>(_inputPixels, Allocator.TempJob);
            var outputIndicesArray = new NativeArray<int>(outputPixels, Allocator.TempJob);

            var matchPalette = new MatchPaletteJob
            {
                Brightness = profile.Brightness / 100f,
                Contrast = profile.Contrast / 100f,
                Saturation = profile.Saturation / 100f,
                Palette = paletteArray,
                OklabPalette = oklabPaletteArray,
                InputPixels = inputPixelsArray,
                OutputPixels = outputPixelsArray,
                Indices = outputIndicesArray,
            };

            matchPalette.Schedule(ScreenWidth * ScreenHeight, ScreenWidth).Complete();
            outputIndicesArray.CopyTo(outputPixels);
            outputPixelsArray.CopyTo(_inputPixels);

            oklabPaletteArray.Dispose();
            paletteArray.Dispose();
            inputPixelsArray.Dispose();
            outputPixelsArray.Dispose();
            outputIndicesArray.Dispose();
        }
        output.SetPixels32(_inputPixels);
        output.Apply();
    }

    [BurstCompile]
    struct MatchPaletteJob : IJobParallelFor
    {
        public float Brightness;
        public float Contrast;
        public float Saturation;

        [ReadOnly] public NativeArray<Color32> Palette;
        [ReadOnly] public NativeArray<float3> OklabPalette;
        [ReadOnly] public NativeArray<Color32> InputPixels;

        [WriteOnly] public NativeArray<Color32> OutputPixels;
        [NativeDisableParallelForRestriction][WriteOnly] public NativeArray<int> Indices;

        public void Execute(int index)
        {
            var colorIndex = ConversionProfile.GetAdjustedC64Color(InputPixels[index], OklabPalette, Brightness, Contrast, Saturation);
            Indices[(ScreenHeight - 1 - index / ScreenWidth) * ScreenWidth + index % ScreenWidth] = colorIndex;
            OutputPixels[index] = Palette[colorIndex];
        }
    }
}