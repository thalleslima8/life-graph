namespace LifeGraph.Infrastructure.Identity;

/// <summary>
/// The caller of the current unit of work, read from the authenticated claims (DA-094).
/// It fails closed: anything short of a well-formed principal is <c>null</c>, never a
/// default Account.
/// </summary>
public interface ICurrentPrincipal
{
    AuthenticatedPrincipal? Authenticated { get; }
}
