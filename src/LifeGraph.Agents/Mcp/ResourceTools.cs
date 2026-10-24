using System.ComponentModel;
using LifeGraph.Graph.Contracts;
using LifeGraph.Resources.Contracts;
using Limaj.Framework.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LifeGraph.Agents.Mcp;

/// <summary>
/// <c>capture_resource</c> (DA-156), a thin adapter over <see cref="IAgentResourceCaptures"/>:
/// the link check, the Account's capture budget and the agent write policy live behind it. It
/// needs <c>lifegraph.write</c> (DA-122), checked before it runs. The answer names the Resource
/// and never carries fetched text (DA-038); a link the Account keeps in a Node hidden from
/// agents answers without a Resource (DA-154).
/// </summary>
[McpServerToolType]
public sealed class ResourceTools
{
    public const string CaptureResourceName = "capture_resource";

    [McpServerTool(Name = CaptureResourceName, Title = "Capturar um link", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Saves a link (http or https) to the person's Inbox as a Resource; Life Graph fetches its title and description afterwards."
        + " Capturing a link the person already keeps answers that same Resource (`alreadyExists`), back in the Inbox if it had left it, so it is safe to retry."
        + " `relatedNodeId` relates the Resource to a Node in the same change, which the person can undo at once."
        + " The answer may have no `nodeId`: then the link is kept where you cannot see it, and nothing changed."
        + " When the change needs the person's approval, the answer has status \"" + GraphWriteTools.PendingApprovalStatus + "\" and nothing changed yet.")]
    public static async Task<CallToolResult> CaptureResourceAsync(
        IAgentResourceCaptures captures,
        [Description("The link, http or https, without user name or password.")] string url,
        [Description("A title for a new Resource; without one, the page's title fills it.")] string? title = null,
        [Description("Where the link came from (the conversation, a page), shown to the person. Optional.")] string? source = null,
        [Description("A Node to relate the Resource to (an id from search or get_node).")] Guid? relatedNodeId = null,
        [Description("What that relation means, such as related_to (the default) or written_by.")] string? relationKind = null,
        CancellationToken cancellationToken = default) =>
        AnswerOf(await captures.CaptureAsync(
            new AgentResourceCaptureRequest(url, title, source, relatedNodeId, relationKind),
            cancellationToken));

    /// <summary>
    /// Applied: what the capture found, the same fields whether the link was new, kept or out
    /// of the agent's sight (DA-154). Pending or refused: as every write tool answers (DA-130).
    /// </summary>
    private static CallToolResult AnswerOf(Result<ChangeOutcome> outcome)
    {
        if (outcome is not { IsSuccess: true, Value: ChangeOutcome.Applied applied })
        {
            return GraphWriteTools.AnswerOf(outcome);
        }

        var captured = applied.Receipt.Captures.Single();
        return GraphReadTools.AnswerOf(Result<object>.Ok(new CaptureAnswer(
            GraphWriteTools.AppliedStatus,
            captured.NodeId,
            captured.Created,
            captured.AlreadyExists,
            captured.AlreadyInInbox,
            applied.Receipt.ChangeSetId)));
    }

    private sealed record CaptureAnswer(
        string Status,
        Guid? NodeId,
        bool Created,
        bool AlreadyExists,
        bool AlreadyInInbox,
        Guid? ChangeSetId);
}
