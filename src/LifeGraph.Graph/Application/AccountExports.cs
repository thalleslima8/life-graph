using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Jobs;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The person's Account export (DA-247, DA-248): asking for one schedules the job that builds
/// it; the status and the download read it back. The export is the person's alone, like the
/// Account itself: an agent, or anyone else, is refused.
/// </summary>
internal sealed class AccountExports(
    LifeGraphDbContext db,
    GraphReadFilter filter,
    IAccountContext accountContext,
    TimeProvider clock)
{
    private const string InProgressMessage = "An export is still being built.";

    private const string NotReadyMessage = "No export is ready to download.";

    private const string NotOwnerMessage = "Only the person who owns the Account exports it.";

    public async Task<Result<AccountExportView>> RequestAsync(CancellationToken cancellationToken)
    {
        if (RefusedPrincipal<AccountExportView>() is { } refused)
        {
            return refused;
        }

        var accountId = accountContext.AccountId!.Value;
        try
        {
            return await db.InAccountResultTransactionAsync(
                async token =>
                {
                    var now = clock.GetUtcNow();
                    var current = await CurrentAsync(token);
                    if (current is not null && IsBuilding(current.Export, current.BuildStatus))
                    {
                        return Result<AccountExportView>.Fail(GraphErrors.ExportInProgress.ToError(InProgressMessage));
                    }

                    // One export per Account: the new request replaces the previous archive.
                    await db.Set<AccountExport>().Where(export => export.AccountId == accountId).ExecuteDeleteAsync(token);

                    var exportId = Guid.CreateVersion7(now);
                    var build = AccountExportBuild.Schedule(accountId, exportId, now);
                    var export = AccountExport.Request(exportId, accountId, build.Id, now);
                    db.Add(build);
                    db.Add(export);
                    await db.SaveChangesAsync(token);
                    return Result<AccountExportView>.Ok(ViewOf(export, build.Status, now));
                },
                cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two first requests at once: the other one got in.
            return Result<AccountExportView>.Fail(GraphErrors.ExportInProgress.ToError(InProgressMessage));
        }
    }

    public async Task<Result<AccountExportView>> StatusAsync(CancellationToken cancellationToken)
    {
        if (RefusedPrincipal<AccountExportView>() is { } refused)
        {
            return refused;
        }

        return await db.InAccountResultTransactionAsync(
            async token =>
            {
                var current = await CurrentAsync(token);
                return Result<AccountExportView>.Ok(current is null
                    ? AccountExportView.None
                    : ViewOf(current.Export, current.BuildStatus, clock.GetUtcNow()));
            },
            cancellationToken);
    }

    public async Task<Result<AccountExportArchiveFile>> ContentAsync(CancellationToken cancellationToken)
    {
        if (RefusedPrincipal<AccountExportArchiveFile>() is { } refused)
        {
            return refused;
        }

        return await db.InAccountResultTransactionAsync(
            async token =>
            {
                var now = clock.GetUtcNow();
                var ready = await db.Set<AccountExport>().AsNoTracking()
                    .Where(export => export.State == AccountExportState.Ready && export.ExpiresAt > now && export.Content != null)
                    .Select(export => new AccountExportArchiveFile(export.Content!, export.CompletedAt!.Value))
                    .SingleOrDefaultAsync(token);
                return ready is null
                    ? Result<AccountExportArchiveFile>.Fail(GraphErrors.ExportNotFound.ToError(NotReadyMessage))
                    : Result<AccountExportArchiveFile>.Ok(ready);
            },
            cancellationToken);
    }

    // The archive itself is left out: the status never loads the zip.
    private async Task<CurrentExport?> CurrentAsync(CancellationToken cancellationToken) =>
        await db.Set<AccountExport>().AsNoTracking()
            .Select(export => new CurrentExport(
                new CurrentExportRow(export.State, export.Failure, export.OversizedData, export.RequestedAt, export.CompletedAt, export.ExpiresAt, export.SizeBytes),
                db.Set<Job>().Where(job => job.Id == export.BuildJobId).Select(job => (JobStatus?)job.Status).FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);

    private static bool IsBuilding(CurrentExportRow export, JobStatus? buildStatus) =>
        export.State == AccountExportState.Pending && buildStatus is not JobStatus.Dead;

    private static AccountExportView ViewOf(AccountExport export, JobStatus buildStatus, DateTimeOffset now) =>
        ViewOf(new CurrentExportRow(export.State, export.Failure, export.OversizedData, export.RequestedAt, export.CompletedAt, export.ExpiresAt, export.SizeBytes), buildStatus, now);

    private static AccountExportView ViewOf(CurrentExportRow export, JobStatus? buildStatus, DateTimeOffset now)
    {
        // A build that gave up leaves the export pending: it reads as failed for an internal reason.
        if (export.State == AccountExportState.Pending && buildStatus is JobStatus.Dead)
        {
            return new AccountExportView(AccountExportStatus.Failed, AccountExportFailure.Internal, null, export.RequestedAt, null, null, null);
        }

        var expired = export.State == AccountExportState.Expired
            || (export.State == AccountExportState.Ready && export.ExpiresAt <= now);
        return expired
            ? new AccountExportView(AccountExportStatus.Expired, null, null, export.RequestedAt, export.CompletedAt, export.ExpiresAt, null)
            : export.State switch
            {
                AccountExportState.Pending => new AccountExportView(AccountExportStatus.Pending, null, null, export.RequestedAt, null, null, null),
                AccountExportState.Ready => new AccountExportView(AccountExportStatus.Ready, null, null, export.RequestedAt, export.CompletedAt, export.ExpiresAt, export.SizeBytes),
                _ => new AccountExportView(AccountExportStatus.Failed, export.Failure, export.OversizedData, export.RequestedAt, null, null, null),
            };
    }

    // Exporting is the person's alone, as is the Account (DA-247): an agent is refused.
    private Result<T>? RefusedPrincipal<T>() => filter.Reader == GraphReader.Human
        ? null
        : Result<T>.Fail(CommonErrors.Forbidden.ToError(NotOwnerMessage));

    private sealed record CurrentExport(CurrentExportRow Export, JobStatus? BuildStatus);

    private sealed record CurrentExportRow(
        AccountExportState State,
        AccountExportFailure? Failure,
        AccountExportData? OversizedData,
        DateTimeOffset RequestedAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? ExpiresAt,
        long? SizeBytes);
}
