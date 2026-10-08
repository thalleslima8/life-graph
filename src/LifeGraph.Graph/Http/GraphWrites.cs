using System.Text.RegularExpressions;
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
    private static readonly Regex OperationPrefix = new(@"^operations\[\d+\]\.", RegexOptions.CultureInvariant);

    public IHttpResultResponder Responder => responder;

    public Task<IResult> WriteAsync(GraphOperation operation, CancellationToken cancellationToken) =>
        WriteAsync(operation, receipt => TypedResults.Ok(receipt), cancellationToken);

    public Task<IResult> WriteAsync(
        GraphOperation operation,
        Func<GraphWriteReceipt, IResult> onWritten,
        CancellationToken cancellationToken) =>
        WriteAsync([operation], onWritten, cancellationToken);

    /// <summary>
    /// The operations of one request, in one GraphChangeSet. Each request field maps to a
    /// single operation, so the errors are named as the request names them.
    /// </summary>
    public async Task<IResult> WriteAsync(
        IReadOnlyList<GraphOperation> operations,
        Func<GraphWriteReceipt, IResult> onWritten,
        CancellationToken cancellationToken)
    {
        if (ProvenanceOfCaller() is not { } provenance)
        {
            return responder.Fail(NotThePerson());
        }

        var written = await writer.WriteAsync(provenance, operations, cancellationToken);
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

    private static Result<GraphWriteReceipt> WithoutOperationPrefix(Result<GraphWriteReceipt> written)
    {
        if (written.IsSuccess || written.Error!.Details is not { Count: > 0 } details)
        {
            return written;
        }

        var renamed = details.ToDictionary(
            pair => OperationPrefix.Replace(pair.Key, string.Empty),
            pair => pair.Value,
            StringComparer.Ordinal);
        var error = written.Error;
        return Result<GraphWriteReceipt>.Fail(new Error(error.Code, error.Message, error.Type, renamed) { RetryAfter = error.RetryAfter });
    }
}
