using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyValues = LifeGraph.Graph.Domain.PropertyValues;

namespace LifeGraph.Graph.Persistence;

internal sealed class NodeConfiguration : IEntityTypeConfiguration<Node>
{
    public void Configure(EntityTypeBuilder<Node> node)
    {
        node.ToTable("nodes", table => table.HasCheckConstraint("ck_nodes_version", "version >= 1"));
        node.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        node.Property(entity => entity.Id).ValueGeneratedNever();
        node.Property(entity => entity.Title).HasMaxLength(GraphLimits.TitleMaxLength);
        node.Property(entity => entity.Body).HasMaxLength(GraphLimits.BodyMaxLength);

        // Optimistic concurrency (DA-022): an update only matches the version it read.
        node.Property(entity => entity.Version).IsConcurrencyToken();

        // Tombstones (DA-021): the purge finds the ones past the delete window.
        node.HasIndex(entity => new { entity.AccountId, entity.DeletedAt })
            .HasFilter("deleted_at IS NOT NULL")
            .HasDatabaseName("ix_nodes_account_id_deleted_at");

        // The Inbox is an explicit state (DA-019); its list reads the newest captures first.
        node.HasIndex(entity => new { entity.AccountId, entity.InboxEnteredAt })
            .HasFilter("inbox_entered_at IS NOT NULL")
            .HasDatabaseName("ix_nodes_account_id_inbox_entered_at");

        // Values keyed by property id, in JSONB (DA-015); jsonb_path_ops serves equality filters.
        node.Property(entity => entity.Properties)
            .HasColumnType(JsonColumn.ColumnType)
            .HasConversion(
                values => values.Json,
                json => PropertyValues.FromJson(json),
                new ValueComparer<PropertyValues>(
                    (left, right) => left!.Equals(right),
                    values => values.GetHashCode(),
                    values => values));
        node.HasIndex(entity => entity.Properties)
            .HasMethod("gin")
            .HasOperators("jsonb_path_ops")
            .HasDatabaseName("ix_nodes_properties");

        // A Type can be referenced only within the Node's Account; with no Type the key is null and unchecked.
        node.HasOne<NodeType>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.TypeId })
            .HasPrincipalKey(type => new { type.AccountId, type.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_nodes_type");
    }
}

internal sealed class RelationConfiguration : IEntityTypeConfiguration<Relation>
{
    public void Configure(EntityTypeBuilder<Relation> relation)
    {
        relation.ToTable("relations", table =>
        {
            table.HasEnumCheck<RelationAssertion>("assertion");
            table.HasEnumCheck<RelationOrigin>("origin");
            table.HasCheckConstraint("ck_relations_not_self", "source_node_id <> target_node_id");
            table.HasCheckConstraint("ck_relations_strength", "strength >= 0 AND strength <= 1");
            table.HasCheckConstraint("ck_relations_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
            // Only the system infers; a Soft Relation always says how sure it is (DA-014).
            table.HasCheckConstraint(
                "ck_relations_soft_is_inferred",
                "assertion = 'hard' OR (origin = 'system' AND confidence IS NOT NULL)");
        });
        relation.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        relation.Property(entity => entity.Id).ValueGeneratedNever();
        relation.Property(entity => entity.Kind).HasMaxLength(GraphLimits.RelationKindMaxLength);
        relation.Property(entity => entity.Assertion).StoredAsSnakeCase();
        relation.Property(entity => entity.Origin).StoredAsSnakeCase();
        relation.HasRowVersion();
        relation.HasIndex(entity => new { entity.AccountId, entity.DeletedAt })
            .HasFilter("deleted_at IS NOT NULL")
            .HasDatabaseName("ix_relations_account_id_deleted_at");

        relation.HasOne<Node>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.SourceNodeId })
            .HasPrincipalKey(node => new { node.AccountId, node.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_relations_source_node");
        relation.HasOne<Node>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.TargetNodeId })
            .HasPrincipalKey(node => new { node.AccountId, node.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_relations_target_node");
    }
}
