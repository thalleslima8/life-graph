using System.Text.Json;
using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

internal sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> collection)
    {
        collection.ToTable("collections");
        collection.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        collection.Property(entity => entity.Id).ValueGeneratedNever();
        collection.Property(entity => entity.Name).HasMaxLength(GraphLimits.NameMaxLength);

        // The criteria language is versioned JSON (DA-164), written as the API writes it.
        collection.Property(entity => entity.Criteria)
            .HasColumnType(JsonColumn.ColumnType)
            .HasConversion(
                criteria => JsonSerializer.Serialize(criteria, GraphWireJson.Options),
                json => JsonSerializer.Deserialize<CollectionCriteria>(json, GraphWireJson.Options),
                new ValueComparer<CollectionCriteria?>(
                    (left, right) => CollectionCriteriaRules.SameCriteria(left, right),
                    criteria => JsonSerializer.Serialize(criteria, GraphWireJson.Options).GetHashCode(StringComparison.Ordinal),
                    criteria => criteria));
        collection.HasRowVersion();
    }
}

internal sealed class CollectionMemberConfiguration : IEntityTypeConfiguration<CollectionMember>
{
    public void Configure(EntityTypeBuilder<CollectionMember> member)
    {
        member.ToTable("collection_members");
        member.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        member.Property(entity => entity.Id).ValueGeneratedNever();
        member.HasIndex(entity => new { entity.CollectionId, entity.NodeId })
            .IsUnique()
            .HasDatabaseName("uq_collection_members_collection_id_node_id");

        // Within the Account only (DB-004): a membership never points at another Account's Node or Collection.
        member.HasOne<Node>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.NodeId })
            .HasPrincipalKey(node => new { node.AccountId, node.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_collection_members_node");
        member.HasOne<Collection>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.CollectionId })
            .HasPrincipalKey(collection => new { collection.AccountId, collection.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_collection_members_collection");
    }
}
