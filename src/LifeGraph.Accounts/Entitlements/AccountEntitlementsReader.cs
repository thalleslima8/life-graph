using LifeGraph.Accounts.Contracts;
using LifeGraph.Accounts.Domain;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LifeGraph.Accounts.Entitlements;

/// <summary>
/// The plan of the Account in context, read now from <c>account_plans</c> (RLS keeps it to that
/// Account), with the limits the configuration gives that plan.
/// </summary>
internal sealed class AccountEntitlementsReader(LifeGraphDbContext db, IOptions<EntitlementsOptions> options) : IEntitlements
{
    public async Task<AccountEntitlements> OfCurrentAccountAsync(CancellationToken cancellationToken)
    {
        var assigned = await db.Set<AccountPlanAssignment>()
            .AsNoTracking()
            .Select(assignment => (AccountPlan?)assignment.Plan)
            .SingleOrDefaultAsync(cancellationToken);
        var plan = assigned ?? AccountPlan.Free;

        return new AccountEntitlements(plan, options.Value.For(plan));
    }

    public async Task<AccountPlanAssignmentView> PlanAssignmentOfCurrentAccountAsync(CancellationToken cancellationToken) =>
        await db.Set<AccountPlanAssignment>()
            .AsNoTracking()
            .Select(assignment => new AccountPlanAssignmentView(assignment.Plan, assignment.Origin, assignment.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken)
        ?? AccountPlanAssignmentView.Unassigned;
}
