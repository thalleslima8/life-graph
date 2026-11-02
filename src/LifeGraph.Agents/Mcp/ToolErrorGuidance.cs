using System.Globalization;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.Agents.Mcp;

/// <summary>
/// What an agent should do next after a refused tool call (DA-286), appended to the refusal's
/// public message. It lives in the adapter because it names MCP tools, which the modules (and
/// the REST API that shares their messages) must not know. Every sentence is fixed text: it
/// never carries a title, a body, a Tag, a URL or any other content of the person (GEN-043).
/// A code whose message already says what to do (graph.anchor_required, graph.tag_not_found,
/// graph.write_conflict, agents.proposal_requires_user, agents.proposal_rejected_before) has no entry.
/// </summary>
internal static class ToolErrorGuidance
{
    private static readonly IReadOnlyDictionary<string, string> ByCode = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [CommonErrors.ValidationFailed.Code] = "Fix the arguments named above and call again.",
        [GraphErrors.NodeNotFound.Code] =
            "Find the Node's id with " + GraphReadTools.SearchGraphName + " or " + GraphReadTools.GetContextName + ", then call again with it.",
        [GraphErrors.RelationNotFound.Code] =
            "Find the Relation's id with " + GraphReadTools.GetNodeName + " or " + GraphReadTools.GetContextName + ", then call again with it.",
        [GraphErrors.TypeNotFound.Code] = "Take the Type's id from " + GraphReadTools.ListTypesName + " and call again.",
        [GraphErrors.PropertyDefinitionNotFound.Code] =
            "Take the property ids of the Type from " + GraphReadTools.ListTypesName + " and call again.",
        [GraphErrors.NodeVersionConflict.Code] =
            "Read the Node again with " + GraphReadTools.GetNodeName + " and send the change with the version it has now.",
        [GraphErrors.RootMissing.Code] = "Tell the person to sign in to Life Graph, then call again.",
        [GraphErrors.ResourceUrlInvalid.Code] = "Do not try again with this link; tell the person if they asked you to keep it.",
        [AgentWriteErrors.ProposalNotFound.Code] =
            "Use the proposal id that " + GraphWriteTools.SuggestChangesName + " answered you; a proposal of another agent reads as not found.",
        [AgentWriteErrors.ProposalNotPending.Code] =
            "Call " + GraphWriteTools.GetProposalName + " to see what became of it; if the change is still wanted, propose it again with "
            + GraphWriteTools.SuggestChangesName + ".",
        [AgentWriteErrors.ProposalQueueFull.Code] =
            "Withdraw the proposals you no longer need with " + GraphWriteTools.WithdrawProposalName + ".",
        [PlanErrors.LimitReached.Code] = "Tell the person; do not try again until they make room.",
    };

    // A fallback by kind, looked up rather than switched over, so a new ErrorType needs no case here.
    private static readonly IReadOnlyDictionary<ErrorType, string> ByType = new Dictionary<ErrorType, string>
    {
        [ErrorType.NotFound] = "Check the id: find it again with " + GraphReadTools.SearchGraphName + " or " + GraphReadTools.GetContextName + ".",
        [ErrorType.Unexpected] = "Call once more; if it fails again, tell the person.",
    };

    /// <summary>The next action for <paramref name="error"/>, or null when its message already says it.</summary>
    public static string? NextActionFor(Error error)
    {
        if (error.Type == ErrorType.TooManyRequests)
        {
            return WaitFor(error.RetryAfter);
        }

        return ByCode.TryGetValue(error.Code, out var byCode) ? byCode
            : ByType.TryGetValue(error.Type, out var byType) ? byType
            : null;
    }

    private static string WaitFor(TimeSpan? retryAfter) =>
        retryAfter is { } wait
            ? "Wait " + Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture)
              + " seconds before you call again; calling sooner is refused the same way."
            : "Wait before you call again; calling sooner is refused the same way.";
}
