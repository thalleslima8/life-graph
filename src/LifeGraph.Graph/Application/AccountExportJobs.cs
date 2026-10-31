using System.Text.Json;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LifeGraph.Graph.Application;

/// <summary>
/// Builds the archive of an Account export (DA-248). The read runs in one read-only snapshot
/// outside the runner's transaction, which has already run queries and can no longer change
/// its isolation; the apply phase stores the result and schedules the archive's expiry. An
/// export replaced or removed in the meantime makes the job a no-op.
/// </summary>
internal sealed class AccountExportBuild(
    LifeGraphDbContext db,
    AccountExportReader reader,
    IOptions<AccountExportOptions> options,
    TimeProvider clock) : ITwoPhaseJobHandler
{
    public const string JobKind = "graph.export";

    public string Kind => JobKind;

    public static Job Schedule(Guid accountId, Guid exportId, DateTimeOffset now) => Job.Schedule(
        accountId,
        JobKind,
        JsonSerializer.Serialize(new AccountExportPayload(exportId), AccountExportPayload.Options),
        now,
        now);

    public async Task<JobApplyPhase> RunOutsideTransactionAsync(string payload, CancellationToken cancellationToken)
    {
        var request = AccountExportPayload.Read(payload);
        var accountId = await db.InAccountTransactionAsync(
            token => db.Set<AccountExport>().AsNoTracking()
                .Where(export => export.Id == request.ExportId && export.State == AccountExportState.Pending)
                .Select(export => (Guid?)export.AccountId)
                .SingleOrDefaultAsync(token),
            cancellationToken);
        if (accountId is null)
        {
            return _ => Task.CompletedTask;
        }

        // One snapshot for the whole read (DA-248, DA-256); a read past the cap stops there and
        // the snapshot is released with nothing kept.
        var budget = new AccountExportSizeBudget(options.Value.MaxArchiveBytes);
        var archive = await db.InAccountSnapshotAsync(token => reader.BuildArchiveAsync(accountId.Value, budget, token), cancellationToken);

        return async token =>
        {
            var export = await db.Set<AccountExport>().SingleOrDefaultAsync(entity => entity.Id == request.ExportId, token);
            if (export is null)
            {
                return;
            }

            var now = clock.GetUtcNow();
            if (budget.OversizedData is { } oversizedData)
            {
                export.FailOverSizeCap(oversizedData, now);
            }
            else if (archive is not null && export.Complete(archive, options.Value.Lifetime, now))
            {
                db.Add(AccountExportExpiry.Schedule(export));
            }
        };
    }
}

/// <summary>Drops a ready archive once its lifetime is over (DA-248); reads already see it expired.</summary>
internal sealed class AccountExportExpiry(LifeGraphDbContext db, TimeProvider clock) : IJobHandler
{
    public const string JobKind = "graph.export_expiry";

    public string Kind => JobKind;

    public static Job Schedule(AccountExport export) => Job.Schedule(
        export.AccountId,
        JobKind,
        JsonSerializer.Serialize(new AccountExportPayload(export.Id), AccountExportPayload.Options),
        export.ExpiresAt ?? export.RequestedAt,
        export.CompletedAt ?? export.RequestedAt);

    public async Task RunAsync(string payload, CancellationToken cancellationToken)
    {
        var request = AccountExportPayload.Read(payload);
        var export = await db.Set<AccountExport>().SingleOrDefaultAsync(entity => entity.Id == request.ExportId, cancellationToken);
        export?.Expire(clock.GetUtcNow());
    }
}

internal sealed record AccountExportPayload(Guid ExportId)
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerOptions.Web);

    private const string EmptyPayloadMessage = "Empty account export payload.";

    public static AccountExportPayload Read(string payload) =>
        JsonSerializer.Deserialize<AccountExportPayload>(payload, Options) ?? throw new JsonException(EmptyPayloadMessage);
}
