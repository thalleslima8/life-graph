using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Persistence;
using LifeGraph.Resources.Contracts;
using Limaj.Framework.Core;

namespace LifeGraph.Resources.Application;

/// <summary>
/// Capture by paste (DA-062, DA-156): the link is checked here, then one
/// <see cref="CaptureResource"/> goes through the single pipeline (DA-013), which finds the
/// Resource the Account already keeps or creates it in the Inbox, with its fetch scheduled in
/// the same transaction (DA-059). Under the Account's capture budget.
/// </summary>
internal sealed class ResourceCaptures(IGraphWriter writer, CaptureAdmission admission, TimeProvider clock)
{
    public async Task<Result<CaptureResourceResponse>> CaptureAsync(Provenance provenance, CaptureResourceRequest request, CancellationToken cancellationToken)
    {
        var address = admission.Admit(request.Url);
        if (!address.IsSuccess)
        {
            return Result<CaptureResourceResponse>.Fail(address.Error!);
        }

        var capture = new CaptureResource(Guid.CreateVersion7(clock.GetUtcNow()), address.Value!.Url) { Title = request.Title };
        var written = await writer.WriteAsync(provenance with { Source = request.Source }, [capture], cancellationToken);

        // Two captures of a new link racing: the unique index refused the second, which now finds the first.
        if (!written.IsSuccess && written.Error!.Code == GraphErrors.ResourceUrlTaken.Code)
        {
            written = await writer.WriteAsync(provenance with { Source = request.Source }, [capture], cancellationToken);
        }

        return written.IsSuccess
            ? Result<CaptureResourceResponse>.Ok(CaptureResourceResponse.Of(written.Value!))
            : Result<CaptureResourceResponse>.Fail(written.Error!);
    }
}

/// <summary>
/// What every capture checks before it writes (DA-150, DA-156): the link, then the Account's
/// capture budget. Without an Account there is no budget to take from, so nothing is captured.
/// </summary>
internal sealed class CaptureAdmission(IResourceAddressRules addressRules, IAccountContext accountContext, CaptureRateLimit rateLimit)
{
    public const string TooManyCapturesMessage = "Too many captures in a short time. Wait and try again.";

    private const string UrlField = "url";

    public Result<ResourceAddress> Admit(string? url)
    {
        var address = addressRules.Read(url, UrlField);
        if (!address.IsSuccess)
        {
            return address;
        }

        if (accountContext.AccountId is not { } accountId)
        {
            return Result<ResourceAddress>.Fail(CommonErrors.Unauthorized.ToError("Sign in to capture a link."));
        }

        return rateLimit.TryTake(accountId)
            ? address
            : Result<ResourceAddress>.Fail(CommonErrors.TooManyRequests.ToError(TooManyCapturesMessage, retryAfter: rateLimit.Window));
    }
}

/// <summary>
/// Capture by an agent (DA-156): the same check and budget as <see cref="ResourceCaptures"/>,
/// then the agent write policy decides between applying and proposing.
/// </summary>
internal sealed class AgentResourceCaptures(IAgentGraphWriter writer, CaptureAdmission admission) : IAgentResourceCaptures
{
    public async Task<Result<ChangeOutcome>> CaptureAsync(AgentResourceCaptureRequest request, CancellationToken cancellationToken)
    {
        var address = admission.Admit(request.Url);
        if (!address.IsSuccess)
        {
            return Result<ChangeOutcome>.Fail(address.Error!);
        }

        var write = new AgentWriteRequest(
            [new AgentCaptureResource(address.Value!.Url)
            {
                Title = request.Title,
                RelatedNodeId = request.RelatedNodeId,
                RelationKind = request.RelationKind,
            }],
            request.Source);
        var written = await writer.WriteAsync(write, cancellationToken);

        // Two captures of a new link racing: the unique index refused the second, which now finds the
        // first. The link was read above, so to an agent that refusal is the only invalid link left (DA-154).
        if (!written.IsSuccess && written.Error!.Code == GraphErrors.ResourceUrlInvalid.Code)
        {
            written = await writer.WriteAsync(write, cancellationToken);
        }

        return written;
    }
}

/// <param name="Url">The link, http or https, without credentials (DA-150).</param>
/// <param name="Title">The title; without one, the fetched title fills it (DA-151).</param>
/// <param name="Source">Where the link came from, kept in the Provenance.</param>
public sealed record CaptureResourceRequest(string? Url, string? Title = null, string? Source = null);

/// <summary>What a capture found (DA-156).</summary>
/// <param name="NodeId">The Resource. An agent's capture of a link kept by a Node hidden from agents names none (DA-154).</param>
/// <param name="Created">The link was new: the Resource was created in the Inbox.</param>
/// <param name="AlreadyExists">The Account already kept the link: this is that Resource.</param>
/// <param name="AlreadyInInbox">It was in the Inbox already, and nothing changed.</param>
/// <param name="ChangeSetId">The GraphChangeSet of the capture, to undo it; none when nothing changed.</param>
public sealed record CaptureResourceResponse(Guid? NodeId, bool Created, bool AlreadyExists, bool AlreadyInInbox, Guid? ChangeSetId)
{
    public static CaptureResourceResponse Of(GraphWriteReceipt receipt)
    {
        var captured = receipt.Captures.Single();
        return new CaptureResourceResponse(captured.NodeId, captured.Created, captured.AlreadyExists, captured.AlreadyInInbox, receipt.ChangeSetId);
    }
}
