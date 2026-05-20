using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SiemensS7Demo.Domain.Programs;
using SiemensS7Demo.Domain.Programs.Abstractions;
using SiemensS7Demo.Persistence.Entities;

namespace SiemensS7Demo.Persistence;

/// <summary>
/// SQLite-backed <see cref="IProgramRepository"/>. Serializes each <see cref="Program"/> to
/// a single JSON blob column in <c>Programs.JsonBlob</c>. Draft slots are stored in the same
/// table with a <c>draft:</c> name prefix so the committed and draft copies of "RecipeA"
/// coexist as rows "RecipeA" and "draft:RecipeA".
/// </summary>
/// <remarks>
/// The wire form is a small <c>PortableProgram</c> / <c>PortableSegment</c> projection rather
/// than direct serialization of the Domain types. Two reasons:
/// <list type="number">
///   <item><description>Polymorphism — <see cref="CycleAction"/> has two subtypes
///   (<see cref="CycleAction.JumpToCycle"/>, <see cref="CycleAction.EndCycle"/>). The
///   projection flattens this with a <c>CycleKind</c> discriminator string plus the two
///   JMP-only fields, which avoids the runtime cost and brittleness of
///   <see cref="JsonDerivedTypeAttribute"/> /
///   <see cref="JsonPolymorphicAttribute"/> while supporting the same set of subtypes.</description></item>
///   <item><description>Rename safety — a Domain type rename (e.g. renaming the
///   <c>JumpToCycle</c> record) does not silently shred old DB rows; the discriminator
///   string is the schema contract.</description></item>
/// </list>
/// </remarks>
public sealed class SqliteProgramRepository : IProgramRepository
{
    internal const string DraftPrefix = "draft:";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Func<EnviroDbContext> _contextFactory;

    public SqliteProgramRepository(Func<EnviroDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    public Task SaveAsync(Program program, CancellationToken ct) => SaveCore(program, draft: false, ct);
    public Task SaveDraftAsync(Program program, CancellationToken ct) => SaveCore(program, draft: true, ct);

    private async Task SaveCore(Program program, bool draft, CancellationToken ct)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        if (string.IsNullOrWhiteSpace(program.Name))
            throw new ArgumentException("Program.Name must be non-empty.", nameof(program));

        var key = draft ? DraftPrefix + program.Name : program.Name;
        var blob = JsonSerializer.Serialize(new PortableProgram(program), JsonOpts);
        var now = DateTimeOffset.UtcNow;

        using var ctx = _contextFactory();
        var existing = await ctx.Programs.FindAsync(new object[] { key }, ct);
        if (existing is null)
        {
            ctx.Programs.Add(new ProgramRow { Name = key, JsonBlob = blob, UpdatedAt = now });
        }
        else
        {
            existing.JsonBlob = blob;
            existing.UpdatedAt = now;
        }
        await ctx.SaveChangesAsync(ct);
    }

    public Task<Program?> GetAsync(string name, CancellationToken ct) => GetCore(name, draft: false, ct);
    public Task<Program?> GetDraftAsync(string name, CancellationToken ct) => GetCore(name, draft: true, ct);

    private async Task<Program?> GetCore(string name, bool draft, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must be non-empty.", nameof(name));

        var key = draft ? DraftPrefix + name : name;
        using var ctx = _contextFactory();
        var row = await ctx.Programs.FindAsync(new object[] { key }, ct);
        if (row is null) return null;
        var portable = JsonSerializer.Deserialize<PortableProgram>(row.JsonBlob, JsonOpts)
            ?? throw new InvalidOperationException($"Program '{key}' JSON is malformed.");
        return portable.ToProgram(name);
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct)
    {
        using var ctx = _contextFactory();
        return await ctx.Programs
            .Where(p => !p.Name.StartsWith(DraftPrefix))
            .OrderBy(p => p.Name)
            .Select(p => p.Name)
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name must be non-empty.", nameof(name));

        using var ctx = _contextFactory();
        var row = await ctx.Programs.FindAsync(new object[] { name }, ct);
        if (row is not null)
        {
            ctx.Programs.Remove(row);
            await ctx.SaveChangesAsync(ct);
        }
        var draft = await ctx.Programs.FindAsync(new object[] { DraftPrefix + name }, ct);
        if (draft is not null)
        {
            ctx.Programs.Remove(draft);
            await ctx.SaveChangesAsync(ct);
        }
    }

    // ---- Wire form (kept decoupled from Domain types so a rename does not shred old rows) ----

    private sealed record PortableProgram(IReadOnlyList<PortableSegment> Segments)
    {
        [JsonConstructor]
        public PortableProgram() : this((IReadOnlyList<PortableSegment>)Array.Empty<PortableSegment>()) { }

        public PortableProgram(Program p)
            : this(p.Segments.Select(s => new PortableSegment(s)).ToList()) { }

        public Program ToProgram(string name)
            => new() { Name = name, Segments = Segments.Select(s => s.ToSegment()).ToList() };
    }

    private sealed record PortableSegment(
        int Index,
        double TempSetpoint,
        double? HumidSetpoint,
        long DurationTicks,
        SegmentMode Mode,
        string? CycleKind,
        int? JmpTargetIndex,
        int? JmpCount,
        bool[] DigitalOutputs,
        string? Note)
    {
        // Parameterless ctor for the JSON deserializer; required-init records would also work
        // but adding it makes the record explicit about which constructor STJ binds against.
        [JsonConstructor]
        public PortableSegment() : this(
            0, 0d, null, 0L, SegmentMode.Hold, null, null, null, Array.Empty<bool>(), null) { }

        public PortableSegment(Segment s) : this(
            s.Index, s.TempSetpoint, s.HumidSetpoint,
            s.Duration.Ticks, s.Mode,
            CycleKindOf(s.Cycle),
            (s.Cycle as CycleAction.JumpToCycle)?.TargetIndex,
            (s.Cycle as CycleAction.JumpToCycle)?.Count,
            s.DigitalOutputs ?? Array.Empty<bool>(),
            s.Note)
        { }

        public Segment ToSegment()
        {
            CycleAction? cycle = CycleKind switch
            {
                "Jmp" when JmpTargetIndex.HasValue && JmpCount.HasValue
                    => new CycleAction.JumpToCycle(JmpTargetIndex.Value, JmpCount.Value),
                "End" => new CycleAction.EndCycle(),
                _ => null
            };
            return new Segment(Index, TempSetpoint, HumidSetpoint,
                TimeSpan.FromTicks(DurationTicks), Mode, cycle,
                DigitalOutputs ?? Array.Empty<bool>(), Note);
        }

        private static string? CycleKindOf(CycleAction? c) => c switch
        {
            CycleAction.JumpToCycle => "Jmp",
            CycleAction.EndCycle => "End",
            null => null,
            _ => throw new InvalidOperationException($"Unknown CycleAction subtype {c.GetType().Name}")
        };
    }
}
