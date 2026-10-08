using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LifeGraph.Graph.Persistence;

/// <summary>The Graph module's share of the one model (DA-111): its tables, keys and checks.</summary>
public sealed class GraphModelContributor : IModelContributor
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PropertyDefinitionConfiguration());
        modelBuilder.ApplyConfiguration(new NodeTypeConfiguration());
        modelBuilder.ApplyConfiguration(new TypePropertyConfiguration());
        modelBuilder.ApplyConfiguration(new NodeConfiguration());
        modelBuilder.ApplyConfiguration(new RelationConfiguration());
        modelBuilder.ApplyConfiguration(new GraphChangeSetConfiguration());
        modelBuilder.ApplyConfiguration(new ChangeEntryConfiguration());
        modelBuilder.ApplyConfiguration(new AgentReadConfiguration());
    }
}
