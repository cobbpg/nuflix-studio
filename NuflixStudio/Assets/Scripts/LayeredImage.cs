using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using static Constants;
using static Unity.Mathematics.math;

public class LayeredImage
{
    public Palette Palette ;

    public NativeArray<int> ReferencePixels = new(ScreenWidth * ScreenHeight, Allocator.Persistent);

    public NativeArray<bool> Bitmap = new(ScreenWidth * ScreenHeight, Allocator.Persistent);
    public NativeArray<int> BitmapColors = new(AttributeWidth * AttributeHeight, Allocator.Persistent);

    public NativeArray<bool> Underlay = new(UnderlayColumns * SpriteWidth * ScreenHeight, Allocator.Persistent);
    public NativeArray<int> UnderlayColors = new(UnderlayColumns * ScreenHeight, Allocator.Persistent);

    public NativeArray<bool> Bug = new(BugSprites * SpriteWidth * ScreenHeight, Allocator.Persistent);
    public NativeArray<int> BugColors = new(BugColorSlots * AttributeHeight, Allocator.Persistent);
    public NativeArray<int> ReferenceBugColors = new(BugColorSlots * AttributeHeight, Allocator.Persistent);

    public int TopBackgroundColor;
    public int BottomBackgroundColor;
    public NativeArray<int> BorderColors = new(AttributeHeight + 1, Allocator.Persistent);

    public bool Valid => Bitmap.Length == ScreenWidth * ScreenHeight;

    public bool DirectlyEditing;

    public LayeredImage(int[] referencePixels, Palette palette)
    {
        Palette = palette;
        referencePixels.AsSpan().CopyTo(ReferencePixels);
    }

    public LayeredImage(Palette palette)
    {
        Palette = palette;
    }

    ~LayeredImage()
    {
        ReferencePixels.Dispose();
        Bitmap.Dispose();
        BitmapColors.Dispose();
        Underlay.Dispose();
        UnderlayColors.Dispose();
        Bug.Dispose();
        BugColors.Dispose();
        ReferenceBugColors.Dispose();
        BorderColors.Dispose();
    }

    public void GenerateColors(bool generateMid, bool generateBug, bool generateRight)
    {
        GenerateColors(true, generateMid, generateBug, generateRight).GetEnumerator().MoveNext();
    }

    public IEnumerable<LayeredImage> GenerateColors(bool instant, bool generateMid = true, bool generateBug = true, bool generateRight = true)
    {
        var colorDistancesArray = Palette.GetColorDistancesArray();
        var referencePixels = ReferencePixels;
        var bitmapColors = BitmapColors;
        var underlayColors = UnderlayColors;
        var bugColors = BugColors;
        var referenceBugColors = ReferenceBugColors;

        var prevBitmapColors = new int[bitmapColors.Length];
        bitmapColors.AsSpan().CopyTo(prevBitmapColors);

        if (generateMid || generateBug)
        {
            var kernel = Resources.Load<ComputeShader>("NufliKernel");
            var midIdealScatterIndex = kernel.FindKernel("MidIdealScatter");
            var midIdealGatherIndex = kernel.FindKernel("MidIdealGather");
            var bugIdealIndex = kernel.FindKernel("BugIdeal");
            var pixelsBuffer = new ComputeBuffer(referencePixels.Length, 4);
            var colorDistancesBuffer = new ComputeBuffer(colorDistancesArray.Length, 4);
            var bitmapColorsBuffer = new ComputeBuffer(bitmapColors.Length, 4);
            var underlayColorsBuffer = new ComputeBuffer(underlayColors.Length, 4);
            var underlayDeltasBuffer = new ComputeBuffer(underlayColors.Length << 8, 4);
            var underlayPaperBuffer = new ComputeBuffer(underlayColors.Length << 8, 4);
            var underlayInkBuffer = new ComputeBuffer(underlayColors.Length << 8, 4);
            var bugColorsBuffer = new ComputeBuffer(bugColors.Length, 4);
            pixelsBuffer.SetData(referencePixels);
            colorDistancesBuffer.SetData(colorDistancesArray);
            bitmapColorsBuffer.SetData(bitmapColors); // Init to avoid problems later
            underlayColorsBuffer.SetData(underlayColors);

            if (generateMid)
            {
                kernel.SetBuffer(midIdealScatterIndex, "Pixels", pixelsBuffer);
                kernel.SetBuffer(midIdealScatterIndex, "ColorDistances", colorDistancesBuffer);
                kernel.SetBuffer(midIdealScatterIndex, "MidDeltas", underlayDeltasBuffer);
                kernel.SetBuffer(midIdealScatterIndex, "MidPaper", underlayPaperBuffer);
                kernel.SetBuffer(midIdealScatterIndex, "MidInk", underlayInkBuffer);
                kernel.SetBuffer(midIdealGatherIndex, "Pixels", pixelsBuffer);
                kernel.SetBuffer(midIdealGatherIndex, "ColorDistances", colorDistancesBuffer);
                kernel.SetBuffer(midIdealGatherIndex, "BitmapColors", bitmapColorsBuffer);
                kernel.SetBuffer(midIdealGatherIndex, "UnderlayColors", underlayColorsBuffer);
                kernel.SetBuffer(midIdealGatherIndex, "MidDeltas", underlayDeltasBuffer);
                kernel.SetBuffer(midIdealGatherIndex, "MidPaper", underlayPaperBuffer);
                kernel.SetBuffer(midIdealGatherIndex, "MidInk", underlayInkBuffer);
            }

            if (generateBug)
            {
                kernel.SetBuffer(bugIdealIndex, "Pixels", pixelsBuffer);
                kernel.SetBuffer(bugIdealIndex, "ColorDistances", colorDistancesBuffer);
                kernel.SetBuffer(bugIdealIndex, "BitmapColors", bitmapColorsBuffer);
                kernel.SetBuffer(bugIdealIndex, "BugColors", bugColorsBuffer);
            }

            if (instant)
            {
                if (generateMid)
                {
                    kernel.Dispatch(midIdealScatterIndex, 1, 10, 16);
                    kernel.Dispatch(midIdealGatherIndex, 1, 10, 1);
                }
                if (generateBug)
                {
                    kernel.Dispatch(bugIdealIndex, 4, 1, 1);
                }
                if (generateMid)
                {
                    var tempUnderlayColors = new int[underlayColors.Length];
                    underlayColorsBuffer.GetData(tempUnderlayColors);
                    tempUnderlayColors.AsSpan().CopyTo(underlayColors);
                    //AsyncGPUReadback.RequestIntoNativeArray(ref underlayColors, underlayColorsBuffer).WaitForCompletion();
                }
                if (generateBug)
                {
                    var tempBugColors = new int[referenceBugColors.Length];
                    bugColorsBuffer.GetData(tempBugColors);
                    tempBugColors.AsSpan().CopyTo(referenceBugColors);
                    //AsyncGPUReadback.RequestIntoNativeArray(ref referenceBugColors, bugColorsBuffer).WaitForCompletion();
                }
                var tempBitmapColors = new int[bitmapColors.Length];
                bitmapColorsBuffer.GetData(tempBitmapColors);
                tempBitmapColors.AsSpan().CopyTo(bitmapColors);
                //AsyncGPUReadback.RequestIntoNativeArray(ref bitmapColors, bitmapColorsBuffer).WaitForCompletion();
            }
            else
            {
                var computeCommands = new CommandBuffer();
                computeCommands.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
                if (generateMid)
                {
                    computeCommands.DispatchCompute(kernel, midIdealScatterIndex, 1, 10, 16);
                    computeCommands.DispatchCompute(kernel, midIdealGatherIndex, 1, 10, 1);
                }
                if (generateBug)
                {
                    computeCommands.DispatchCompute(kernel, bugIdealIndex, 4, 1, 1);
                }
                var fence = computeCommands.CreateGraphicsFence(GraphicsFenceType.CPUSynchronisation, SynchronisationStageFlags.ComputeProcessing);
                Graphics.ExecuteCommandBufferAsync(computeCommands, ComputeQueueType.Background);

                while (!fence.passed)
                {
                    yield return null;
                }

                var underlayColorsRequest = AsyncGPUReadback.Request(underlayColorsBuffer);
                var bugColorsRequest = AsyncGPUReadback.Request(bugColorsBuffer);
                var bitmapColorsRequest = AsyncGPUReadback.Request(bitmapColorsBuffer);

                while (!underlayColorsRequest.done || !bugColorsRequest.done || !bitmapColorsRequest.done)
                {
                    yield return null;
                }

                if (generateMid)
                {
                    underlayColorsRequest.GetData<int>().CopyTo(underlayColors);
                }
                if (generateBug)
                {
                    bugColorsRequest.GetData<int>().CopyTo(referenceBugColors);
                }
                bitmapColorsRequest.GetData<int>().CopyTo(bitmapColors);

                computeCommands.Dispose();
            }

            pixelsBuffer.Dispose();
            colorDistancesBuffer.Dispose();
            bitmapColorsBuffer.Dispose();
            underlayColorsBuffer.Dispose();
            underlayDeltasBuffer.Dispose();
            underlayPaperBuffer.Dispose();
            underlayInkBuffer.Dispose();
            bugColorsBuffer.Dispose();
        }

        for (var y = 0; y < AttributeHeight; y++)
        {
            bitmapColors[y * AttributeWidth] = 0xff;
            bitmapColors[y * AttributeWidth + 1] = 0xff;
            bitmapColors[y * AttributeWidth + 2] = 0xff;
        }

        if (generateBug)
        {
            OptimiseReferenceBugColors();
        }

        if (generateRight)
        {
            var rightAttributes = new RightAttributesJob
            {
                ColorDistances = colorDistancesArray,
                Pixels = referencePixels,
                BitmapColors = bitmapColors,
            };

            rightAttributes.Schedule(AttributeHeight, 0x10).Complete();
        }

        var midStartBlock = MidStartX >> 3;
        var midEndBlock = MidEndX >> 3;
        for (var y = 0; y < AttributeHeight; y++)
        {
            if (!generateBug)
            {
                for (var x = 0; x < midStartBlock; x++)
                {
                    bitmapColors[y * AttributeWidth + x] = prevBitmapColors[y * AttributeWidth + x];
                }
            }
            if (!generateMid)
            {
                for (var x = midStartBlock; x < midEndBlock; x++)
                {
                    bitmapColors[y * AttributeWidth + x] = prevBitmapColors[y * AttributeWidth + x];
                }
            }
            if (!generateRight)
            {
                for (var x = midEndBlock; x < AttributeWidth; x++)
                {
                    bitmapColors[y * AttributeWidth + x] = prevBitmapColors[y * AttributeWidth + x];
                }
            }
        }

        colorDistancesArray.Dispose();
    }

    // Rearrange reference bug colours among slots so they are set with the fewest register updates possible
    private void OptimiseReferenceBugColors()
    {
        var bugColors = BugColors;
        var bitmapColors = BitmapColors;
        ReferenceBugColors.AsSpan().CopyTo(bugColors);

        // Determine for each section how soon each colour is going to be needed when going downwards
        var nextUsedRows = new int[AttributeHeight * PaletteSize];
        for (var i = 0; i < nextUsedRows.Length; i++)
        {
            nextUsedRows[i] = AttributeHeight;
        }
        for (var y = AttributeHeight - 1; y >= 0; y--)
        {
            var bix = y * BugColorSlots;
            var rix = y * PaletteSize;
            if (y < AttributeHeight - 1)
            {
                for (var i = 0; i < PaletteSize; i++)
                {
                    nextUsedRows[rix + i] = nextUsedRows[rix + PaletteSize + i];
                }
            }
            for (var i = 0; i < BugColorSlots; i++)
            {
                var c = bugColors[bix + i];
                if (c < 0)
                {
                    continue;
                }
                nextUsedRows[rix + c] = y;
            }
        }

        // Shuffle multicolour components so any common colours between subsequent sections stay in the same slot, and new colours override the least reusable slots
        var bugRow = new int[] { -1, -1, -1 };
        var nextUses = new int[bugRow.Length];
        for (var y = 0; y < AttributeHeight; y++)
        {
            var bix = y * BugColorSlots + 1;
            // Make a note of how soon each current colour will be needed again, and mark unneeded colours
            for (var i = 0; i < bugRow.Length; i++)
            {
                var c = bugRow[i];
                var stillNeeded = c == bugColors[bix] || c == bugColors[bix + 1] || c == bugColors[bix + 2];
                nextUses[i] = c < 0 ? AttributeHeight : stillNeeded ? -1 : nextUsedRows[y * PaletteSize + c];
                if (nextUses[i] >= 0)
                {
                    bugRow[i] = UnusedColor(bugRow[i]);
                }
            }
            // Go through the set of colours and allocate new ones to the slots that contain the least likely needed colours at the moment
            for (var i = 0; i < bugRow.Length; i++)
            {
                var c = bugColors[bix + i];
                if (c < 0 || c == bugRow[0] || c == bugRow[1] || c == bugRow[2])
                {
                    // Nothing to do with unused or already contained colours
                    continue;
                }
                var freeIndex = Array.IndexOf(nextUses, nextUses.Max());
                bugRow[freeIndex] = c;
                // Making sure that we don't allocate this slot again in this loop
                nextUses[freeIndex] = -2;
            }
            // Finalise the row
            for (var i = 0; i < bugRow.Length; i++)
            {
                bugColors[bix + i] = bugRow[i];
            }
        }

        // Fill the unused slots with the next needed colour
        for (var i = bugColors.Length - 1 - BugColorSlots; i >= 0; i--)
        {
            if (bugColors[i] < 0)
            {
                bugColors[i] = UnusedColor(bugColors[i + BugColorSlots]);
            }
        }

        // Rearrange bug colour slots such that if a multicolour component is swapped with the hires one, their indices stay fixed
        var bugColorsWithFixedSwaps = new int[bugColors.Length];
        var indices = new int[BugColorSlots];
        for (var i = 0; i < BugColorSlots; i++)
        {
            bugColorsWithFixedSwaps[i] = bugColors[i];
            indices[i] = i;
        }
        for (var y = 1; y < AttributeHeight; y++)
        {
            var bix = y * BugColorSlots;
            var bixPrev = bix - BugColorSlots;
            var ch = bugColors[bix];
            var chPrev = bugColors[bixPrev];
            if (ch != chPrev)
            {
                var cmiPrev = -1;
                for (var i = 1; i < BugColorSlots; i++)
                {
                    if (bugColors[bixPrev + indices[i]] == ch)
                    {
                        cmiPrev = i;
                        break;
                    }
                }
                if (cmiPrev > 0)
                {
                    var cmi = -1;
                    for (var j = 1; j < BugColorSlots; j++)
                    {
                        if (bugColors[bix + indices[j]] == chPrev)
                        {
                            cmi = j;
                            break;
                        }

                    }
                    if (cmi > 0 && cmi != cmiPrev)
                    {
                        var idx = indices[cmi];
                        indices[cmi] = indices[cmiPrev];
                        indices[cmiPrev] = idx;
                    }
                }
            }
            for (var j = 0; j < BugColorSlots; j++)
            {
                bugColorsWithFixedSwaps[bix + j] = bugColors[bix + indices[j]];
            }
        }
        bugColorsWithFixedSwaps.AsSpan().CopyTo(bugColors);

        // Set attributes in the bug area to remove register pressure
        for (var y = 4; y < AttributeHeight; y += 4)
        {
            var bix = y * BugColorSlots;
            var aix = y * AttributeWidth;
            var six = (y << 1) * ScreenWidth;

            bitmapColors[aix + 0] = 0xff;
            bitmapColors[aix + 1] = 0xff;
            bitmapColors[aix + 2] = 0xff;

            // If there are no light grey pixels, we can use the ink as well to replace a colour
            var freeColors = 2;
            for (var x = 0; x < SpriteWidth; x++)
            {
                if (ReferencePixels[six + x] == 0xf || ReferencePixels[six + ScreenWidth + x] == 0xf)
                {
                    freeColors = 1;
                    break;
                }
            }

            var replacements = 0;

            // If a colour appears for just one section within a longer run, we can replace it and get rid of two changes
            while (replacements < freeColors)
            {
                var replaced = false;
                for (var i = 1 - replacements; i < BugColorSlots; i++)
                {
                    var cur = bugColors[bix + i];
                    if (cur < 0)
                    {
                        continue;
                    }
                    var next = bugColors[bix + i + BugColorSlots];
                    if (cur == next || next < 0)
                    {
                        continue;
                    }
                    var prev = bugColors[bix + i - BugColorSlots];
                    if (prev == next)
                    {
                        bugColors[bix + i] = prev;
                        var c = replacements == 0 ? cur | 0xf0 : (bitmapColors[aix] & 0xf) | (cur << 4);
                        bitmapColors[aix + 0] = c;
                        bitmapColors[aix + 1] = c;
                        bitmapColors[aix + 2] = c;
                        replacements++;
                        replaced = true;
                        break;
                    }
                }
                if (!replaced)
                {
                    break;
                }
            };

            // If a colour has its last use in this section, it can be removed and set to its next value
            while (replacements < freeColors)
            {
                var replaced = false;
                for (var i = 1 - replacements; i < BugColorSlots; i++)
                {
                    var cur = bugColors[bix + i];
                    if (cur < 0)
                    {
                        continue;
                    }
                    var next = bugColors[bix + i + BugColorSlots];
                    if (next == cur)
                    {
                        continue;
                    }
                    bugColors[bix + i] = next;
                    var c = replacements == 0 ? cur | 0xf0 : (bitmapColors[aix] & 0xf) | (cur << 4);
                    bitmapColors[aix + 0] = c;
                    bitmapColors[aix + 1] = c;
                    bitmapColors[aix + 2] = c;
                    replacements++;
                    replaced = true;
                    break;
                }
                if (!replaced)
                {
                    break;
                }
            };
        }

        // Set unused colours to the next one in the same slot
        for (var i = bugColors.Length - 1 - BugColorSlots; i >= 0; i--)
        {
            if (bugColors[i] < 0)
            {
                bugColors[i] = UnusedColor(bugColors[i + BugColorSlots]);
            }
        }
    }

    public static int UnusedColor(int color) => (color & 0xf) - PaletteSize;

    [BurstCompile]
    struct RightAttributesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ColorDistances;
        [ReadOnly] public NativeArray<int> Pixels;

        [NativeDisableParallelForRestriction][WriteOnly] public NativeArray<int> BitmapColors;

        public void Execute(int index)
        {
            var bestD = int.MaxValue;
            var bestAttr = 0;
            var pix = (index << 1) * ScreenWidth + ScreenWidth - 8;

            for (var attr = 0; attr < 0x100; attr++)
            {
                var ink = attr >> 4;
                var paper = attr & 0xf;
                var d = 0;
                for (var y = 0; y < 2; y++)
                {
                    for (var x = 0; x < 8; x++)
                    {
                        var ofs = Pixels[pix + y * ScreenWidth + x] << 4;
                        d += min(ColorDistances[ofs + ink], ColorDistances[ofs + paper]);
                    }
                }
                if (d < bestD)
                {
                    bestD = d;
                    bestAttr = attr;
                }
            }

            BitmapColors[index * AttributeWidth + AttributeWidth - 1] = bestAttr;
        }
    }

    public void GenerateLayers()
    {
        var colorDistancesArray = Palette.GetColorDistancesArray();
        var leftBugIndicesArray = new NativeArray<int>(RenderBugJob.LeftPixelIndices, Allocator.TempJob);
        var rightBugIndicesArray = new NativeArray<int>(RenderBugJob.RightPixelIndices, Allocator.TempJob);

        var renderMid = new RenderMidJob
        {
            ColorDistances = colorDistancesArray,
            Pixels = ReferencePixels,
            BitmapColors = BitmapColors,
            UnderlayColors = UnderlayColors,
            Bitmap = Bitmap,
            Underlay = Underlay,
        };

        var renderBug = new RenderBugJob
        {
            ColorDistances = colorDistancesArray,
            LeftIndices = leftBugIndicesArray,
            RightIndices = rightBugIndicesArray,
            Pixels = ReferencePixels,
            BitmapColors = BitmapColors,
            BugColors = BugColors,
            Bitmap = Bitmap,
            Bug = Bug,
        };

        var renderRight = new RenderRightJob
        {
            ColorDistances = colorDistancesArray,
            Pixels = ReferencePixels,
            BitmapColors = BitmapColors,
            Bitmap = Bitmap,
        };

        var mid = renderMid.Schedule(UnderlayColumns * ScreenHeight, 0x40);
        var bug = renderBug.Schedule(AttributeHeight, 0x10);
        var right = renderRight.Schedule(ScreenHeight, 0x10);
        JobHandle.ScheduleBatchedJobs();
        mid.Complete();
        bug.Complete();
        right.Complete();

        for (var y = 0; y < ScreenHeight; y++)
        {
            for (var c = 0; c < UnderlayColumns; c++)
            {
                var x = c * (SpriteWidth << 1) + SpriteWidth;
                UpdateUnderlayColorUse(x, y);
            }
        }

        colorDistancesArray.Dispose();
        leftBugIndicesArray.Dispose();
        rightBugIndicesArray.Dispose();
    }

    public void Render(ref Texture2D target, bool showInkLayer, bool showLoresSpriteLayer, bool showHiresSpriteLayer, bool showPaperLayer)
    {
        if (target == null)
        {
            target = new Texture2D(ScreenWidth, ScreenHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
        }
        var pixels = target.GetRawTextureData<Color32>();
        var paletteArray = new NativeArray<Color32>(Palette.Colors.ToArray(), Allocator.TempJob);
        var indices = new NativeArray<int>(pixels.Length, Allocator.TempJob);

        var renderLayers = new RenderLayersJob
        {
            ShowInk = showInkLayer,
            ShowLoresSprite = showLoresSpriteLayer,
            ShowHiresSprite = showHiresSpriteLayer,
            ShowPaper = showPaperLayer,
            Palette = paletteArray,
            Bitmap = Bitmap,
            BitmapColors = BitmapColors,
            Underlay = Underlay,
            UnderlayColors = UnderlayColors,
            Bug = Bug,
            BugColors = BugColors,
            OutputPixels = pixels,
            Indices = indices,
        };

        renderLayers.Schedule(ScreenWidth * ScreenHeight, ScreenWidth).Complete();

        paletteArray.Dispose();
        indices.Dispose();

        target.Apply();
    }

    public void RenderLayers(NativeArray<int> result)
    {
        var pixels = new NativeArray<Color32>(ReferencePixels.Length, Allocator.TempJob);
        var paletteArray = new NativeArray<Color32>(Palette.Colors.ToArray(), Allocator.TempJob);

        var renderLayers = new RenderLayersJob
        {
            ShowInk = true,
            ShowLoresSprite = true,
            ShowHiresSprite = true,
            ShowPaper = true,
            Palette = paletteArray,
            Bitmap = Bitmap,
            BitmapColors = BitmapColors,
            Underlay = Underlay,
            UnderlayColors = UnderlayColors,
            Bug = Bug,
            BugColors = BugColors,
            OutputPixels = pixels,
            Indices = result,
        };

        renderLayers.Schedule(ScreenWidth * ScreenHeight, ScreenWidth).Complete();

        pixels.Dispose();
        paletteArray.Dispose();
    }

    [BurstCompile]
    struct RenderLayersJob : IJobParallelFor
    {
        public bool ShowInk;
        public bool ShowLoresSprite;
        public bool ShowHiresSprite;
        public bool ShowPaper;

        [ReadOnly] public NativeArray<Color32> Palette;
        [ReadOnly] public NativeArray<bool> Bitmap;
        [ReadOnly] public NativeArray<int> BitmapColors;
        [ReadOnly] public NativeArray<bool> Underlay;
        [ReadOnly] public NativeArray<int> UnderlayColors;
        [ReadOnly] public NativeArray<bool> Bug;
        [ReadOnly] public NativeArray<int> BugColors;

        [NativeDisableParallelForRestriction][WriteOnly] public NativeArray<Color32> OutputPixels;
        [WriteOnly] public NativeArray<int> Indices;

        public void Execute(int index)
        {
            var px = index % ScreenWidth;
            var py = index / ScreenWidth;
            var col = GetPixel(px, py);
            Indices[index] = col;
            OutputPixels[(ScreenHeight - 1 - py) * ScreenWidth + px] = col < 0 ? new Color32() : Palette[col];
        }

        public int GetPixel(int px, int py)
        {
            var by = py >> 1;
            var bc = BitmapColors[by * AttributeWidth + (px >> 3)];
            var bp = Bitmap[py * ScreenWidth + px];
            if (px < MidStartX)
            {
                var hc = BugColors[by * BugColorSlots];
                var hp = Bug[py * BugSprites * SpriteWidth + px];
                var mpix = py * BugSprites * SpriteWidth + SpriteWidth + (px & ~1);
                var mp = (Bug[mpix] ? 2 : 0) | (Bug[mpix + 1] ? 1 : 0);
                var mc = BugColors[by * BugColorSlots + mp];
                return bp & ShowInk ? bc >> 4 : hp & ShowHiresSprite ? hc : (mp > 0) && ShowLoresSprite ? mc : ShowPaper ? bc & 0xf : -1;
            }
            else if (px < MidEndX)
            {
                var uy = py < ScreenHeight - 1 && (py & 1) == 1 && px >= MidEndX - 8 ? py + 1 : py;
                var uc = UnderlayColors[uy * UnderlayColumns + ((px >> 3) - BugBlockWidth) / UnderlayBlockWidth];
                var up = Underlay[py * UnderlayColumns * SpriteWidth + ((px - SpriteWidth) >> 1)];
                return bp & ShowInk ? bc >> 4 : up & ShowLoresSprite ? uc : ShowPaper ? bc & 0xf : -1;
            }
            else
            {
                return bp & ShowInk ? bc >> 4 : ShowPaper ? bc & 0xf : -1;
            }
        }
    }

    public ImageSection GetSection(int px, int py)
    {
        if (px < 0 || px >= ScreenWidth) return ImageSection.SideBorder;
        if (py < 0) return ImageSection.TopBorder;
        if (py >= ScreenHeight) return ImageSection.BottomBorder;
        if (px < MidStartX) return ImageSection.Bug;
        if (px >= MidEndX) return ImageSection.Right;
        return ImageSection.Mid;
    }

    public void RenderBugColors(ref Texture2D target, bool clearUnusedEntries)
    {
        if (target == null)
        {
            target = new Texture2D(BugColorSlots, AttributeHeight * 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
        }
        var pixels = new Color32[BugColorSlots * AttributeHeight * 2];
        for (var y = 0; y < AttributeHeight; y++)
        {
            for (var s = 0; s < BugColorSlots; s++)
            {
                var c = BugColors[y * BugColorSlots + s];
                var col = Palette.Colors[c & 0xf];
                if (clearUnusedEntries && c < 0)
                {
                    col.a = 127;
                }
                pixels[(ScreenHeight - 2 - y * 2) * BugColorSlots + s] = col;
                pixels[(ScreenHeight - 1 - y * 2) * BugColorSlots + s] = col;
            }
        }
        target.SetPixels32(pixels);
        target.Apply();
    }

    public void RenderMidColors(ref Texture2D target, bool clearUnusedEntries)
    {
        if (target == null)
        {
            target = new Texture2D(UnderlayColumns, ScreenHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
        }
        var pixels = new Color32[UnderlayColumns * ScreenHeight];
        for (var y = 0; y < ScreenHeight; y++)
        {
            for (var c = 0; c < UnderlayColumns; c++)
            {
                var ix = UnderlayColors[y * UnderlayColumns + c];
                var col = Palette.Colors[ix & 0xf];
                if (clearUnusedEntries && ix < 0)
                {
                    col.a = 127;
                }
                pixels[(ScreenHeight - 1 - y) * UnderlayColumns + c] = col;
            }
        }
        target.SetPixels32(pixels);
        target.Apply();
    }

    public void RenderBorderColors(ref Texture2D target)
    {
        if (target == null)
        {
            target = new Texture2D(1, AttributeHeight + 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
        }
        var pixels = new Color32[AttributeHeight + 1];
        for (var y = 0; y <= AttributeHeight; y++)
        {
            pixels[AttributeHeight - y] = Palette.Colors[BorderColors[y]];
        }
        target.SetPixels32(pixels);
        target.Apply();
    }

    public unsafe int GetBugSectionDistance(int sectionY, int ch, int cm1, int cm2, int cm3)
    {
        var result = 0;
        var pix = (sectionY << 1) * ScreenWidth;
        var aix = sectionY * AttributeWidth;
        var bcix = sectionY * BugColorSlots;
        var cols = stackalloc int[6];
        cols[2] = ch; // Hires
        cols[3] = cm1; // Multi 1
        cols[4] = cm2; // Multi 2
        cols[5] = cm3; // Multi 3

        for (var y = 0; y < 2; y++)
        {
            var piy = y * ScreenWidth;
            var biy = y * BugSprites * SpriteWidth;
            for (var x = 0; x < SpriteWidth; x += 2)
            {
                var attr = BitmapColors[aix + (x >> 3)];
                cols[0] = attr & 0xf; // Paper
                cols[1] = attr >> 4; // Ink
                var ds1 = Palette.Distances[ReferencePixels[pix + piy + x]];
                var ds2 = Palette.Distances[ReferencePixels[pix + piy + x + 1]];
                var bestD = int.MaxValue;
                for (var pat = 0; pat < 24; pat++)
                {
                    var c1 = cols[RenderBugJob.LeftPixelIndices[pat]];
                    var c2 = cols[RenderBugJob.RightPixelIndices[pat]];
                    if (c1 < 0 || c2 < 0)
                    {
                        continue;
                    }
                    var d = ds1[c1] + ds2[c2];
                    if (d < bestD)
                    {
                        bestD = d;
                    }
                }
                result += bestD;
            }
        }
        return result;
    }

    public int GetMidSectionDistance(int screenY, int column, int underlay)
    {
        var result = 0;
        var cx = column * UnderlayBlockWidth + BugBlockWidth;
        var sx = cx << 3;
        var six = screenY * ScreenWidth + sx;
        var bix = (screenY >> 1) * AttributeWidth + cx;

        for (var x = 0; x < SpriteWidth; x++)
        {
            var attr = BitmapColors[bix + (x >> 2)];
            var ink = attr >> 4;
            var paper = attr & 0xf;
            var ds1 = Palette.Distances[ReferencePixels[six++]];
            var ds2 = Palette.Distances[ReferencePixels[six++]];
            var bestD = int.MaxValue;
            for (var pat = 0; pat < 7; pat++)
            {
                var bkg = pat < 4 ? paper : underlay & 0xf;
                var b1 = (pat & 1) == 0 ? bkg : ink;
                var b2 = (pat & 2) == 0 ? bkg : ink;
                var d = ds1[b1] + ds2[b2] + pat;
                if (d < bestD)
                {
                    bestD = d;
                }
            }
            result += bestD;
        }
        return result;
    }

    public void ReoptimiseForPixel(int x, int y)
    {
        switch (GetSection(x, y))
        {
            case ImageSection.Bug:
                // Should be equivalent to GenerateColors(false, true, false);
                ReoptimiseForBugPixel(y);
                /*
                var bcs = new List<int>(BugColors);
                GenerateColors(false, true, false);
                for (var i = 0; i < BugColors.Length; i++)
                {
                    if (BugColors[i] != bcs[i])
                    {
                        Debug.Log($"Difference at {i}");
                        break;
                    }
                }
                */
                break;
            case ImageSection.Mid:
                // Should be equivalent to GenerateColors(true, false, false);
                ReoptimiseForMidPixel(x, y);
                break;
            case ImageSection.Right:
                GenerateColors(false, false, true);
                break;
        }
    }

    private void ReoptimiseForBugPixel(int y)
    {
        var sy = y >> 1;

        const int combos = 0x10000;
        var colorDistancesArray = Palette.GetColorDistancesArray();
        var deltasArray = new NativeArray<int>(combos, Allocator.TempJob);

        var optimise = new OptimiseBugJob
        {
            ColorDistances = colorDistancesArray,
            Pixels = ReferencePixels,
            SectionY = sy,
            Deltas = deltasArray
        };

        optimise.Schedule(combos, 0x100).Complete();

        var dmin = int.MaxValue;
        var bestCombo = 0;
        for (var i = 0; i < combos; i++)
        {
            if (deltasArray[i] < dmin)
            {
                dmin = deltasArray[i];
                bestCombo = i;
            }
        }

        colorDistancesArray.Dispose();
        deltasArray.Dispose();

        var bix = sy * BugColorSlots;
        ReferenceBugColors[bix + 0] = ((bestCombo >> 0) & 0xf) - 1;
        ReferenceBugColors[bix + 1] = ((bestCombo >> 4) & 0xf) - 1;
        ReferenceBugColors[bix + 2] = ((bestCombo >> 8) & 0xf) - 1;
        ReferenceBugColors[bix + 3] = ((bestCombo >> 12) & 0xf) - 1;

        OptimiseReferenceBugColors();
    }

    private void ReoptimiseForMidPixel(int x, int y)
    {
        var sx = ((x >> 3) - BugBlockWidth) / UnderlayBlockWidth;
        var sy = y >> 1;

        const int underlayCombos = 0x100;
        var colorDistancesArray = Palette.GetColorDistancesArray();
        var deltasArray = new NativeArray<int>(underlayCombos, Allocator.TempJob);
        var paperColorsArray = new NativeArray<int>(underlayCombos, Allocator.TempJob);
        var inkColorsArray = new NativeArray<int>(underlayCombos, Allocator.TempJob);

        var optimise = new OptimiseMidJob
        {
            ColorDistances = colorDistancesArray,
            Pixels = ReferencePixels,
            SectionX = sx,
            SectionY = sy,
            Deltas = deltasArray,
            PaperColors = paperColorsArray,
            InkColors = inkColorsArray
        };

        optimise.Schedule(underlayCombos, 0x10).Complete();

        var dmin = int.MaxValue;
        var underlays = 0;
        var paper = 0;
        var ink = 0;
        for (var i = 0; i < underlayCombos; i++)
        {
            if (deltasArray[i] < dmin)
            {
                dmin = deltasArray[i];
                underlays = i;
                paper = paperColorsArray[i];
                ink = inkColorsArray[i];
            }
        }

        UnderlayColors[(sy << 1) * UnderlayColumns + sx] = underlays & 0xf;
        UnderlayColors[((sy << 1) + 1) * UnderlayColumns + sx] = underlays >> 4;
        var cx = sx * UnderlayBlockWidth + BugBlockWidth;
        for (var i = 0; i < UnderlayBlockWidth; i++)
        {
            BitmapColors[sy * AttributeWidth + cx + i] = (((ink >> (i << 2)) & 0xf) << 4) | ((paper >> (i << 2)) & 0xf);
        }

        colorDistancesArray.Dispose();
        deltasArray.Dispose();
        paperColorsArray.Dispose();
        inkColorsArray.Dispose();
    }

    public void SetBorderColor(int x, int y, int color, bool floodFill)
    {
        if (x < 0 || x >= ScreenWidth)
        {
            var i = clamp((y + (x < 0 ? 0 : 1)) >> 1, 0, AttributeHeight);
            var oldColor = BorderColors[i];
            BorderColors[i] = color;
            if (floodFill)
            {
                for (var j = i - 1; j >= 0 && BorderColors[j] == oldColor; j--)
                {
                    BorderColors[j] = color;
                }
                for (var j = i + 1; j <= AttributeHeight && BorderColors[j] == oldColor; j++)
                {
                    BorderColors[j] = color;
                }
            }
            return;
        }
        if (y < 0)
        {
            TopBackgroundColor = color;
            return;
        }
        if (y >= ScreenHeight)
        {
            BottomBackgroundColor = color;
        }
    }

    public void SetPixel(Pen layer, int x, int y, bool value)
    {
        switch (layer)
        {
            case Pen.Ink:
                Bitmap[y * ScreenWidth + x] = value;
                break;
            case Pen.Sprite:
                if (x >= ScreenWidth - 8)
                {
                    return;
                }
                if (x < SpriteWidth)
                {
                    Bug[y * BugSprites * SpriteWidth + x] = value;
                    UpdateBugColorUse(y >> 1);
                }
                else
                {
                    Underlay[y * UnderlayColumns * SpriteWidth + ((x - SpriteWidth) >> 1)] = value;
                    UpdateUnderlayColorUse(x, y);
                }
                break;
            case Pen.Multi1:
            case Pen.Multi2:
            case Pen.Multi3:
                if (x >= SpriteWidth)
                {
                    return;
                }
                var leftValue = value && layer != Pen.Multi1;
                var rightValue = value && layer != Pen.Multi2;
                Bug[y * BugSprites * SpriteWidth + SpriteWidth + (x & ~1)] = leftValue;
                Bug[y * BugSprites * SpriteWidth + SpriteWidth + (x | 1)] = rightValue;
                UpdateBugColorUse(y >> 1);
                break;
            case Pen.Paper:
                if (value)
                {
                    SetPixel(Pen.Ink, x, y, false);
                    SetPixel(Pen.Sprite, x, y, false);
                    SetPixel(Pen.Multi1, x, y, false);
                }
                break;
        }
    }

    public int GetPixel(int x, int y, bool showInkLayer, bool showLoresSpriteLayer, bool showHiresSpriteLayer, bool showPaperLayer)
    {
        return new RenderLayersJob
        {
            ShowInk = showInkLayer,
            ShowLoresSprite = showLoresSpriteLayer,
            ShowHiresSprite = showHiresSpriteLayer,
            ShowPaper = showPaperLayer,
            Bitmap = Bitmap,
            BitmapColors = BitmapColors,
            Underlay = Underlay,
            UnderlayColors = UnderlayColors,
            Bug = Bug,
            BugColors = BugColors,
        }.GetPixel(x, y);
    }

    public void GetBugColorsUsed(int y, bool[] result)
    {
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = false;
        }
        var bix1 = (y << 1) * BugSprites * SpriteWidth;
        var bix2 = bix1 + BugSprites * SpriteWidth;
        for (var i = 0; i < SpriteWidth; i += 2)
        {
            result[(Bug[bix1 + SpriteWidth + i] ? 2 : 0) | (Bug[bix1 + SpriteWidth + i + 1] ? 1 : 0)] = true;
            result[(Bug[bix2 + SpriteWidth + i] ? 2 : 0) | (Bug[bix2 + SpriteWidth + i + 1] ? 1 : 0)] = true;
        }
        var hiresBugColorUsed = false;
        for (var i = 0; i < SpriteWidth; i++)
        {
            hiresBugColorUsed |= Bug[bix1 + i];
            hiresBugColorUsed |= Bug[bix2 + i];
        }
        result[0] = hiresBugColorUsed;
    }

    private void UpdateBugColorUse(int y)
    {
        var bugColorsUsed = new bool[BugColorSlots];
        GetBugColorsUsed(y, bugColorsUsed);
        for (var i = 0; i < BugColorSlots; i++)
        {
            if (bugColorsUsed[i])
            {
                BugColors[y * BugColorSlots + i] &= 0xf;
            }
            else
            {
                BugColors[y * BugColorSlots + i] = UnusedColor(BugColors[y * BugColorSlots + i]);
            }
        }
    }

    private void UpdateUnderlayColorUse(int x, int y)
    {
        const int extraPixelsOffset = -(UnderlayColumns * SpriteWidth + 4);
        var column = (x - SpriteWidth) / (SpriteWidth << 1);
        var cy = y + (x >= MidEndX - 8 && y != 0 && y != ScreenHeight - 1 ? y & 1 : 0);
        var cix = cy * UnderlayColumns + column;
        var uix = cix * SpriteWidth;
        var width = column < 5 || y == 0 || y == ScreenHeight - 1 ? SpriteWidth : (cy & 1) == 1 ? SpriteWidth - 4 : SpriteWidth + 4;
        var used = false;
        for (var i = 0; i < width; i++)
        {
            var ofs = i < SpriteWidth ? i : i + extraPixelsOffset;
            used |= Underlay[uix + ofs];
        }
        if (used)
        {
            UnderlayColors[cix] &= 0xf;
        }
        else
        {
            UnderlayColors[cix] = UnusedColor(UnderlayColors[cix]);
        }
    }

    public void BackportLayerChangesToReference()
    {
        var pixelCount = ReferencePixels.Length;
        var tempImage = Clone();
        var originalLayers = new NativeArray<int>(pixelCount, Allocator.TempJob);
        var editedLayers = new NativeArray<int>(pixelCount, Allocator.TempJob);
        tempImage.RenderLayers(editedLayers);
        tempImage.GenerateColors(true, true, true);
        tempImage.GenerateLayers();
        tempImage.RenderLayers(originalLayers);
        for (var i = 0; i < pixelCount; i++)
        {
            var edited = editedLayers[i];
            if (edited != originalLayers[i])
            {
                ReferencePixels[i] = edited;
            }
        }
        originalLayers.Dispose();
        editedLayers.Dispose();
    }

    public LayeredImage Clone()
    {
        var result = new LayeredImage(ReferencePixels.ToArray(), Palette);
        // Equivalent to result.Read(Write()), just much faster
        Bitmap.AsSpan().CopyTo(result.Bitmap);
        Underlay.AsSpan().CopyTo(result.Underlay);
        Bug.AsSpan().CopyTo(result.Bug);
        BitmapColors.AsSpan().CopyTo(result.BitmapColors);
        UnderlayColors.AsSpan().CopyTo(result.UnderlayColors);
        BugColors.AsSpan().CopyTo(result.BugColors);
        ReferenceBugColors.AsSpan().CopyTo(result.ReferenceBugColors);
        BorderColors.AsSpan().CopyTo(result.BorderColors);
        result.TopBackgroundColor = TopBackgroundColor;
        result.BottomBackgroundColor = BottomBackgroundColor;
        return result;
    }

    public void Read(IList<byte> bytes)
    {
        var i = 5;
        DirectlyEditing = bytes[i++] != 0;
        for (var j = 0; j < ReferencePixels.Length; j++)
        {
            ReferencePixels[j] = bytes[i++];
        }
        for (var j = 0; j < Bitmap.Length; j++)
        {
            Bitmap[j] = bytes[i++] != 0;
        }
        for (var j = 0; j < Underlay.Length; j++)
        {
            Underlay[j] = bytes[i++] != 0;
        }
        for (var j = 0; j < Bug.Length; j++)
        {
            Bug[j] = bytes[i++] != 0;
        }
        for (var j = 0; j < BitmapColors.Length; j++)
        {
            BitmapColors[j] = bytes[i++];
        }
        for (var j = 0; j < UnderlayColors.Length; j++)
        {
            UnderlayColors[j] = (sbyte)bytes[i++];
        }
        for (var j = 0; j < BugColors.Length; j++)
        {
            BugColors[j] = (sbyte)bytes[i++];
        }
        for (var j = 0; j < BugColors.Length; j++)
        {
            ReferenceBugColors[j] = (sbyte)bytes[i++];
        }
        for (var j = 0; j < BorderColors.Length; j++)
        {
            BorderColors[j] = (sbyte)bytes[i++];
        }
        TopBackgroundColor = (sbyte)bytes[i++];
        BottomBackgroundColor = (sbyte)bytes[i++];
    }

    public byte[] Write()
    {
        var result = new List<int>
        {
            'N',
            'F',
            'X',
            'P',
            1, // Version
            DirectlyEditing ? 1 : 0
        };
        result.AddRange(ReferencePixels);
        result.AddRange(Bitmap.Select(v => v ? 1 : 0));
        result.AddRange(Underlay.Select(v => v ? 1 : 0));
        result.AddRange(Bug.Select(v => v ? 1 : 0));
        result.AddRange(BitmapColors);
        result.AddRange(UnderlayColors);
        result.AddRange(BugColors);
        result.AddRange(ReferenceBugColors);
        result.AddRange(BorderColors);
        result.Add(TopBackgroundColor);
        result.Add(BottomBackgroundColor);
        return result.Select(v => (byte)v).ToArray();
    }

    [BurstCompile]
    struct OptimiseMidJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ColorDistances;
        [ReadOnly] public NativeArray<int> Pixels;
        [ReadOnly] public int SectionX;
        [ReadOnly] public int SectionY;

        [WriteOnly] public NativeArray<int> Deltas;
        [WriteOnly] public NativeArray<int> PaperColors;
        [WriteOnly] public NativeArray<int> InkColors;

        public unsafe void Execute(int index)
        {
            var u1 = index & 0xf;
            var u2 = index >> 4;
            var cx = SectionX * UnderlayBlockWidth + BugBlockWidth;
            var six = (SectionY << 1) * ScreenWidth + (cx << 3);
            var cds = ColorDistances.AsReadOnlySpan();
            var totalD = 0;
            var bestPapers = stackalloc int[UnderlayBlockWidth];
            var bestInks = stackalloc int[UnderlayBlockWidth];

            for (var c = 0; c < UnderlayBlockWidth; c++)
            {
                var bestBlockD = int.MaxValue;
                for (var paper = 0; paper < 16; paper++)
                {
                    for (var ink = 0; ink < 16; ink++)
                    {
                        var blockD = 0;
                        for (var y = 0; y < 2; y++)
                        {
                            var underlay = y == 0 ? u1 : u2;
                            for (var x = 0; x < 4; x++)
                            {
                                var pix = six + y * ScreenWidth + (c << 3) + (x << 1);
                                var ds1 = cds.Slice(Pixels[pix] << 4, 16);
                                var ds2 = cds.Slice(Pixels[pix + 1] << 4, 16);
                                var bestD = int.MaxValue;
                                for (var pat = 0; pat < 7; pat++)
                                {
                                    var bkg = pat < 4 ? paper : underlay;
                                    var b1 = (pat & 1) == 0 ? bkg : ink;
                                    var b2 = (pat & 2) == 0 ? bkg : ink;
                                    // The pattern penalty factor can be ANDed with certain numbers for different policies (the default is the last option):
                                    // 0 - no preference, 3 - avoid ink, 4 - avoid underlay, 7 - favour paper, then ink, then underlay
                                    var d = ds1[b1] + ds2[b2] + pat;
                                    if (d < bestD)
                                    {
                                        bestD = d;
                                    }
                                }
                                blockD += bestD;
                            }
                        }
                        if (blockD < bestBlockD)
                        {
                            bestBlockD = blockD;
                            bestPapers[c] = paper;
                            bestInks[c] = ink;
                        }
                    }
                }
                totalD += bestBlockD;
            }

            Deltas[index] = totalD;
            var papers = 0;
            var inks = 0;
            for (var i = 0; i < UnderlayBlockWidth; i++)
            {
                papers |= bestPapers[i] << (i << 2);
                inks |= bestInks[i] << (i << 2);
            }
            PaperColors[index] = papers;
            InkColors[index] = inks;
        }
    }

    [BurstCompile]
    struct RenderMidJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ColorDistances;
        [ReadOnly] public NativeArray<int> Pixels;
        [ReadOnly] public NativeArray<int> BitmapColors;
        [ReadOnly] public NativeArray<int> UnderlayColors;

        [NativeDisableContainerSafetyRestriction][NativeDisableParallelForRestriction][WriteOnly] public NativeArray<bool> Bitmap;
        [NativeDisableContainerSafetyRestriction][NativeDisableParallelForRestriction][WriteOnly] public NativeArray<bool> Underlay;

        public void Execute(int index)
        {
            var y = index / UnderlayColumns;
            var ux = index % UnderlayColumns;
            var cx = ux * UnderlayBlockWidth + BugBlockWidth;
            var sx = cx << 3;
            var uix = index * SpriteWidth;
            var six = y * ScreenWidth + sx;
            var bix = (y >> 1) * AttributeWidth + cx;
            var underlay = UnderlayColors[index];
            var lastUnderlay = y < ScreenHeight - 1 && (y & 1) == 1 && ux == UnderlayColumns - 1 ? UnderlayColors[index + UnderlayColumns] : underlay;
            var patCount = underlay < 0 ? 4 : 7;
            var cds = ColorDistances.AsReadOnlySpan();

            for (var x = 0; x < SpriteWidth; x++)
            {
                if (x == SpriteWidth - 4)
                {
                    underlay = lastUnderlay;
                }
                var attr = BitmapColors[bix + (x >> 2)];
                var ink = attr >> 4;
                var paper = attr & 0xf;
                var pix = six + (x << 1);
                var ds1 = cds.Slice(Pixels[pix] << 4, 16);
                var ds2 = cds.Slice(Pixels[pix + 1] << 4, 16);
                var bestD = int.MaxValue;
                var bestPat = 0;
                for (var pat = 0; pat < patCount; pat++)
                {
                    var bkg = pat < 4 ? paper : underlay & 0xf;
                    var b1 = (pat & 1) == 0 ? bkg : ink;
                    var b2 = (pat & 2) == 0 ? bkg : ink;
                    // The pattern penalty factor can be ANDed with certain numbers for different policies (the default is the last option):
                    // 0 - no preference, 3 - avoid ink, 4 - avoid underlay, 7 - favour paper, then ink, then underlay
                    var d = ds1[b1] + ds2[b2] + pat;
                    if (d < bestD)
                    {
                        bestD = d;
                        bestPat = pat;
                    }
                }
                Bitmap[pix] = (bestPat & 1) != 0;
                Bitmap[pix + 1] = (bestPat & 2) != 0;
                Underlay[uix + x] = (bestPat & 4) != 0;
            }
        }
    }

    [BurstCompile]
    struct OptimiseBugJob : IJobParallelFor
    {
        private static readonly int[] PixL = { 1, 1, 2, 2, 3, 1, 3, 2, 3, 4, 1, 4, 2, 4, 5, 1, 5, 2, 5 };
        private static readonly int[] PixR = { 1, 2, 1, 2, 3, 3, 1, 3, 2, 4, 4, 1, 4, 2, 5, 5, 1, 5, 2 };
        private static readonly int[] Pens = { 0, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4 };

        [ReadOnly] public NativeArray<int> ColorDistances;
        [ReadOnly] public NativeArray<int> Pixels;
        [ReadOnly] public int SectionY;

        [WriteOnly] public NativeArray<int> Deltas;

        public unsafe void Execute(int index)
        {
            var attrY = SectionY;
            var pixelY = SectionY << 1;
            var pixelBase = pixelY * ScreenWidth;
            var cds = ColorDistances.AsReadOnlySpan();
            var cols = stackalloc int[6];
            cols[0] = 0xf; // Paper
            cols[1] = 0xf; // Ink
            cols[2] = ((index >> 0) & 0xf) - 1; // Hires
            cols[3] = ((index >> 4) & 0xf) - 1; // Multi 1
            cols[4] = ((index >> 8) & 0xf) - 1; // Multi 2
            cols[5] = ((index >> 12) & 0xf) - 1; // Multi 3

            Deltas[index] = int.MaxValue;

            if (cols[3] >= 0 && (cols[2] < 0 || cols[3] == cols[2])) return;
            if (cols[4] >= 0 && (cols[3] < 0 || cols[4] == cols[2] || cols[4] <= cols[3])) return;
            if (cols[5] >= 0 && (cols[4] < 0 || cols[5] == cols[2] || cols[5] <= cols[4])) return;

            var curSpanD = 0;
            for (var pi = 0; pi < BugPatternWidth * 2; pi++)
            {
                var bestPatD = int.MaxValue;
                var pix = pi < BugPatternWidth ? pixelBase + (pi << 1) : pixelBase + ((pi - BugPatternWidth) << 1) + ScreenWidth;
                var ds1 = cds.Slice(Pixels[pix] << 4, 16);
                var ds2 = cds.Slice(Pixels[pix + 1] << 4, 16);
                for (var pat = 0; pat < 19; pat++)
                {
                    var b1 = cols[PixL[pat]];
                    var b2 = cols[PixR[pat]];
                    var d = ds1[b1 & 0xf] + ds2[b2 & 0xf] + Pens[pat] - ((min(b1, 0) + min(b2, 0)) << 16);
                    bestPatD = min(bestPatD, d);
                }
                curSpanD += bestPatD;
            }

            Deltas[index] = curSpanD;
        }
    }

    [BurstCompile]
    struct RenderBugJob : IJobParallelFor
    {
        public static readonly int[] LeftPixelIndices = { 1, 1, 2, 2, 0, 1, 0, 2, 0, 3, 1, 3, 2, 3, 4, 1, 4, 2, 4, 5, 1, 5, 2, 5 };
        public static readonly int[] RightPixelIndices = { 1, 2, 1, 2, 0, 0, 1, 0, 2, 3, 3, 1, 3, 2, 4, 4, 1, 4, 2, 5, 5, 1, 5, 2 };

        [ReadOnly] public NativeArray<int> ColorDistances;
        [ReadOnly] public NativeArray<int> LeftIndices;
        [ReadOnly] public NativeArray<int> RightIndices;
        [ReadOnly] public NativeArray<int> Pixels;
        [ReadOnly] public NativeArray<int> BitmapColors;
        [ReadOnly] public NativeArray<int> BugColors;

        [NativeDisableContainerSafetyRestriction][NativeDisableParallelForRestriction][WriteOnly] public NativeArray<bool> Bitmap;
        [NativeDisableContainerSafetyRestriction][NativeDisableParallelForRestriction][WriteOnly] public NativeArray<bool> Bug;

        public unsafe void Execute(int index)
        {
            var pix = (index << 1) * ScreenWidth;
            var bix = (index << 1) * BugSprites * SpriteWidth;
            var aix = index * AttributeWidth;
            var bcix = index * BugColorSlots;
            var cds = ColorDistances.AsReadOnlySpan();
            var cols = stackalloc int[6];
            cols[2] = BugColors[bcix]; // Hires
            cols[3] = BugColors[bcix + 1]; // Multi 1
            cols[4] = BugColors[bcix + 2]; // Multi 2
            cols[5] = BugColors[bcix + 3]; // Multi 3

            for (var y = 0; y < 2; y++)
            {
                var piy = y * ScreenWidth;
                var biy = y * BugSprites * SpriteWidth;
                for (var x = 0; x < SpriteWidth; x += 2)
                {
                    var attr = BitmapColors[aix + (x >> 3)];
                    cols[0] = attr & 0xf; // Paper
                    cols[1] = attr >> 4; // Ink
                    var ds1 = cds.Slice(Pixels[pix + piy + x] << 4, 16);
                    var ds2 = cds.Slice(Pixels[pix + piy + x + 1] << 4, 16);
                    var bestD = int.MaxValue;
                    var bestPat = 0;
                    for (var pat = 0; pat < 24; pat++)
                    {
                        var c1 = cols[LeftIndices[pat]];
                        var c2 = cols[RightIndices[pat]];
                        if (c1 < 0 || c2 < 0)
                        {
                            continue;
                        }
                        var d = ds1[c1] + ds2[c2];
                        if (d < bestD)
                        {
                            bestD = d;
                            bestPat = pat;
                        }
                    }
                    var i1 = LeftIndices[bestPat];
                    var i2 = RightIndices[bestPat];
                    Bitmap[pix + piy + x] = i1 == 1;
                    Bitmap[pix + piy + x + 1] = i2 == 1;
                    Bug[bix + biy + x] = i1 == 2;
                    Bug[bix + biy + x + 1] = i2 == 2;
                    var bkg = max(max(i1, i2) - 2, 0);
                    Bug[bix + biy + SpriteWidth + x] = (bkg & 2) != 0;
                    Bug[bix + biy + SpriteWidth + x + 1] = (bkg & 1) != 0;
                }
            }
        }
    }

    [BurstCompile]
    struct RenderRightJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ColorDistances;
        [ReadOnly] public NativeArray<int> Pixels;
        [ReadOnly] public NativeArray<int> BitmapColors;

        [NativeDisableContainerSafetyRestriction][NativeDisableParallelForRestriction][WriteOnly] public NativeArray<bool> Bitmap;

        public void Execute(int index)
        {
            var pix = index * ScreenWidth + ScreenWidth - 8;
            var attr = BitmapColors[(index >> 1) * AttributeWidth + AttributeWidth - 1];
            var ink = attr >> 4;
            var paper = attr & 0xf;

            for (var x = 0; x < 8; x++)
            {
                var ofs = Pixels[pix + x] << 4;
                Bitmap[pix + x] = ColorDistances[ofs + ink] <= ColorDistances[ofs + paper];
            }
        }
    }
}

public enum ImageSection
{
    Bug, Mid, Right, SideBorder, TopBorder, BottomBorder
}