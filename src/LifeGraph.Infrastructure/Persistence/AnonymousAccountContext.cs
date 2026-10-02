namespace LifeGraph.Infrastructure.Persistence;

// Default until authentication exists. TODO(E1): replace with the principal-backed context.
public sealed class AnonymousAccountContext : IAccountContext
{
    public Guid? AccountId => null;
}
