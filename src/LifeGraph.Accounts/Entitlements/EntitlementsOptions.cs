using System.ComponentModel.DataAnnotations;
using LifeGraph.Accounts.Contracts;

namespace LifeGraph.Accounts.Entitlements;

/// <summary>
/// The limits of each plan, read from configuration (global_v2 §55: "devem ser configuráveis").
/// The defaults are the §55 table; where it says "fair use", a high number stands in, never "no
/// limit". These are plan limits, apart from the security limits of agent writes (DA-087).
/// </summary>
public sealed class EntitlementsOptions : IValidatableObject
{
    public const string SectionName = "Entitlements";

    public PlanLimitsOptions Free { get; set; } = new()
    {
        Nodes = 500,
        Relations = 2_000,
        Resources = 100,
        Collections = 10,
        CustomTypes = 5,
        CustomProperties = 20,
        AgentConnections = 1,
        AgentWriteOperationsPerMonth = 100,
        SharedGraphViews = 3,
        NodesPerSharedGraphView = 25,
    };

    public PlanLimitsOptions Premium { get; set; } = new()
    {
        Nodes = 10_000,
        Relations = 50_000,
        Resources = 5_000,
        Collections = 1_000,
        CustomTypes = 200,
        CustomProperties = 1_000,
        AgentConnections = 10,
        AgentWriteOperationsPerMonth = 10_000,
        SharedGraphViews = 100,
        NodesPerSharedGraphView = 250,
    };

    public PlanLimits For(AccountPlan plan) => plan switch
    {
        AccountPlan.Free => Free.ToLimits(),
        AccountPlan.Premium => Premium.ToLimits(),
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan."),
    };

    // The data annotations validator does not descend into nested objects: each plan is
    // validated here, so a bad value in either block fails the start (GEN-052).
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (name, limits) in new[] { (nameof(Free), Free), (nameof(Premium), Premium) })
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(limits, new ValidationContext(limits), results, validateAllProperties: true);
            foreach (var result in results)
            {
                yield return new ValidationResult(
                    $"{SectionName}:{name}: {result.ErrorMessage}",
                    [.. result.MemberNames.Select(member => $"{name}.{member}")]);
            }
        }
    }
}

/// <summary>One plan's block of <see cref="EntitlementsOptions"/>; zero means none may be created.</summary>
public sealed class PlanLimitsOptions
{
    /// <summary>No configured limit goes past this: a typo of a few zeros fails the start instead.</summary>
    public const int Ceiling = 1_000_000;

    [Range(0, Ceiling)]
    public int Nodes { get; set; }

    [Range(0, Ceiling)]
    public int Relations { get; set; }

    [Range(0, Ceiling)]
    public int Resources { get; set; }

    [Range(0, Ceiling)]
    public int Collections { get; set; }

    [Range(0, Ceiling)]
    public int CustomTypes { get; set; }

    [Range(0, Ceiling)]
    public int CustomProperties { get; set; }

    [Range(0, Ceiling)]
    public int AgentConnections { get; set; }

    [Range(0, Ceiling)]
    public int AgentWriteOperationsPerMonth { get; set; }

    [Range(0, Ceiling)]
    public int SharedGraphViews { get; set; }

    [Range(0, Ceiling)]
    public int NodesPerSharedGraphView { get; set; }

    public PlanLimits ToLimits() => new(
        Nodes,
        Relations,
        Resources,
        Collections,
        CustomTypes,
        CustomProperties,
        AgentConnections,
        AgentWriteOperationsPerMonth,
        SharedGraphViews,
        NodesPerSharedGraphView);
}
