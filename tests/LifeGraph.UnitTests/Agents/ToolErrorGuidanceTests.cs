using System.Globalization;
using LifeGraph.Accounts.Contracts;
using LifeGraph.Agents.Mcp;
using LifeGraph.Graph.Contracts;
using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.UnitTests.Agents;

/// <summary>What a refused tool call tells the agent to do next (DA-286): fixed text that names the tool to call.</summary>
public sealed class ToolErrorGuidanceTests
{
    private const string Canary = "CANARY-7f3a node content";

    public static TheoryData<string, string> CodesAndTheToolTheyName => new()
    {
        { CommonErrors.ValidationFailed.Code, "arguments" },
        { GraphErrors.NodeNotFound.Code, GraphReadTools.SearchGraphName },
        { GraphErrors.RelationNotFound.Code, GraphReadTools.GetNodeName },
        { GraphErrors.TypeNotFound.Code, GraphReadTools.ListTypesName },
        { GraphErrors.PropertyDefinitionNotFound.Code, GraphReadTools.ListTypesName },
        { GraphErrors.NodeVersionConflict.Code, GraphReadTools.GetNodeName },
        { GraphErrors.RootMissing.Code, "person" },
        { GraphErrors.ResourceUrlInvalid.Code, "person" },
        { AgentWriteErrors.ProposalNotFound.Code, GraphWriteTools.SuggestChangesName },
        { AgentWriteErrors.ProposalNotPending.Code, GraphWriteTools.GetProposalName },
        { AgentWriteErrors.ProposalQueueFull.Code, GraphWriteTools.WithdrawProposalName },
        { PlanErrors.LimitReached.Code, "person" },
    };

    [Theory]
    [MemberData(nameof(CodesAndTheToolTheyName))]
    public void Each_mapped_code_says_what_to_do_next_and_never_repeats_the_refusal_content(string code, string named)
    {
        var guidance = ToolErrorGuidance.NextActionFor(ErrorOf(code));

        Assert.NotNull(guidance);
        Assert.Contains(named, guidance, StringComparison.Ordinal);
        Assert.DoesNotContain(Canary, guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void A_not_found_code_without_an_entry_falls_back_to_finding_the_id_again() =>
        Assert.Contains(
            GraphReadTools.SearchGraphName,
            ToolErrorGuidance.NextActionFor(GraphErrors.CollectionNotFound.ToError(Canary)),
            StringComparison.Ordinal);

    [Fact]
    public void A_code_whose_message_already_says_what_to_do_gets_no_guidance()
    {
        Assert.Null(ToolErrorGuidance.NextActionFor(GraphErrors.AnchorRequired.ToError(Canary)));
        Assert.Null(ToolErrorGuidance.NextActionFor(AgentWriteErrors.ProposalRequiresUser.ToError(Canary)));
    }

    [Fact]
    public void Too_many_requests_says_how_long_to_wait_in_whole_seconds()
    {
        var withWait = ToolErrorGuidance.NextActionFor(CommonErrors.TooManyRequests.ToError(Canary, retryAfter: TimeSpan.FromMilliseconds(1_200)));
        var withoutWait = ToolErrorGuidance.NextActionFor(CommonErrors.TooManyRequests.ToError(Canary));

        Assert.StartsWith("Wait 2 seconds ", withWait, StringComparison.Ordinal);
        Assert.StartsWith("Wait before ", withoutWait, StringComparison.Ordinal);
    }

    // DA-308: the descriptions tell the agent the limits it really meets by default.
    [Fact]
    public void The_limits_told_in_the_descriptions_are_the_default_agent_write_limits()
    {
        var defaults = new AgentWriteOptions();

        Assert.Equal(GraphWriteTools.DefaultMaxOperationsText, defaults.MaxOperations.ToString(CultureInfo.InvariantCulture));
        Assert.Equal(GraphWriteTools.DefaultBulkThresholdText, defaults.BulkThreshold.ToString(CultureInfo.InvariantCulture));
    }

    private static Error ErrorOf(string code) =>
        new[] { CommonErrors.All, GraphErrors.All, AgentWriteErrors.All, PlanErrors.All }
            .SelectMany(codes => codes)
            .Single(entry => entry.Code == code)
            .ToError(Canary);
}
