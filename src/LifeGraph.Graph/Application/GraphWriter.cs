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
    IAccountContext accountContext,
    ICurrentPrincipal currentPrincipal,
    TimeProvider clock) : IGraphWriter
{
    public const int MaxOperations = 100;

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
            (accountId, now, token) => WriteInTransactionAsync(accountId, provenance, operations, now, token),
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
                    await db.SaveChangesAsync(token);
                }

                return undone;
            },
            cancellationToken);
    }

    // One transaction of the current Account around the work, and the translation of what
    // the database refuses: whatever happens, a failed write leaves nothing tracked or stored.
    private async Task<Result<GraphWriteReceipt>> RunAsync(
        Provenance provenance,
        FieldErrors shapeErrors,
        Func<Guid, DateTimeOffset, CancellationToken, Task<Result<GraphWriteReceipt>>> work,
        CancellationToken cancellationToken)
    {
        EnsureTheActorIsTheCaller(provenance);

        if (accountContext.AccountId is not { } accountId)
        {
            return Result<GraphWriteReceipt>.Fail(CommonErrors.Unauthorized.ToError("Sign in to change the graph."));
        }

        if (!shapeErrors.IsEmpty)
        {
            return Result<GraphWriteReceipt>.Fail(shapeErrors.ToError());
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
            return Result<GraphWriteReceipt>.Fail(exception.Entries.All(entry => entry.Entity is Node)
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
            return Result<GraphWriteReceipt>.Fail(TranslateRace(postgres));
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<Result<GraphWriteReceipt>> WriteInTransactionAsync(
        Guid accountId,
        Provenance provenance,
        IReadOnlyList<GraphOperation> operations,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var workingSet = await GraphWorkingSet.LoadAsync(db, operations, cancellationToken);
        var changeSet = GraphChangeSet.Applied(accountId, provenance, now);
        var write = new WriteInProgress(accountId, provenance, now, workingSet, changeSet);
        var invalid = new OperationErrors();

        for (var index = 0; index < operations.Count; index++)
        {
            var applied = operations[index] switch
            {
                CreateNode create => write.Apply(create),
                UpdateNode update => write.Apply(update),
                DeleteNode delete => write.Apply(delete),
                DeleteRelation delete => write.Apply(delete),
                CreateRelation create => write.Apply(create),
                DefineProperty define => write.Apply(define),
                CreateType create => write.Apply(create),
                ArchiveNode archive => write.Apply(archive),
                UpdatePropertyDefinition update => write.Apply(update),
                DeletePropertyDefinition delete => write.Apply(delete),
                RenameType rename => write.Apply(rename),
                AttachProperty attach => write.Apply(attach),
                DetachProperty detach => write.Apply(detach),
                DeleteType delete => write.Apply(delete),
                _ => throw new NotSupportedException($"Unknown graph operation {operations[index].GetType().Name}."),
            };

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
            return Result<GraphWriteReceipt>.Ok(new GraphWriteReceipt(null, []));
        }

        db.AddRange(write.Added);
        db.RemoveRange(write.Removed);
        db.Add(changeSet);
        GraphPurge.ScheduleFor(db, changeSet, now);
        await db.SaveChangesAsync(cancellationToken);

        return Result<GraphWriteReceipt>.Ok(new GraphWriteReceipt(changeSet.Id, write.Changes));
    }

    private static FieldErrors CheckShape(Provenance provenance, IReadOnlyList<GraphOperation> operations)
    {
        var errors = new FieldErrors();
        if (operations.Count == 0 || operations.Count > MaxOperations)
        {
            errors.Add("operations", $"Send between 1 and {MaxOperations} operations.");
        }

        if (operations.Any(operation => operation is null))
        {
            errors.Add("operations", "An operation is missing.");
        }

        return CheckSource(errors, provenance);
    }

    private static FieldErrors CheckSource(FieldErrors errors, Provenance provenance)
    {
        if (provenance.Source?.Trim().Length > Provenance.SourceMaxLength)
        {
            errors.Add("source", $"Must be at most {Provenance.SourceMaxLength} characters.");
        }

        return errors;
    }

    private static DateTimeOffset TruncatedToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMicrosecond), value.Offset);

    // The adapter builds the Provenance from the caller; a mismatch is a bug, never a request
    // to attribute the change to someone else.
    private void EnsureTheActorIsTheCaller(Provenance provenance)
    {
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

    private static Error TranslateRace(PostgresException postgres) => postgres.ConstraintName switch
    {
        GraphConstraints.TypeNameUnique => GraphErrors.TypeNameTaken.ToError(WriteInProgress.TypeNameTakenMessage),
        GraphConstraints.PropertyNameUnique => GraphErrors.PropertyNameTaken.ToError(WriteInProgress.PropertyNameTakenMessage),
        _ => RaceLostError(),
    };

    private static Error RaceLostError() => GraphErrors.WriteConflict.ToError("Another change got in first. Send the write again.");
}
