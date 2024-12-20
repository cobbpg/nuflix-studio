using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using UnityEngine;
using static Constants;
using static Unity.Mathematics.math;

public class CodeGeneration
{
    public const int CodeSizeLimit = 0x1100;

    private LayeredImage _image;

    private LayeredImage Image => _image;

    private NativeArray<int> _bitmapColors;
    private NativeArray<int> _underlayColors;
    private NativeArray<int> _bugColors;

    public List<byte> Code { get; private set; }
    public List<NtscAdjustment> NtscAdjustments { get; private set; }
    public int NtscRtsOffset { get; private set; }
    public List<int> FreeCycles { get; private set; }
    public byte InitX { get; private set; }
    public byte InitY { get; private set; }
    public int BankChangeAddress { get; private set; }
    public bool SpriteMoveFailed { get; private set; }

    public CodeGeneration(LayeredImage image)
    {
        _image = image;
        _bitmapColors = _image.BitmapColors;
        _underlayColors = _image.UnderlayColors;
        _bugColors = _image.BugColors;
    }

    public void Generate(List<RegisterUpdate>[] updates, List<RegisterUpdate> deferredUpdates, Dictionary<int, byte> slowValuesMapping, string logPath = null)
    {
        var log = logPath != null;

        StringBuilder sb = null;
        StringBuilder rsb = null;
        if (log)
        {
            sb = new StringBuilder();
            rsb = new StringBuilder();
        }

        var initRegs = SpeedCodeSnippet.GetFirstNeededValues(updates[0], 2);
        var regA = 0x38;
        var regX = initRegs.Count > 0 ? initRegs[0] : 0;
        var regY = initRegs.Count > 1 ? initRegs[1] : 0;
        InitX = (byte)regX;
        InitY = (byte)regY;

        var totalFreeCycles = 0;

        var bugColors = _bugColors.ToList();
        for (var i = 0; i < BugColorSlots; i++)
        {
            _bugColors[i] &= 0xf;
        }

        FreeCycles = new List<int>();
        var snippets = new List<SpeedCodeSnippet>();
        var remainingDeferredUpdates = new HashSet<RegisterUpdate>(deferredUpdates);
        var unusedDeferredUpdates = new HashSet<RegisterUpdate>();
        for (var y = 0; y < AttributeHeight; y++)
        {
            var speedcode = new SpeedCodeSnippet();
            snippets.Add(speedcode);
            var generationAttempts = 0;
            var actualRowUpdates = new List<RegisterUpdate>(updates[y]);
            var nextRowUpdates = y < AttributeHeight - 1 ? updates[y + 1] : new List<RegisterUpdate>();
            var screenY = (y << 1) + 1;
            var matchingDeferredUpdates = remainingDeferredUpdates.Where(update => update.ScreenY <= screenY + 1 && screenY <= update.LastScreenY).OrderBy(update => update.LastScreenY).ToList();
            var initRegA = regA;
            var initRegX = regX;
            var initRegY = regY;
            var extraLimit = min(6, matchingDeferredUpdates.Count);
            var freeCycles = 0;
            while (true)
            {
                speedcode.Generate(y, regA, regX, regY, extraLimit, actualRowUpdates, nextRowUpdates, matchingDeferredUpdates);
                if (freeCycles >= 0)
                {
                    freeCycles = speedcode.FreeCycles;
                }
                generationAttempts++;
                if (speedcode.Overflowed)
                {
                    rsb?.Append($"Overflow {y} ({speedcode.Cycle}): ");
                    if (speedcode.ExtraUpdates > 0)
                    {
                        extraLimit = max(speedcode.ExtraUpdates - 1, 0);
                        rsb?.AppendLine($"Extra {extraLimit}");
                        continue;
                    }
                    freeCycles = -1;
                    if (DeferBugColorSwap(y, actualRowUpdates, nextRowUpdates, rsb))
                    {
                        continue;
                    }
                    if (DeferBugColorUpdate(y, actualRowUpdates, nextRowUpdates, rsb))
                    {
                        continue;
                    }
                    if (RemoveLeastImportantUnderlayUpdate(y, actualRowUpdates, rsb))
                    {
                        continue;
                    }
                    rsb?.AppendLine("Unresolved");
                    break;
                }
                break;
            }
            foreach (var update in speedcode.DeferredUpdatesIncluded)
            {
                remainingDeferredUpdates.Remove(update);
            }
            var droppedUpdates = remainingDeferredUpdates.Where(update => update.LastScreenY < screenY + 2).ToList();
            if (droppedUpdates.Count > 0)
            {
                freeCycles |= 0x100;
                unusedDeferredUpdates.UnionWith(droppedUpdates);
                foreach (var update in droppedUpdates)
                {
                    remainingDeferredUpdates.Remove(update);
                }
            }
            FreeCycles.Add(freeCycles);
            regA = speedcode.Registers[0];
            regX = speedcode.Registers[1];
            regY = speedcode.Registers[2];
            var effectiveUpdates = speedcode.EffectiveUpdates;
            if (y < AttributeHeight - 1)
            {
                screenY--;
                for (var s = 0; s < BugColorSlots; s++)
                {
                    _bugColors[(y + 1) * BugColorSlots + s] = _bugColors[y * BugColorSlots + s];
                }
                for (var c = 0; c < UnderlayColumns; c++)
                {
                    _underlayColors[(screenY + 1) * UnderlayColumns + c] = _underlayColors[screenY * UnderlayColumns + c] & 0xf;
                }
                var startedSecondRow = false;
                foreach (var update in effectiveUpdates)
                {
                    switch (update.Type)
                    {
                        case RegisterType.BugColor:
                            _bugColors[(y + 1) * BugColorSlots + update.Slot] = update.Value & 0xf;
                            break;
                        case RegisterType.UnderlayColor:
                            if (!startedSecondRow && update.ScreenY > screenY + 1)
                            {
                                startedSecondRow = true;
                                for (var c = 0; c < UnderlayColumns; c++)
                                {
                                    _underlayColors[(screenY + 2) * UnderlayColumns + c] = _underlayColors[(screenY + 1) * UnderlayColumns + c] & 0xf;
                                }
                            }
                            _underlayColors[max(update.ScreenY, screenY + 1) * UnderlayColumns + update.Slot] = update.Value;
                            break;
                    }
                }
                if (!startedSecondRow)
                {
                    for (var c = 0; c < UnderlayColumns; c++)
                    {
                        _underlayColors[(screenY + 2) * UnderlayColumns + c] = _underlayColors[(screenY + 1) * UnderlayColumns + c] & 0xf;
                    }
                }
            }
            if (log)
            {
                sb.Append($"{y}{((y & 3) == 0 ? "*" : "")}\t| ");
                for (var s = 0; s < BugColorSlots; s++)
                {
                    var col = bugColors[y * BugColorSlots + s];
                    sb.Append(col < 0 ? " " : $"{col:x1}");
                }
                sb.Append($" | ");
                for (var s = 0; s < BugColorSlots; s++)
                {
                    var col = _bugColors[y * BugColorSlots + s];
                    sb.Append(col < 0 ? " " : $"{col:x1}");
                }
                sb.Append($" | ");
                for (var c = 0; c < UnderlayColumns; c++)
                {
                    var col = _underlayColors[(y << 1) * UnderlayColumns + c];
                    sb.Append(col < 0 ? " " : $"{col:x1}");
                }
                sb.Append($" | {effectiveUpdates.Count,2} | free cc: {speedcode.FreeCycles,2}/{speedcode.Cycle} |");
                foreach (var update in effectiveUpdates)
                {
                    sb.Append($" {update.Address:x4}:{update.Value:x2}[{update.FirstCycle}-{update.LastCycle}]");
                }
                sb.AppendLine();
                var bugAttributes = _bitmapColors[y * AttributeWidth] & 0xff;
                sb.Append($"{(speedcode.Overflowed ? "!!!" : "")}\t| {(bugAttributes != 0xff ? $"{bugAttributes:x2}" : "  ")}   |      | ");
                for (var c = 0; c < UnderlayColumns; c++)
                {
                    var col = _underlayColors[(y << 1) * UnderlayColumns + UnderlayColumns + c];
                    sb.Append(col < 0 ? " " : $"{col:x1}");
                }
                sb.AppendLine($" | {(speedcode.ExtraUpdates > 0 ? "+" + speedcode.ExtraUpdates : "  ")} | a={initRegA & 0xff:x2} x={initRegX & 0xff:x2} y={initRegY & 0xff:x2} | " + string.Join("; ", speedcode.Code));
                foreach (var update in speedcode.DeferredUpdatesIncluded)
                {
                    rsb?.AppendLine($"Included {y} {update.Type} {update}");
                }
            }
            totalFreeCycles += speedcode.FreeCycles;
        }
        foreach (var update in unusedDeferredUpdates)
        {
            rsb?.AppendLine($"Unused {update.Type} {update}");
        }

        // NTSC adjustments
        var lastRegX = (int)InitX;
        var endedWithLastColumn = false;
        NtscAdjustments = new List<NtscAdjustment>();
        var padCycles = 0;
        NtscAdjustments.Add(NtscAdjustment.RunWithDelay(3, 0));
        var totalLength = 0;
        for (var y = 0; y < AttributeHeight; y++)
        {
            var speedcode = snippets[y];
            var originalLength = speedcode.Code.Select(ins => ins.Length).Sum();
            totalLength += originalLength;
            padCycles += endedWithLastColumn ? 3 : 2;
            endedWithLastColumn = speedcode.Code[^1].Operand == 0xd02d;
            if ((y & 3) < 3 || y == AttributeHeight - 1)
            {
                var trig = speedcode.Code[^1];
                var prev = speedcode.Code[^2];
                if (trig.Operation == Operation.STA && prev.Operand == 0xd02d)
                {
                    var xVal = lastRegX;
                    for (var i = speedcode.Code.Count - 2; i >= 0; i--)
                    {
                        var ins = speedcode.Code[i];
                        if (ins.Operation == Operation.LDX)
                        {
                            xVal = ins.Operand;
                            break;
                        }
                    }
                    NtscAdjustments.Add(NtscAdjustment.RunWithDelay(padCycles, originalLength - 3));
                    NtscAdjustments.Add(NtscAdjustment.AdjustFliTrigger(xVal & 0xff));
                    padCycles = 0;
                }
                else
                {
                    NtscAdjustments.Add(NtscAdjustment.RunWithDelay(padCycles, originalLength - 3));
                    NtscAdjustments.Add(NtscAdjustment.RunWithDelay(2, 3));
                    padCycles = 0;
                }
            }
            else if (!endedWithLastColumn)
            {
                NtscAdjustments.Add(NtscAdjustment.RunWithDelay(padCycles, originalLength));
                padCycles = 2;
            }
            else
            {
                NtscAdjustments.Add(NtscAdjustment.RunWithDelay(padCycles, originalLength));
                padCycles = 0;
            }
            lastRegX = speedcode.Registers[SpeedCodeSnippet.IndexX];
        }

        sb?.AppendLine($"free cycles = {totalFreeCycles}");
        sb?.Append("\nResolutions:\n" + rsb);

        var spriteMoveCount = snippets
            .Skip(RegisterUpdate.EarliestSpriteUpdateScreenY >> 1)
            .Take((RegisterUpdate.LatestSpriteUpdateScreenY >> 1) - (RegisterUpdate.EarliestSpriteUpdateScreenY >> 1) + 1)
            .Select(snippet => snippet.Code.Count(ins => ins.IsStore && ins.Operand < 0xd010))
            .Sum();
        SpriteMoveFailed = spriteMoveCount < 8;

        var instructions = snippets.SelectMany(snippet => snippet.Code).ToList();
        instructions.Add(new Instruction(Operation.RTS));

        if (log)
        {
            File.WriteAllText(logPath, sb.ToString());
        }

        Code = new List<byte>();
        new Instruction(Operation.STA, 0xd011).Emit(Code, slowValuesMapping);
        foreach (var instruction in instructions)
        {
            if (instruction.Operand == RegisterUpdate.DefaultBankSwitchValue)
            {
                BankChangeAddress = Code.Count + 0x1001;
            }
            instruction.Emit(Code, slowValuesMapping);
        }

        NtscRtsOffset = Code.Count - 1 + NtscAdjustments.Select(adj => adj.DeltaLength).Sum();
    }

    public void PadCode()
    {
        if (Code.Count > CodeSizeLimit)
        {
            Debug.LogWarning($"Speedcode size too big: {Code.Count}");
        }
        else
        {
            while (Code.Count < CodeSizeLimit)
            {
                Code.Add(0);
            }
        }
    }

    public void AdjustCodeForNtsc()
    {
        var ntscCode = new byte[NtscRtsOffset + 1];
        var src = Code.Count - 1;
        var dst = ntscCode.Length - 1;
        ntscCode[dst--] = Code[src--];
        for (var i = NtscAdjustments.Count - 1; i >= 0; i--)
        {
            NtscAdjustment adj = NtscAdjustments[i];
            switch (adj.DelayCycles)
            {
                case 0:
                    var newAdr = 0xd011 - adj.FliTriggerDelta;
                    ntscCode[dst--] = (byte)(newAdr >> 8);
                    ntscCode[dst--] = (byte)(newAdr & 0xff);
                    ntscCode[dst--] = 0x9d;
                    src -= 3;
                    break;
                case 2:
                case 3:
                case 4:
                    for (var j = 0; j < adj.RunLength; j++)
                    {
                        ntscCode[dst--] = Code[src--];
                    }
                    switch (adj.DelayCycles)
                    {
                        case 2:
                            ntscCode[dst--] = 0xea;
                            break;
                        case 3:
                            ntscCode[dst--] = 0x00;
                            ntscCode[dst--] = 0x24;
                            break;
                        case 4:
                            ntscCode[dst--] = 0xea;
                            ntscCode[dst--] = 0xea;
                            break;
                    }
                    break;
                default:
                    throw new ArgumentException($"Unsupported adjustment delay: {adj.DelayCycles}");
            }
        }
        while (src >= 0)
        {
            ntscCode[dst--] = Code[src--];
        }
        Code = ntscCode.ToList();
    }

    private bool DeferBugColorSwap(int y, List<RegisterUpdate> current, List<RegisterUpdate> next, StringBuilder sb)
    {
        if (y >= AttributeHeight - 1)
        {
            return false;
        }
        var hiresBugUpdate = current.FirstOrDefault(update => update.Type == RegisterType.BugColor && update.Slot == 0);
        if (hiresBugUpdate == null)
        {
            return false;
        }
        var multiSlot = -1;
        for (var i = 1; i < BugColorSlots; i++)
        {
            if ((_bugColors[y * BugColorSlots + i] & 0xf) == (hiresBugUpdate.Value & 0xf))
            {
                multiSlot = i;
                break;
            }
        }
        if (multiSlot < 0)
        {
            return false;
        }
        var multiBugUpdate = current.FirstOrDefault(update => update.Type == RegisterType.BugColor && update.Slot == multiSlot);
        if (multiBugUpdate == null || ((_bugColors[y * BugColorSlots] & 0xf) != (multiBugUpdate.Value & 0xf)))
        {
            return false;
        }
        current.Remove(hiresBugUpdate);
        current.Remove(multiBugUpdate);
        // Move the updates to the next row only if they are still relevant
        var keepHires = y < AttributeHeight - 2 && (hiresBugUpdate.Value & 0xf) == (_bugColors[(y + 2) * BugColorSlots] & 0xf);
        var keepMulti = y < AttributeHeight - 2 && (multiBugUpdate.Value & 0xf) == (_bugColors[(y + 2) * BugColorSlots + multiSlot] & 0xf);
        sb?.AppendLine($"Deferring Bug Swap {y} {multiBugUpdate.Slot} {hiresBugUpdate} ({(keepHires ? "keep" : "drop")}) {multiBugUpdate} ({(keepMulti ? "keep" : "drop")})");
        if (keepHires)
        {
            next.Add(hiresBugUpdate);
        }
        if (keepMulti)
        {
            next.Add(multiBugUpdate);
        }
        RegisterUpdate.SortUpdatesByTime(next);
        return true;
    }

    private bool DeferBugColorUpdate(int y, List<RegisterUpdate> current, List<RegisterUpdate> next, StringBuilder sb)
    {
        if (y >= AttributeHeight - 1)
        {
            return false;
        }
        RegisterUpdate bestUpdate = null;
        var bestD = int.MaxValue;
        var bixCur = y * BugColorSlots;
        var bixNext = bixCur + BugColorSlots;
        foreach (var update in current)
        {
            if (update.Type == RegisterType.BugColor)
            {
                var ch = _bugColors[(update.Slot == 0 ? bixCur : bixNext) + 0];
                var cm1 = _bugColors[(update.Slot == 1 ? bixCur : bixNext) + 1];
                var cm2 = _bugColors[(update.Slot == 2 ? bixCur : bixNext) + 2];
                var cm3 = _bugColors[(update.Slot == 3 ? bixCur : bixNext) + 3];
                var d = Image.GetBugSectionDistance(y + 1, ch, cm1, cm2, cm3);
                if (d < bestD)
                {
                    bestD = d;
                    bestUpdate = update;
                }
            }
        }
        if (bestUpdate == null)
        {
            return false;
        }
        current.Remove(bestUpdate);
        // Drop the update completely if it's already superseded in the next row
        var keep = y < AttributeHeight - 2 && bestUpdate.Value == _bugColors[(y + 2) * BugColorSlots + bestUpdate.Slot];
        sb?.AppendLine($"Deferring Bug {bestUpdate.Slot} {bestUpdate} ({(keep ? "keep" : "drop")})");
        if (keep)
        {
            next.Add(bestUpdate);
        }
        RegisterUpdate.SortUpdatesByTime(next);
        //BugColors[(y + 1) * BugColorSlots + bestUpdate.Slot] = BugColors[y * BugColorSlots + bestUpdate.Slot];
        return true;
    }

    private bool RemoveLeastImportantUnderlayUpdate(int y, List<RegisterUpdate> updates, StringBuilder sb)
    {
        if (y >= AttributeHeight - 1)
        {
            return false;
        }
        var bestD = int.MaxValue;
        var changedSlot = -1;
        var currentUpdates = new RegisterUpdate[UnderlayColumns];
        var nextUpdates = new RegisterUpdate[UnderlayColumns];
        var screenY = (y << 1) + 1;
        for (var i = 0; i < updates.Count; i++)
        {
            var update = updates[i];
            if (update.Type == RegisterType.UnderlayColor)
            {
                if (update.Early)
                {
                    currentUpdates[update.Slot] = update;
                }
                else
                {
                    nextUpdates[update.Slot] = update;
                }
            }
        }
        // First only check columns with two updates
        for (var i = 0; i < UnderlayColumns; i++)
        {
            if (currentUpdates[i] == null || nextUpdates[i] == null)
            {
                continue;
            }
            var colPrev = _underlayColors[(screenY - 1) * UnderlayColumns + i] & 0xf;
            var colNext = _underlayColors[(screenY + 1) * UnderlayColumns + i] & 0xf;
            var d1 = Image.GetMidSectionDistance(screenY, i, colPrev);
            var d2 = Image.GetMidSectionDistance(screenY, i, colNext);
            if (colPrev == colNext)
            {
                // Strongly prefer removing instances where a single row has a different colour within a longer run
                d2 >>= 2;
            }
            if (d2 < bestD)
            {
                // Replace the current update with the next one (the preferred option of the two)
                bestD = d2;
                changedSlot = i + UnderlayColumns;
            }
            if (d1 < bestD)
            {
                // Remove the current update
                bestD = d1;
                changedSlot = i;
            }
        }
        if (changedSlot >= 0)
        {
            var uix = (y << 1) * UnderlayColumns + (changedSlot % UnderlayColumns);
            if (changedSlot < UnderlayColumns)
            {
                var currentUpdate = currentUpdates[changedSlot];
                sb?.AppendLine($"Removing Underlay {y} {changedSlot} {currentUpdate}");
                updates.Remove(currentUpdate);
                _underlayColors[uix + UnderlayColumns] = _underlayColors[uix];
            }
            else
            {
                var column = changedSlot - UnderlayColumns;
                var currentUpdate = currentUpdates[column];
                var nextUpdate = nextUpdates[column];
                if (_underlayColors[uix] == _underlayColors[uix + UnderlayColumns * 2])
                {
                    sb?.AppendLine($"Removing Double Underlay {y} {changedSlot} {currentUpdate} + {nextUpdate}");
                    updates.Remove(currentUpdate);
                    updates.Remove(nextUpdate);
                    _underlayColors[uix + UnderlayColumns] = _underlayColors[uix];
                }
                else
                {
                    sb?.AppendLine($"Overriding Underlay {y} {changedSlot} {currentUpdates[column]} -> {nextUpdate.Value:x2}");
                    currentUpdate.Value = nextUpdate.Value;
                    updates.Remove(nextUpdate);
                    _underlayColors[uix + UnderlayColumns] = _underlayColors[uix + UnderlayColumns * 2];
                }
            }
            return true;
        }
        // Remove the only update from a column if it's immediately updated in the following section
        bestD = int.MaxValue;
        RegisterUpdate bestUpdate = null;
        for (var i = 0; i < UnderlayColumns; i++)
        {
            var update = currentUpdates[i] ?? nextUpdates[i];
            if (update == null)
            {
                continue;
            }
            var colNext = _underlayColors[(screenY + 1) * UnderlayColumns + i] & 0xf;
            var colAfter = _underlayColors[(screenY + 2) * UnderlayColumns + i] & 0xf;
            if (colAfter == colNext)
            {
                continue;
            }
            var colPrev = _underlayColors[(screenY - 1) * UnderlayColumns + i] & 0xf;
            var d = Image.GetMidSectionDistance(screenY + 1, i, colPrev);
            if (update.Early)
            {
                d += Image.GetMidSectionDistance(screenY, i, colPrev);
            }
            if (d < bestD)
            {
                bestD = d;
                bestUpdate = update;
                changedSlot = i;
            }
        }
        if (bestUpdate != null)
        {
            var uix = (y << 1) * UnderlayColumns + changedSlot;
            updates.Remove(bestUpdate);
            sb?.AppendLine($"Removing Solitary Underlay {y} {changedSlot} {bestUpdate}");
            _underlayColors[uix + UnderlayColumns] = _underlayColors[uix];
            _underlayColors[uix + UnderlayColumns * 2] = _underlayColors[uix];
            return true;
        }
        return false;
    }
}

public class SpeedCodeSnippet
{
    public const int RegisterCount = 3;
    public const int IndexA = 0;
    public const int IndexX = 1;
    public const int IndexY = 2;

    public int RowNumber;
    public int Cycle;
    public readonly int[] Registers = new int[RegisterCount];
    public readonly List<Instruction> Code = new();
    public readonly List<RegisterUpdate> EffectiveUpdates = new();
    public readonly List<RegisterUpdate> DeferredUpdatesIncluded = new();
    public bool Overflowed;
    public int FreeCycles;
    public int ExtraUpdates;

    private readonly Operation[] LoadOps = { Operation.LDA, Operation.LDX, Operation.LDY };
    private readonly Operation[] StoreOps = { Operation.STA, Operation.STX, Operation.STY };
    private readonly int[] _futureUses = new int[RegisterCount];
    private int _fliRegister = -1;
    private int _lastColumnRegister = -1;

    public void Generate(int rowNumber, int a, int x, int y, int extraLimit, List<RegisterUpdate> updates, List<RegisterUpdate> followingUpdates, List<RegisterUpdate> deferredUpdates)
    {
        RowNumber = rowNumber;
        Cycle = rowNumber > 0 && (rowNumber & 3) == 0 ? 0x0a : 0x0b;
        Registers[IndexA] = a;
        Registers[IndexX] = x;
        Registers[IndexY] = y;
        Code.Clear();
        Overflowed = false;
        FreeCycles = 0;
        ExtraUpdates = 0;
        EffectiveUpdates.Clear();
        DeferredUpdatesIncluded.Clear();
        var remainingUpdates = new List<RegisterUpdate>(updates);
        var screenY = (rowNumber << 1) + 1;
        var earlyCount = remainingUpdates.Count(update => update.Early);
        for (var i = 0; i < deferredUpdates.Count; i++)
        {
            var update = deferredUpdates[i];
            if (update.Type != RegisterType.SpriteY || update.LastScreenY != screenY)
            {
                if (ExtraUpdates >= extraLimit)
                {
                    break;
                }
                ExtraUpdates++;
            }
            remainingUpdates.Add(update.CloneDeferred(screenY));
            DeferredUpdatesIncluded.Add(update);
        }
        RegisterUpdate.SortUpdatesByTime(remainingUpdates);
        for (var r = 0; r < RegisterCount; r++)
        {
            _futureUses[r] = remainingUpdates.Count(c => c.AcceptsValue(Registers[r]));
        }
        _fliRegister = -1;
        _lastColumnRegister = -1;
        while (remainingUpdates.Count > 0)
        {
            var update = remainingUpdates[0];
            if (update.Early)
            {
                // Emit the write immediately, trying to make use of the current values of registers
                remainingUpdates.Remove(update);
                if (TryAddImmediateWrite(update))
                {
                    continue;
                }
                AddBestUpdate(update, remainingUpdates);
            }
            else if (!update.Late(Cycle))
            {
                // Updates that are neither early nor late can be freely rearranged, so we try to pick the best one first
                var emitted = false;
                for (var i = 0; i < remainingUpdates.Count; i++)
                {
                    update = remainingUpdates[i];
                    if (update.Late(Cycle))
                    {
                        break;
                    }
                    if (TryAddImmediateWrite(update))
                    {
                        emitted = true;
                        break;
                    }
                }
                if (!emitted)
                {
                    // Couldn't find a fast one, let's just pick the first in the list
                    update = remainingUpdates[0];
                }
                remainingUpdates.Remove(update);
                if (emitted)
                {
                    continue;
                }
                AddBestUpdate(update, remainingUpdates);
            }
            else
            {
                // Special case: when we need to update the last column just before the FLI trigger, we need to have them prepared in advance
                // TODO: properly figure out the latest moment at which we need to preload the mandatory registers
                // we don't necessarily want to do them right away, because they force everything to go through the third register, which can be less efficient
                if (_fliRegister < 0 && remainingUpdates.Count > 1 && remainingUpdates[^1].Address == 0xd011 && remainingUpdates[^2].Address == 0xd02d/* && (Cycle > 0x20 || remainingUpdates.Count < 5)*/)
                {
                    //_fliRegister = AddBestLoad(remainingUpdates[^1].Value, remainingUpdates);
                    // We have to force using A for the FLI trigger, because we might need to add a cycle to it when patching code for NTSC
                    _fliRegister = IndexA;
                    AddLoad(IndexA, remainingUpdates[^1].Value, remainingUpdates, superfluousLoadAllowed: true);
                    _lastColumnRegister = AddBestLoad(remainingUpdates[^2].Value, remainingUpdates, true);
                }
                // Commit to this write, but try to preload registers with any subsequently useful values and pad with the necessary delay
                if (!AlreadyCovered(update))
                {
                    AddBestLoad(update.Value, remainingUpdates, update.OnlyNybble);
                }
                // Problem: when a subsequent update is multicovered, we are missing an opportunity to preload a needed register
                var waitCycles = update.FirstCycle - 3 - Cycle;
                while (waitCycles > 1)
                {
                    var addedLoad = false;
                    for (var r = 0; r < RegisterCount; r++)
                    {
                        if (_futureUses[r] != 0 || r == _fliRegister || r == _lastColumnRegister)
                        {
                            continue;
                        }
                        var futureUpdates = remainingUpdates.ToList();
                        if (AlreadyCovered(remainingUpdates[^1]))
                        {
                            futureUpdates.AddRange(followingUpdates);
                        }
                        for (var j = 0; j < futureUpdates.Count; j++)
                        {
                            var futureUpdate = futureUpdates[j];
                            if (AlreadyCovered(futureUpdate))
                            {
                                continue;
                            }
                            AddLoad(r, futureUpdate.Value, futureUpdates);
                            waitCycles -= 2;
                            addedLoad = true;
                            break;
                        }
                        break;
                    }
                    if (addedLoad)
                    {
                        continue;
                    }
                    AddNop();
                    waitCycles -= 2;
                }
                if (waitCycles == 1)
                {
                    for (var i = Code.Count - 1; i >= 0; i--)
                    {
                        if (Code[i].AddExtraCycleIfPossible())
                        {
                            Cycle++;
                            if (Code[i].Operation == Operation.NOP)
                            {
                                FreeCycles++;
                            }
                            break;
                        }
                    }
                }
                remainingUpdates.Remove(update);
                TryAddImmediateWrite(update);
            }
        }
        while (Cycle < RegisterUpdate.LatestCycle)
        {
            AddNop(Cycle == 0x34);
        }
    }

    private bool AlreadyCovered(RegisterUpdate update)
    {
        for (var r = 0; r < RegisterCount; r++)
        {
            if (update.AcceptsValue(Registers[r]))
            {
                return true;
            }
        }
        return false;
    }

    private void CheckOverflow(RegisterUpdate update)
    {
        if (update.LastCycle >= Cycle - 1)
        {
            return;
        }
        if (update.LastCycle == RegisterUpdate.FliTriggerCycle && Code[^2].IsStore)
        {
            return;
        }
        if (Overflowed)
        {
            return;
        }
        //Debug.Log($"Overflow at y = {RowNumber} cycle = {Cycle:x02} update = {update}");
        Overflowed = true;
    }

    private bool TryAddImmediateWrite(RegisterUpdate update)
    {
        var emitted = false;
        for (var r = 0; r < RegisterCount; r++)
        {
            if (!update.AcceptsValue(Registers[r]))
            {
                continue;
            }
            if (!emitted)
            {
                AddStore(r, update.Address);
                EffectiveUpdates.Add(update);
                CheckOverflow(update);
                emitted = true;
            }
            _futureUses[r]--;
        }
        return emitted;
    }

    private int AddBestLoad(int value, List<RegisterUpdate> remainingUpdates, bool freeHighNybble = false)
    {
        var chosenRegister = -1;
        var usesMin = int.MaxValue;
        for (var r = 0; r < RegisterCount; r++)
        {
            if (r == _fliRegister || r == _lastColumnRegister)
            {
                continue;
            }
            if (_futureUses[r] < usesMin)
            {
                usesMin = _futureUses[r];
                chosenRegister = r;
            }
        }
        if (freeHighNybble)
        {
            for (var i = 0; i < remainingUpdates.Count; i++)
            {
                var otherUpdate = remainingUpdates[i];
                if (!otherUpdate.OnlyNybble && (otherUpdate.Value & 0xf) == value)
                {
                    value = otherUpdate.Value;
                    break;
                }
            }
        }
        for (var r = 0; r < RegisterCount; r++)
        {
            if (Registers[r] == value)
            {
                // We already have this value in a register
                return r;
            }
        }
        AddLoad(chosenRegister, value, remainingUpdates);
        return chosenRegister;
    }

    private void AddBestUpdate(RegisterUpdate update, List<RegisterUpdate> remainingUpdates)
    {
        var chosenRegister = AddBestLoad(update.Value, remainingUpdates, update.OnlyNybble);
        AddStore(chosenRegister, update.Address);
        EffectiveUpdates.Add(update);
        CheckOverflow(update);
    }

    private void AddNop(bool extraCycle = false)
    {
        int cycles = extraCycle ? 3 : 2;
        Cycle += cycles;
        FreeCycles += cycles;
        Code.Add(new Instruction(Operation.NOP, extraCycle: extraCycle));
    }

    private void AddLoad(int registerIndex, int value, List<RegisterUpdate> futureUpdates, bool extraCycle = false, bool superfluousLoadAllowed = false)
    {
        if (!superfluousLoadAllowed && (Registers[0] == value || Registers[1] == value || Registers[2] == value))
        {
            Debug.LogWarning($"Trying to add superfluous load ({LoadOps[registerIndex]} {value:x02} / {Registers[0]:x02} {Registers[1]:x02} {Registers[2]:x02})!");
        }
        Cycle += extraCycle ? 3 : 2;
        Registers[registerIndex] = value;
        _futureUses[registerIndex] = futureUpdates.Count(update => update.AcceptsValue(value));
        Code.Add(new Instruction(LoadOps[registerIndex], value, extraCycle));
    }

    private void AddStore(int registerIndex, int address)
    {
        Cycle += 4;
        Code.Add(new Instruction(StoreOps[registerIndex], address));
    }

    public static List<byte> GetFirstNeededValues(List<RegisterUpdate> updates, int limit)
    {
        var result = new List<byte>();
        for (var i = 0; i < updates.Count && result.Count < limit; i++)
        {
            var update = updates[i];
            var val = (byte)update.Value;
            var found = result.Contains(val) || (update.OnlyNybble && result.Any(v => (v & 0xf) == (val & 0xf)));
            // TODO If the result already contains a compatible nybble, we should replace it
            if (!found)
            {
                result.Add(val);
            }
        }
        return result;
    }
}

public enum Operation
{
    NOP, RTS, LDA, LDX, LDY, STA, STX, STY
}

public class Instruction
{
    public Operation Operation;
    public int Operand;
    public bool ExtraCycle;
    public int OperandOffset;

    public int Cycles => (ExtraCycle ? 1 : 0) + Operation switch
    {
        Operation.NOP => 2,
        Operation.RTS => 6,
        Operation.LDA => 2,
        Operation.LDX => 2,
        Operation.LDY => 2,
        Operation.STA => 4,
        Operation.STX => 4,
        Operation.STY => 4,
        _ => 0
    };

    public int Length => Operation switch
    {
        Operation.NOP => ExtraCycle ? 2 : 1,
        Operation.RTS => 1,
        Operation.LDA => 2,
        Operation.LDX => 2,
        Operation.LDY => 2,
        Operation.STA => 3,
        Operation.STX => 3,
        Operation.STY => 3,
        _ => 0
    };

    public bool IsStore => Operation == Operation.STA || Operation == Operation.STX || Operation == Operation.STY;

    public Instruction(Operation operation, int operand = 0, bool extraCycle = false)
    {
        Operation = operation;
        Operand = operand;
        ExtraCycle = extraCycle;
    }

    public bool AddExtraCycleIfPossible()
    {
        if (ExtraCycle || Operand < 0 || IsStore)
        {
            return false;
        }
        ExtraCycle = true;
        return true;
    }

    public void ExtendSta(int xVal)
    {
        if (Operation != Operation.STA)
        {
            return;
        }
        ExtraCycle = true;
        OperandOffset = -xVal;
    }

    public void Emit(IList<byte> code, Dictionary<int, byte> slowValuesMapping)
    {
        switch (Operation)
        {
            case Operation.NOP:
                if (ExtraCycle)
                {
                    code.Add(0x24);
                    code.Add(0x00);
                }
                else
                {
                    code.Add(0xea);
                }
                break;
            case Operation.RTS:
                code.Add(0x60);
                break;
            case Operation.LDA:
                if (ExtraCycle)
                {
                    code.Add(0xa5);
                    code.Add(slowValuesMapping[Operand]);
                }
                else
                {
                    code.Add(0xa9);
                    code.Add((byte)Operand);
                }
                break;
            case Operation.LDX:
                if (ExtraCycle)
                {
                    code.Add(0xa6);
                    code.Add(slowValuesMapping[Operand]);
                }
                else
                {
                    code.Add(0xa2);
                    code.Add((byte)Operand);
                }
                break;
            case Operation.LDY:
                if (ExtraCycle)
                {
                    code.Add(0xa4);
                    code.Add(slowValuesMapping[Operand]);
                }
                else
                {
                    code.Add(0xa0);
                    code.Add((byte)Operand);
                }
                break;
            case Operation.STA:
                code.Add((byte)(ExtraCycle ? 0x9d : 0x8d));
                var actualOperand = Operand + OperandOffset;
                code.Add((byte)actualOperand);
                code.Add((byte)(actualOperand >> 8));
                break;
            case Operation.STX:
                code.Add(0x8e);
                code.Add((byte)Operand);
                code.Add((byte)(Operand >> 8));
                break;
            case Operation.STY:
                code.Add(0x8c);
                code.Add((byte)Operand);
                code.Add((byte)(Operand >> 8));
                break;
        }
    }

    public override string ToString()
    {
        var imm = Operand;
        var reg = $"V_{Operand & 0xff:x2}";
        var op = Operation switch
        {
            Operation.NOP => "N",
            Operation.RTS => "R",
            Operation.LDA => $"A={imm:x2}",
            Operation.LDX => $"X={imm:x2}",
            Operation.LDY => $"Y={imm:x2}",
            Operation.STA => $"{reg}=A",
            Operation.STX => $"{reg}=X",
            Operation.STY => $"{reg}=Y",
            _ => throw new NotImplementedException(),
        };
        return (ExtraCycle ? "*" : "") + op;
    }
}

public class RegisterUpdate
{
    public const int EarliestCycle = 0x0a;
    public const int LatestCycle = 0x37;
    public const int FliTriggerCycle = 0x3a;
    public const int UnderlayStartCycle = 0x14;
    public const int EarliestSpriteUpdateScreenY = 0x7b;
    public const int LatestSpriteUpdateScreenY = 0xa3;
    public const int DefaultBankSwitchValue = -253;

    private static readonly int[] BugRegisters = { 0xd027, 0xd025, 0xd02e, 0xd026 };

    private static readonly int[] ScreenAddressValues =
    {
        0x68, 0x58, 0x48, 0x38, 0x28, 0x18, 0x08, 0x78, 0x68, 0x58, 0x48, 0x38, 0x28, 0x18, 0x08, 0x78,
        0x68, 0x58, 0x48, 0x38, 0x28, 0x18, 0x08, 0x78, 0x68, 0x58, 0x48, 0x38, 0x28, 0x18, 0x08, 0x78,
        0x68, 0x58, 0x48, 0x38, 0x28, 0x18, 0x08, 0x78, 0x68, 0x58, 0x48, 0x38, 0x28, 0x18, 0x08, 0x78,
        0x68, 0x58, 0x48, 0x38, 0x28, 0x18, 0x08, 0x78, 0x68, 0x58, 0x48, 0x38, 0x28, 0x18, 0x08, DefaultBankSwitchValue,
        0x98, 0xa8, 0xb8, 0x88, 0x98, 0xa8, 0xb8, 0x88, 0x98, 0xa8, 0xb8, 0x88, 0x98, 0xa8, 0xb8, 0x88,
        0x98, 0xa8, 0xb8, 0x98, 0xa8, 0xb8, 0x88, 0x98, 0xa8, 0xb8, 0x88, 0x98, 0xa8, 0xb8, 0x88, 0x98,
        0xa8, 0xb8, 0x88, 0x18
    };

    public RegisterType Type;
    public int Slot;
    public int Value;
    public int ScreenY; // The screen row at which the new value will be taking effect
    public int LastScreenY; // The screen row until which the update can be deferred (it needs to be set just before)

    public int Address => Type switch
    {
        RegisterType.UnderlayColor => 0xd028 + Slot,
        RegisterType.BugColor => BugRegisters[Slot],
        RegisterType.BorderColor => 0xd020,
        RegisterType.BackgroundColor => 0xd021,
        RegisterType.ScreenAddress => Value < 0 ? 0xdd00 : 0xd018,
        RegisterType.FliTrigger => 0xd011,
        RegisterType.SpriteY => 0xd001 + (Slot << 1),
        _ => throw new NotImplementedException(),
    };

    public int FirstCycle => Type switch
    {
        RegisterType.UnderlayColor => ScreenY < 0 ? EarliestCycle : (ScreenY & 1) == 0 ? min(UnderlayStartCycle + (Slot + 1) * UnderlayBlockWidth, LatestCycle) : EarliestCycle,
        RegisterType.BugColor => ScreenY < 0 ? EarliestCycle : UnderlayStartCycle,
        RegisterType.BorderColor => EarliestCycle + 4,
        RegisterType.FliTrigger => (ScreenY & 6) == 0 ? ScreenY < ScreenHeight ? EarliestCycle : LatestCycle + 1 : FliTriggerCycle,
        _ => EarliestCycle
    };

    public int LastCycle => Type switch
    {
        RegisterType.UnderlayColor => ScreenY < 0 ? LatestCycle : (ScreenY & 1) == 0 ? LatestCycle : UnderlayStartCycle - 1 + Slot * UnderlayBlockWidth,
        RegisterType.FliTrigger => max(FirstCycle, LatestCycle),
        _ => LatestCycle
    };

    public bool OnlyNybble => Type switch
    {
        RegisterType.UnderlayColor => true,
        RegisterType.BugColor => true,
        RegisterType.BorderColor => true,
        RegisterType.BackgroundColor => true,
        _ => false
    };

    public bool Early => LastCycle < LatestCycle;

    public bool Late(int cycle) => FirstCycle > cycle + 3;

    public bool AcceptsValue(int value) => Value == value || (value >= 0 && OnlyNybble && (Value & 0xf) == (value & 0xf));

    public RegisterUpdate CloneDeferred(int newScreenY)
    {
        var y = ScreenY >= newScreenY ? ScreenY : -1;
        return new RegisterUpdate
        {
            Type = Type,
            Slot = Slot,
            Value = Value,
            ScreenY = y,
            LastScreenY = y
        };
    }

    public override string ToString() => $"{Address:x04}:{Value:x02}/{FirstCycle:x02}-{LastCycle:x02}/{ScreenY}-{LastScreenY}";

    public static void SortUpdatesByTime(List<RegisterUpdate> updates)
    {
        updates.Sort((u1, u2) =>
        {
            var first = u1.FirstCycle.CompareTo(u2.FirstCycle);
            return first == 0 ? u1.LastCycle.CompareTo(u2.LastCycle) : first;
        }
        );
    }

    public static RegisterUpdate UnderlayColorUpdate(int column, int color, int screenY, int lastScreenY = -1)
    {
        return new RegisterUpdate
        {
            Type = RegisterType.UnderlayColor,
            Slot = column,
            Value = color,
            ScreenY = screenY,
            LastScreenY = lastScreenY < 0 ? screenY : lastScreenY
        };
    }

    public static RegisterUpdate BugColorUpdate(int slot, int color, int screenY, int lastScreenY = -1)
    {
        return new RegisterUpdate
        {
            Type = RegisterType.BugColor,
            Slot = slot,
            Value = color,
            ScreenY = screenY,
            LastScreenY = lastScreenY < 0 ? screenY : lastScreenY
        };
    }

    public static RegisterUpdate BorderColorUpdate(int color, int screenY)
    {
        return new RegisterUpdate
        {
            Type = RegisterType.BorderColor,
            Value = color,
            ScreenY = screenY,
            LastScreenY = screenY
        };
    }

    public static RegisterUpdate BackgroundColorUpdate(int color)
    {
        return new RegisterUpdate
        {
            Type = RegisterType.BackgroundColor,
            Value = color,
            ScreenY = 0,
            LastScreenY = ScreenHeight - 1
        };
    }

    public static RegisterUpdate ScreenAddressUpdate(int screenY)
    {
        return new RegisterUpdate
        {
            Type = RegisterType.ScreenAddress,
            Slot = -1,
            Value = ScreenAddressValues[(screenY >> 1) - 1],
            ScreenY = screenY,
            LastScreenY = screenY
        };
    }

    public static RegisterUpdate FliTriggerUpdate(int screenY)
    {
        return new RegisterUpdate
        {
            Type = RegisterType.FliTrigger,
            Slot = -1,
            Value = screenY < ScreenHeight ? 0x38 | (screenY & 7) : 0x10,
            ScreenY = screenY,
            LastScreenY = screenY
        };
    }

    public static RegisterUpdate SpriteYUpdate(int slot)
    {
        return new RegisterUpdate
        {
            Type = RegisterType.SpriteY,
            Slot = slot,
            Value = 0xd4,
            ScreenY = EarliestSpriteUpdateScreenY,
            LastScreenY = LatestSpriteUpdateScreenY - ((slot ^ 7) << 1)
        };
    }
}

public enum RegisterType
{
    UnderlayColor, BugColor, BorderColor, BackgroundColor, ScreenAddress, FliTrigger, SpriteY
}

public class NtscAdjustment
{
    public int DelayCycles;
    public int RunLength;
    public int FliTriggerDelta;

    public int DeltaLength => DelayCycles switch
    {
        0 => 0,
        2 => 1,
        3 => 2,
        4 => 2,
        _ => throw new ArgumentOutOfRangeException()
    };

    public static NtscAdjustment RunWithDelay(int delayCycles, int runLength) => new() { DelayCycles = delayCycles, RunLength = runLength };

    public static NtscAdjustment AdjustFliTrigger(int delta) => new() { FliTriggerDelta = delta };

    public static Dictionary<int, int> GetDeltaMapping(IList<NtscAdjustment> adjustments)
    {
        var result = new Dictionary<int, int>();
        foreach (var adjustment in adjustments)
        {
            if (adjustment.DelayCycles > 0 || result.ContainsKey(adjustment.FliTriggerDelta))
            {
                continue;
            }
            result[adjustment.FliTriggerDelta] = result.Count;
        }
        return result;
    }

    public byte GetValue(Dictionary<int, int> deltaMapping) => (byte)(DelayCycles == 0 ? deltaMapping[FliTriggerDelta] : ((DelayCycles - 1) << 6) | RunLength);
}