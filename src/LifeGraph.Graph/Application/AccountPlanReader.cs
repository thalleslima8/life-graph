using LifeGraph.Accounts.Contracts;
using LifeGraph.Graph.Domain;
using LifeGraph.Infrastructure.Errors;
using LifeGraph.Infrastructure.Persistence;
using Limaj.Framework.Core;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Application;

/// <summary>
/// The person's plan, its limits and how much of each the Account uses (E13, DA-266), for the
/// "Plano e uso" section. Read-only, never priced and never selling Premium (DA-260): it shows
/// where the Account stands, and a limit stops only new creation (DA-086). The person's alone,
/// like the export.
/// </summary>
internal sealed class AccountPlanReader(
    LifeGraphDbContext db,
    GraphReadFilter filter,
    GraphUsage usage,
    IEntitlements entitlements,
    IAgentIdentities agentIdentities,
    TimeProvider clock)
{
    public Task<Result<AccountPlanView>> ReadAsync(CancellationToken cancellationToken)
    {
        if (filter.Reader != GraphReader.Human)
        {
            return Task.FromResult(Result<AccountPlanView>.Fail(CommonErrors.Forbidden.ToError("Only the person who owns the Account sees its plan.")));
        }

        return db.InAccountResultTransactionAsync(
            async token =>
            {
                var account = await entitlements.OfCurrentAccountAsync(token);
                var limits = account.Limits;
                var counts = await usage.CountAsync(token);

                // Suspended and platform-disabled links hold a place, as at creation (DA-220).
                var shares = await db.Set<SharedGraphView>().CountAsync(share => share.RevokedAt == null, token);
                var connections = await agentIdentities.CountActiveOfCurrentAccountAsync(token);
                var periodStart = AgentWriteQuota.PeriodStartOf(clock.GetUtcNow());
                var agentWrites = await db.Set<GraphChangeSet>()
                    .Where(changeSet => changeSet.QuotaOperations > 0 && changeSet.CreatedAt >= periodStart)
                    .SumAsync(changeSet => changeSet.QuotaOperations, token);

                return Result<AccountPlanView>.Ok(new AccountPlanView(
                    account.Plan,
                    [
                        new PlanLimitUsage(PlanLimitKind.Nodes, counts.Nodes, limits.Nodes),
                        new PlanLimitUsage(PlanLimitKind.Relations, counts.Relations, limits.Relations),
                        new PlanLimitUsage(PlanLimitKind.Resources, counts.Resources, limits.Resources),
                        new PlanLimitUsage(PlanLimitKind.Collections, counts.Collections, limits.Collections),
                        new PlanLimitUsage(PlanLimitKind.CustomTypes, counts.CustomTypes, limits.CustomTypes),
                        new PlanLimitUsage(PlanLimitKind.CustomProperties, counts.CustomProperties, limits.CustomProperties),
                        new PlanLimitUsage(PlanLimitKind.AgentConnections, connections, limits.AgentConnections),
                        new PlanLimitUsage(PlanLimitKind.SharedGraphViews, shares, limits.SharedGraphViews),
                    ],
                    limits.NodesPerSharedGraphView,
                    new AgentWriteQuotaUsage(agentWrites, limits.AgentWriteOperationsPerMonth, periodStart.AddMonths(1))));
            },
            cancellationToken);
    }
}

/// <param name="Limits">One entry per limited kind, in a fixed order.</param>
/// <param name="NodesPerSharedGraphView">How many Nodes one link may hold; <c>null</c> when the plan sets no limit.</param>
public sealed record AccountPlanView(
    AccountPlan Plan,
    IReadOnlyList<PlanLimitUsage> Limits,
    int? NodesPerSharedGraphView,
    AgentWriteQuotaUsage AgentWrites);

/// <param name="Used">What the Account holds now: live rows only, never what the system creates (DA-264).</param>
/// <param name="Max">The plan's limit; <c>null</c> when the plan sets none.</param>
public sealed record PlanLimitUsage(PlanLimitKind Kind, int Used, int? Max);

/// <param name="Used">Operations the Account's agents applied in the current UTC month (DA-261).</param>
/// <param name="Max">The month's quota; <c>null</c> when the plan sets none.</param>
/// <param name="ResetsAt">The first instant of the next UTC month, when the quota starts again: display only.</param>
public sealed record AgentWriteQuotaUsage(int Used, int? Max, DateTimeOffset ResetsAt);
