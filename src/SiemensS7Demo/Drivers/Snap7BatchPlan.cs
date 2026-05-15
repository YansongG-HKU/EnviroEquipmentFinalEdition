using System.Collections.Generic;
using System.Linq;
using SiemensS7Demo.Models;

namespace SiemensS7Demo.Drivers;

public sealed record Snap7BatchTagSlot(TagDefinition Tag, int OffsetInWindow);

public sealed record Snap7BatchWindow(
    int AreaCode,
    int DbNumber,
    int StartByte,
    int Length,
    IReadOnlyList<Snap7BatchTagSlot> Tags);

public static class Snap7BatchPlan
{
    public static IReadOnlyList<Snap7BatchWindow> Plan(
        IReadOnlyList<TagDefinition> tags,
        int maxWindowBytes,
        int mergeSlack)
    {
        var slots = tags
            .Select(tag =>
            {
                var address = S7Address.Parse(tag);
                return new
                {
                    Tag = tag,
                    Address = address,
                    Size = address.ByteSize(tag.DataType)
                };
            })
            .OrderBy(s => s.Address.AreaCode)
            .ThenBy(s => s.Address.DbNumber)
            .ThenBy(s => s.Address.ByteOffset)
            .ToList();

        var windows = new List<Snap7BatchWindow>();
        var bucket = new List<(TagDefinition Tag, S7Address Address, int Size)>();
        int currentArea = -1;
        int currentDb = -1;

        void EmitWindow(List<(TagDefinition Tag, S7Address Address, int Size)> segment)
        {
            var startByte = segment[0].Address.ByteOffset;
            var length = segment.Max(s => s.Address.ByteOffset + s.Size) - startByte;
            var slotList = segment
                .Select(s => new Snap7BatchTagSlot(s.Tag, s.Address.ByteOffset - startByte))
                .ToList();
            windows.Add(new Snap7BatchWindow(
                segment[0].Address.AreaCode,
                segment[0].Address.DbNumber,
                startByte,
                length,
                slotList));
        }

        void Flush()
        {
            if (bucket.Count == 0) return;

            var segment = new List<(TagDefinition Tag, S7Address Address, int Size)>();
            int end = 0;

            foreach (var item in bucket)
            {
                var itemEnd = item.Address.ByteOffset + item.Size;
                if (segment.Count == 0)
                {
                    segment.Add(item);
                    end = itemEnd;
                    continue;
                }

                var gap = item.Address.ByteOffset - end;
                var prospectiveLength = itemEnd - segment[0].Address.ByteOffset;
                // Split when the gap exceeds the slack threshold AND the window would still
                // be smaller than max (i.e., merging would waste space without filling a window).
                // If the window would reach or exceed max, we always merge up to max and rely
                // on the size cap to prevent over-reads.
                var shouldSplit = gap > mergeSlack && prospectiveLength < maxWindowBytes;
                if (!shouldSplit && prospectiveLength <= maxWindowBytes)
                {
                    segment.Add(item);
                    end = itemEnd;
                }
                else
                {
                    EmitWindow(segment);
                    segment = new List<(TagDefinition, S7Address, int)> { item };
                    end = itemEnd;
                }
            }

            if (segment.Count > 0) EmitWindow(segment);
            bucket.Clear();
        }

        foreach (var s in slots)
        {
            if (s.Address.AreaCode != currentArea || s.Address.DbNumber != currentDb)
            {
                Flush();
                currentArea = s.Address.AreaCode;
                currentDb = s.Address.DbNumber;
            }
            bucket.Add((s.Tag, s.Address, s.Size));
        }
        Flush();

        return windows;
    }
}
