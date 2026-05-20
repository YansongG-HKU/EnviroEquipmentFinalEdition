using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SiemensS7Demo.Domain.Programs.Abstractions;

/// <summary>
/// Persistence boundary for <see cref="Program"/> values. The editor first calls
/// <see cref="SaveDraftAsync"/> with a working copy (overwrites the draft slot per name);
/// when the user commits, <see cref="SaveAsync"/> writes the canonical row. Draft slots
/// live in the same table — drafts are programs whose name starts with the
/// <c>draft:</c> prefix internally. Implementations must be safe to call concurrently from
/// the editor and the execution service.
/// </summary>
/// <remarks>
/// Placed in the Domain project (not App) so the Persistence implementation can reference
/// the interface from the same direction as the Domain types it serializes, without
/// inverting the dependency arrow (App does not reference Persistence).
/// </remarks>
public interface IProgramRepository
{
    Task SaveAsync(Program program, CancellationToken ct);
    Task SaveDraftAsync(Program program, CancellationToken ct);
    Task<Program?> GetAsync(string name, CancellationToken ct);
    Task<Program?> GetDraftAsync(string name, CancellationToken ct);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct);
    Task DeleteAsync(string name, CancellationToken ct);
}
