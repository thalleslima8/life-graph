using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Graph.Persistence;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The single write pipeline (DA-013). It loads what the operations touch, applies them
/// through the domain in order and stores the changes, their GraphChangeSet and the
/// Provenance with one SaveChanges, in one transaction of the current Account. Any failure,
/// expected (a <see cref="Result"/>) or not (an exception), leaves nothing stored.
/// </summary>
internal sealed class GraphWriter(
    LifeGraphDbContext db,
    GraphReadFilter filter,
    IAccountContext accountContext,
    ICurrentPrincipal currentPrincipal,
    IResourceAddressRules addressRules,
    TimeProvider clock) : IGraphWriter
{
    public const int MaxOperations = 100;

    public const string ResourceUrlTakenMessage = "Another resource already keeps this link.";

    public const string NodeChangedMessage = "The node changed after it was read. Read it again and reapply the change.";

    public Task<Result<GraphWriteReceipt>> WriteAsync(
        Provenance provenance,
        IReadOnlyList<GraphOperation> operations,
        CancellationToken cancellationToken)
    {
        var shapeErrors = CheckShape(provenance, operations);
        return RunAsync(
            provenance,
            shapeErrors,
            (accountId, now, token) => WriteInTransactionAsync(new TransactionalWrite(accountId, provenance, operations, now), complete: null, token),
            cancellationToken);
    }

    public Task<Result<GraphWriteReceipt>> UndoAsync(
        Provenance provenance,
        Guid changeSetId,
        CancellationToken cancellationToken)
    {
        var shapeErrors = CheckSource(new FieldErrors(), provenance);
        return RunAsync(
            provenance,
            shapeErrors,
            async (_, now, token) =>
            {
                var undone = await ChangeSetUndo.RunAsync(db, provenance, changeSetId, now, token);
                if (undone.IsSuccess)
                {
                    await SaveAsync(provenance, token);
                }

                return undone;
            },
            cancellationToken);
    }

    public Task<Result<GraphWriteReceipt>> RestoreAsync(
        Provenance provenance,
        GraphEntityKind kind,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        var shapeErrors = CheckSource(new FieldErrors(), provenance);
        return RunAsync(
            provenance,
            shapeErrors,
            async (accountId, now, token) =>
            {
                var restored = await EntityRestore.RunAsync(db, filter, new EntityToRestore(accountId, provenance, kind, entityId, now), token);
                if (restored.IsSuccess && restored.Value!.ChangeSetId is not null)
                {
                    await SaveAsync(provenance, token);
                }

                return restored;
            },
            cancellationToken);
    }

    // One transaction of the current Account around the work, and the translation of what
    // the database refuses: whatever happens, a failed write leaves nothing tracked or stored.
    // The provenance is the caller's; work that writes on another's behalf (the person
    // approving an agent's proposal) builds that Provenance inside.
    internal async Task<Result<T>> RunAsync<T>(
        Provenance provenance,
        FieldErrors shapeErrors,
        Func<Guid, DateTimeOffset, CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken)
    {
        EnsureTheActorIsTheCaller(provenance);

        if (accountContext.AccountId is not { } accountId)
        {
            return Result<T>.Fail(CommonErrors.Unauthorized.ToError("Sign in to change the graph."));
        }

        if (!shapeErrors.IsEmpty)
        {
            return Result<T>.Fail(shapeErrors.ToError());
        }

        // Postgres keeps microseconds: the recorded states must read back as they were written.
        var now = TruncatedToMicroseconds(clock.GetUtcNow());
        try
        {
            var written = await db.InAccountTransactionAsync(token => work(accountId, now, token), cancellationToken);
            if (!written.IsSuccess)
            {
                db.ChangeTracker.Clear();
            }

            return written;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            db.ChangeTracker.Clear();
            return Result<T>.Fail(exception.Entries.All(entry => entry.Entity is Node)
                ? GraphErrors.NodeVersionConflict.ToError(NodeChangedMessage)
                : RaceLostError());
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation,
        } postgres)
        {
            // A race the loading could not see: a name or id taken, or a referenced row gone.
            db.ChangeTracker.Clear();
            return Result<T>.Fail(TranslateRace(postgres, provenance.Actor));
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>
    /// The write itself, inside a transaction <see cref="RunAsync{T}"/> opened. <paramref name="complete"/>
    /// finishes the GraphChangeSet before it is stored (the window count, the proposal it applies).
    /// </summary>
    internal async Task<Result<GraphWriteReceipt>> WriteInTransactionAsync(
        TransactionalWrite transactional,
        Action<GraphChangeSet>? complete,
        CancellationToken cancellationToken)
    {
        var (accountId, provenance, _, now) = transactional;
        var expansion = await ResourceCaptureExpansion.ExpandAsync(db, addressRules, transactional, cancellationToken);
        if (!expansion.IsSuccess)
        {
            return Result<GraphWriteReceipt>.Fail(expansion.Error!);
        }

        var (operations, captures, refetch) = expansion.Value!;
        var workingSet = await GraphWorkingSet.LoadAsync(db, operations, cancellationToken);
        var changeSet = GraphChangeSet.Applied(accountId, provenance, now);
        var write = new WriteInProgress(accountId, provenance, now, workingSet, changeSet, addressRules);
        var invalid = new OperationErrors();

        for (var index = 0; index < operations.Count; index++)
        {
            var applied = Apply(write, operations[index]);

            if (applied.IsSuccess)
            {
                continue;
            }

            // Field errors add up across operations (DA-104); anything else is the answer,
            // unless input errors came first: a later miss may only be their consequence.
            if (applied.Error!.Code == CommonErrors.ValidationFailed.Code)
            {
                invalid.Add(index, applied.Error);
            }
            else if (invalid.IsEmpty)
            {
                return Result<GraphWriteReceipt>.Fail(OperationErrors.OfOperation(index, applied.Error));
            }
        }

        if (!invalid.IsEmpty)
        {
            return Result<GraphWriteReceipt>.Fail(invalid.ToError());
        }

        if (write.Changes.Count == 0)
        {
            // A capture of a link kept in the Inbox changes nothing, yet a failed fetch runs again (DA-156).
            if (refetch.Count > 0)
            {
                ResourceFetchScheduling.ScheduleAgain(db, accountId, refetch, now);
                await db.SaveChangesAsync(cancellationToken);
            }

            return Result<GraphWriteReceipt>.Ok(new GraphWriteReceipt(null, []) { Captures = captures });
        }

        db.AddRange(write.Added);
        db.RemoveRange(write.Removed);
        db.Add(changeSet);
        GraphPurge.ScheduleFor(db, changeSet, now);
        ResourceFetchScheduling.ScheduleFor(db, workingSet, changeSet, now);
        ResourceFetchScheduling.ScheduleAgain(db, accountId, refetch, now);
        complete?.Invoke(changeSet);
        await SaveAsync(provenance, cancellationToken);

        return Result<GraphWriteReceipt>.Ok(new GraphWriteReceipt(changeSet.Id, write.Changes) { Captures = captures });
    }

    /// <summary>
    /// The field errors a write of its operations would answer now, with nothing
    /// stored: the same domain rules (sizes, formats, property values against the Type) run on
    /// the current graph, inside a transaction <see cref="RunAsync{T}"/> opened, and dropped.
    /// What only applying can tell (a version that moved, an entity gone) is left to then, so
    /// an edit or a delete is checked against the Node as it is now. The tracked entities are
    /// cleared: call it before anything the caller tracks.
    /// </summary>
    internal async Task<Result> CheckFieldsInTransactionAsync(TransactionalWrite transactional, CancellationToken cancellationToken)
    {
        var (accountId, provenance, _, now) = transactional;
        var invalid = new OperationErrors();
        try
        {
            var expansion = await ResourceCaptureExpansion.ExpandAsync(db, addressRules, transactional, cancellationToken);
            if (!expansion.IsSuccess)
            {
                return Result.Fail(expansion.Error!);
            }

            var operations = expansion.Value!.Operations;
            var workingSet = await GraphWorkingSet.LoadAsync(db, operations, cancellationToken);
            var write = new WriteInProgress(accountId, provenance, now, workingSet, GraphChangeSet.Applied(accountId, provenance, now), addressRules);
            for (var index = 0; index < operations.Count; index++)
            {
                var operation = operations[index] switch
                {
                    UpdateNode update when workingSet.FindNode(update.NodeId) is { } node => update with { ExpectedVersion = node.Version },
                    DeleteNode delete when workingSet.FindNode(delete.NodeId) is { } node => delete with { ExpectedVersion = node.Version },
                    var other => other,
                };
                var applied = Apply(write, operation);
                if (!applied.IsSuccess && applied.Error!.Code == CommonErrors.ValidationFailed.Code)
                {
                    invalid.Add(index, applied.Error);
                }
            }
        }
        finally
        {
            // The rules ran on tracked entities: none of it may reach a save.
            db.ChangeTracker.Clear();
        }

        return invalid.IsEmpty ? Result.Ok() : Result.Fail(invalid.ToError());
    }

    private static Result Apply(WriteInProgress write, GraphOperation operation) => operation switch
    {
        CreateNode create => write.Apply(create),
        UpdateNode update => write.Apply(update),
        DeleteNode delete => write.Apply(delete),
        DeleteRelation delete => write.Apply(delete),
        CreateRelation create => write.Apply(create),
        DefineProperty define => write.Apply(define),
        CreateType create => write.Apply(create),
        ArchiveNode archive => write.Apply(archive),
        ReturnToInbox back => write.Apply(back),
        UpdatePropertyDefinition update => write.Apply(update),
        DeletePropertyDefinition delete => write.Apply(delete),
        RenameType rename => write.Apply(rename),
        SetTypeHiddenFromAgents setHidden => write.Apply(setHidden),
        AttachProperty attach => write.Apply(attach),
        DetachProperty detach => write.Apply(detach),
        DeleteType delete => write.Apply(delete),
        _ => throw new NotSupportedException($"Unknown graph operation {operation.GetType().Name}."),
    };

    // Every Node and Relation the write changed records who wrote it: anyone but the agent
    // that created it makes it the person's for good (DA-133). Done here, once, so no path of
    // the pipeline (write, Undo, restore, proposal) can forget it. The system's fetch is no
    // one: its metadata keeps the Node the agent's, and its text untrusted (DA-153).
    private async Task SaveAsync(Provenance provenance, CancellationToken cancellationToken)
    {
        await ProjectCanonicalUrlsAsync(cancellationToken);
        if (provenance.Actor is GraphActor.System)
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var writer = (provenance.Actor as GraphActor.AgentIdentity)?.AgentIdentityId;
        foreach (var node in db.ChangeTracker.Entries<Node>().Where(entry => entry.State == EntityState.Modified))
        {
            node.Entity.WrittenBy(writer);
        }

        foreach (var relation in db.ChangeTracker.Entries<Relation>().Where(entry => entry.State == EntityState.Modified))
        {
            relation.Entity.WrittenBy(writer);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // The canonical_url column follows the Resource's value on every path that changes a Node
    // (write, Undo, restore, fetch): it is the projection the unique index stands on (DA-154).
    private async Task ProjectCanonicalUrlsAsync(CancellationToken cancellationToken)
    {
        var nodes = db.ChangeTracker.Entries<Node>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity)
            .ToList();
        if (nodes.Count == 0)
        {
            return;
        }

        var resourceTypeId = await db.Set<NodeType>().AsNoTracking()
            .Where(type => type.SystemKey == SystemOntology.ResourceTypeKey)
            .Select(type => (Guid?)type.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var canonicalId = await db.Set<PropertyDefinition>().AsNoTracking()
            .Where(definition => definition.SystemKey == SystemOntology.ResourcePropertyKeys.CanonicalUrl)
            .Select(definition => (Guid?)definition.Id)
            .SingleOrDefaultAsync(cancellationToken);
        foreach (var node in nodes)
        {
            var canonicalUrl = node.TypeId is { } typeId
                && typeId == resourceTypeId
                && canonicalId is { } propertyId
                && node.Properties.Values.TryGetValue(propertyId, out var value)
                && value.ValueKind == System.Text.Json.JsonValueKind.String
                    ? value.GetString()
                    : null;
            if (node.CanonicalUrl != canonicalUrl)
            {
                node.ProjectCanonicalUrl(canonicalUrl);
            }
        }
    }

    internal static FieldErrors CheckShape(Provenance provenance, IReadOnlyList<GraphOperation> operations)
    {
        var errors = new FieldErrors();
        if (operations.Count == 0 || operations.Count > MaxOperations)
        {
            errors.Add(OperationKeys.List, $"Send between 1 and {MaxOperations} operations.");
        }

        if (operations.Any(operation => operation is null))
        {
            errors.Add(OperationKeys.List, "An operation is missing.");
        }

        return CheckSource(errors, provenance);
    }

    internal static FieldErrors CheckSource(FieldErrors errors, Provenance provenance)
    {
        if (provenance.Source?.Trim().Length > Provenance.SourceMaxLength)
        {
            errors.Add("source", $"Must be at most {Provenance.SourceMaxLength} characters.");
        }

        return errors;
    }

    internal static DateTimeOffset TruncatedToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMicrosecond), value.Offset);

    // The adapter builds the Provenance from the caller; a mismatch is a bug, never a request
    // to attribute the change to someone else.
    private void EnsureTheActorIsTheCaller(Provenance provenance)
    {
        // The application's own writes come only through the job's entry (DA-152, DA-153).
        if (provenance.Actor is GraphActor.System || provenance.Channel == WriteChannel.System)
        {
            throw new InvalidOperationException("The system writes only through the job's entry of the pipeline.");
        }

        var caller = currentPrincipal.Authenticated;
        if (caller is null)
        {
            return;
        }

        var actorType = provenance.Actor is GraphActor.AgentIdentity ? PrincipalType.AgentIdentity : PrincipalType.Human;
        if (actorType != caller.Type)
        {
            throw new InvalidOperationException("The write's actor is not the authenticated caller.");
        }
    }

    // A link another Resource keeps: a Human is told so; an agent gets the answer of a link that
    // cannot be kept, naming neither the Resource nor that one exists, whether it is hidden from
    // agents or not (DA-154). A proposal never reaches the index, so it answers nothing about the
    // link either; its approval is the person's write.
    private static Error TranslateRace(PostgresException postgres, GraphActor actor) => postgres.ConstraintName switch
    {
        GraphConstraints.TypeNameUnique => GraphErrors.TypeNameTaken.ToError(WriteInProgress.TypeNameTakenMessage),
        GraphConstraints.PropertyNameUnique => GraphErrors.PropertyNameTaken.ToError(WriteInProgress.PropertyNameTakenMessage),
        GraphConstraints.NodeCanonicalUrlUnique when actor is GraphActor.AgentIdentity =>
            GraphErrors.ResourceUrlInvalid.ToError(GraphErrors.ResourceUrlInvalidMessage),
        GraphConstraints.NodeCanonicalUrlUnique => GraphErrors.ResourceUrlTaken.ToError(ResourceUrlTakenMessage),
        _ => RaceLostError(),
    };

    private static Error RaceLostError() => GraphErrors.WriteConflict.ToError("Another change got in first. Send the write again.");
}

/// <summary>
/// A write of the current Account inside a transaction <see cref="GraphWriter.RunAsync{T}"/>
/// opened: who writes, what, and the transaction's now.
/// </summary>
internal sealed record TransactionalWrite(Guid AccountId, Provenance Provenance, IReadOnlyList<GraphOperation> Operations, DateTimeOffset Now);
