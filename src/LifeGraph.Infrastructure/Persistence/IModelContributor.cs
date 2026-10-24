using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Infrastructure.Persistence;

/// <summary>
/// A module's share of the one <see cref="LifeGraphDbContext"/> model (DA-111). The module
/// keeps its entities in its own domain and applies its <c>IEntityTypeConfiguration</c>s
/// here, so one context, one connection and one transaction cover a graph change and its
/// GraphChangeSet (DA-013).
/// <para>
/// The web host gets the contributors from DI; the design-time factory in
/// <c>LifeGraph.Migrations</c> lists the same ones explicitly. A test proves both models
/// match the migrations.
/// </para>
/// </summary>
public interface IModelContributor
{
    void Configure(ModelBuilder modelBuilder);
}
