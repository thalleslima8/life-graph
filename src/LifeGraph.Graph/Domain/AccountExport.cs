namespace LifeGraph.Graph.Domain;

/// <summary>
/// The Account export the person asked for (DA-247, DA-248): a job builds the archive, which
/// stays here for a short while and then expires, so the copy of the data at rest does not
/// outlive its use. One per Account: a new request replaces the previous one.
/// </summary>
public sealed class AccountExport
{
    private AccountExport()
    {
    }

    public Guid Id { get; private set; }

    public Guid AccountId { get; private set; }

    public AccountExportState State { get; private set; }

    /// <summary>Why a <see cref="AccountExportState.Failed"/> export failed; <c>null</c> otherwise.</summary>
    public AccountExportFailure? Failure { get; private set; }

    /// <summary>
    /// The data that pushed the archive past the size cap, only on a <see cref="AccountExportFailure.SizeCap"/>
    /// failure (DA-256), so the person learns what makes the export too large.
    /// </summary>
    public AccountExportData? OversizedData { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    /// <summary>When the archive was built, or the export failed.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>When a ready archive stops being downloadable and is dropped.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>The zip, while ready; dropped when it expires.</summary>
    public byte[]? Content { get; private set; }

    public long? SizeBytes { get; private set; }

    /// <summary>
    /// The job that builds the archive: when it gives up, the export reads as failed for an
    /// internal reason, with no second state to keep in step.
    /// </summary>
    public Guid BuildJobId { get; private set; }

    public static AccountExport Request(Guid id, Guid accountId, Guid buildJobId, DateTimeOffset now) => new()
    {
        Id = id,
        AccountId = accountId,
        BuildJobId = buildJobId,
        State = AccountExportState.Pending,
        RequestedAt = now,
    };

    /// <summary>The archive is built; it can be downloaded until <paramref name="lifetime"/> passes.</summary>
    public bool Complete(byte[] content, TimeSpan lifetime, DateTimeOffset now)
    {
        if (State != AccountExportState.Pending)
        {
            return false;
        }

        State = AccountExportState.Ready;
        Content = content;
        SizeBytes = content.LongLength;
        CompletedAt = now;
        ExpiresAt = now + lifetime;
        return true;
    }

    /// <summary>
    /// The archive would pass the size cap (DA-248, DA-256): it fails whole, never truncated,
    /// naming the data that pushed it over.
    /// </summary>
    public bool FailOverSizeCap(AccountExportData oversizedData, DateTimeOffset now)
    {
        if (State != AccountExportState.Pending)
        {
            return false;
        }

        State = AccountExportState.Failed;
        Failure = AccountExportFailure.SizeCap;
        OversizedData = oversizedData;
        CompletedAt = now;
        return true;
    }

    /// <summary>Drops the archive once its lifetime is over; before that, nothing changes.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (State != AccountExportState.Ready || ExpiresAt > now)
        {
            return false;
        }

        State = AccountExportState.Expired;
        Content = null;
        return true;
    }

    /// <summary>A ready archive past its lifetime is already expired for every read, even before its job runs.</summary>
    public AccountExportState StateAt(DateTimeOffset now) =>
        State == AccountExportState.Ready && ExpiresAt <= now ? AccountExportState.Expired : State;
}

public enum AccountExportState
{
    Pending,
    Ready,
    Failed,
    Expired,
}

public enum AccountExportFailure
{
    /// <summary>The archive would be larger than the configured cap.</summary>
    SizeCap,

    /// <summary>The build kept failing until its job gave up.</summary>
    Internal,
}

/// <summary>The kinds of data the archive holds, in the order it writes them (DA-256).</summary>
public enum AccountExportData
{
    PropertyDefinitions,
    Types,
    Nodes,
    Relations,
    Tags,
    Collections,
    ChangeSets,
    ChangeProposals,
    AgentIdentities,
    AgentReads,
    SharedGraphViews,

    /// <summary>The Markdown copy of each Node (<c>nodes/{id}.md</c>).</summary>
    NodeMarkdown,
}
