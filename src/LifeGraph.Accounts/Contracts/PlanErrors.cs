using System.Globalization;
using LifeGraph.Infrastructure.Errors;
using Limaj.Framework.Core;

namespace LifeGraph.Accounts.Contracts;

/// <summary>
/// The kinds a plan limits at creation (global_v2 §55), as a refusal and the plan's usage name
/// them. A share past the limit keeps its own refusal, <c>sharing.limit_reached</c> (DA-081).
/// </summary>
public enum PlanLimitKind
{
    Nodes,
    Relations,
    Resources,
    Collections,
    CustomTypes,
    CustomProperties,
    AgentConnections,
    SharedGraphViews,
}

/// <summary>
/// The refusal of a creation past the plan's limit (DA-086): a business rule (DA-101), the same
/// code for every kind, which the details name with the limit. Nothing was created, and what
/// exists stays readable, editable, deletable and exportable.
/// </summary>
public static class PlanErrors
{
    /// <summary>The details key that names the limited kind, in snake_case.</summary>
    public const string KindDetail = "kind";

    /// <summary>The details key that carries the plan's limit of that kind.</summary>
    public const string LimitDetail = "limit";

    public static readonly ErrorCode LimitReached = ErrorCode.BusinessRule("plan.limit_reached", ErrorRecovery.AskUser).WithPublicDetails();

    public static IReadOnlyList<ErrorCode> All { get; } = [LimitReached];

    public static Error LimitReachedError(PlanLimitKind kind, int limit) => LimitReached.ToError(
        $"The plan's limit of {limit} {Describe(kind)} is reached. Nothing was created; delete what is no longer needed to make room.",
        new Dictionary<string, string[]>
        {
            [KindDetail] = [WireNameOf(kind)],
            [LimitDetail] = [limit.ToString(CultureInfo.InvariantCulture)],
        });

    /// <summary>The kind as the details carry it: <c>nodes</c>, <c>custom_types</c>, <c>agent_connections</c>...</summary>
    public static string WireNameOf(PlanLimitKind kind) => kind switch
    {
        PlanLimitKind.Nodes => "nodes",
        PlanLimitKind.Relations => "relations",
        PlanLimitKind.Resources => "resources",
        PlanLimitKind.Collections => "collections",
        PlanLimitKind.CustomTypes => "custom_types",
        PlanLimitKind.CustomProperties => "custom_properties",
        PlanLimitKind.AgentConnections => "agent_connections",
        PlanLimitKind.SharedGraphViews => "shared_graph_views",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown limited kind."),
    };

    private static string Describe(PlanLimitKind kind) => kind switch
    {
        PlanLimitKind.Nodes => "nodes",
        PlanLimitKind.Relations => "relations",
        PlanLimitKind.Resources => "resources",
        PlanLimitKind.Collections => "collections",
        PlanLimitKind.CustomTypes => "custom types",
        PlanLimitKind.CustomProperties => "custom properties",
        PlanLimitKind.AgentConnections => "agent connections",
        PlanLimitKind.SharedGraphViews => "shared links",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown limited kind."),
    };
}
