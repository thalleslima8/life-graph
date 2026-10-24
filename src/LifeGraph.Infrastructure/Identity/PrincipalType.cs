namespace LifeGraph.Infrastructure.Identity;

/// <summary>Who is behind a request. The central read filter decides what each type may see.</summary>
public enum PrincipalType
{
    Human,
    AgentIdentity,
    ShareVisitor,
}
