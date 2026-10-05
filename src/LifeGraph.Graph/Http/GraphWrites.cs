using LifeGraph.Graph.Contracts;
using LifeGraph.Http;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Identity;
using Limaj.Framework.Core;
using Limaj.Framework.Web.Http;
using Microsoft.AspNetCore.Http;

namespace LifeGraph.Graph.Http;

/// <summary>
/// What every write endpoint shares: the Provenance of the caller, one operation through the
/// single pipeline (DA-013) and the receipt, whose GraphChangeSet id the UI offers to undo.
/// </summary>
internal sealed class GraphWrites(IGraphWriter writer, ICurrentPrincipal currentPrincipal, IHttpResultResponder responder)
{
    private const string OnlyOperation = "operations[0].";

    public IHttpResultResponder Responder => responder;

    public Task<IResult> WriteAsync(GraphOperation operation, CancellationToken cancellationToken) =>
        WriteAsync(operation, receipt => TypedResults.Ok(receipt), cancellationToken);

    public async Task<IResult> WriteAsync(
        GraphOperation operation,
        Func<GraphWriteReceipt, IResult> onWritten,
        CancellationToken cancellationToken)
    {
        if (ProvenanceOfCaller() is not { } provenance)
        {
            return responder.Fail(NotThePerson());
        }

        var written = await writer.WriteAsync(provenance, [operation], cancellationToken);
        return responder.ToHttpResult(WithoutOperationPrefix(written), onWritten);
    }

    public async Task<IResult> UndoAsync(Guid changeSetId, CancellationToken cancellationToken)
    {
        if (ProvenanceOfCaller() is not { } provenance)
        {
            return responder.Fail(NotThePerson());
        }

        var undone = await writer.UndoAsync(provenance, changeSetId, cancellationToken);
        return responder.ToHttpResult(undone, receipt => TypedResults.Ok(receipt));
    }

    // The session is the person in the SPA. Agents write through MCP, with their own
    // Provenance, from E3 on.
    private Provenance? ProvenanceOfCaller() =>
        currentPrincipal.Authenticated is { Type: PrincipalType.Human }
            ? new Provenance(new GraphActor.Human(), WriteChannel.Ui)
            : null;

    private static Error NotThePerson() => CommonErrors.Forbidden.ToError("Only the person who owns the Account writes here.");

    // A request carries one operation, so its fields are named as the request names them.
    private static Result<GraphWriteReceipt> WithoutOperationPrefix(Result<GraphWriteReceipt> written)
    {
        if (written.IsSuccess || written.Error!.Details is not { Count: > 0 } details)
        {
            return written;
        }

        var renamed = details.ToDictionary(
            pair => pair.Key.StartsWith(OnlyOperation, StringComparison.Ordinal) ? pair.Key[OnlyOperation.Length..] : pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        var error = written.Error;
        return Result<GraphWriteReceipt>.Fail(new Error(error.Code, error.Message, error.Type, renamed) { RetryAfter = error.RetryAfter });
    }
}
