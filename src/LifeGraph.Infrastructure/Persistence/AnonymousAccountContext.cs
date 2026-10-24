namespace LifeGraph.Infrastructure.Persistence;

/// <summary>No Account at all: for migrations and design-time tooling, which never read Account rows.</summary>
public sealed class AnonymousAccountContext : IAccountContext
{
    public Guid? AccountId => null;
}
