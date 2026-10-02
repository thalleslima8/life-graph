namespace LifeGraph.Infrastructure.Persistence;

/// <summary>
/// The Account whose data the current unit of work may see. Row-level security is
/// keyed on it, so a null value means no Account rows are visible at all.
/// </summary>
public interface IAccountContext
{
    Guid? AccountId { get; }
}
