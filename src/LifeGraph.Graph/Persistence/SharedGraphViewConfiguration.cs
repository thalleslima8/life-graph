using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

internal sealed class SharedGraphViewConfiguration : IEntityTypeConfiguration<SharedGraphView>
{
    public void Configure(EntityTypeBuilder<SharedGraphView> share)
    {
        share.ToTable("shared_graph_views", table =>
        {
            table.HasEnumCheck<ChangeActorKind>("created_by");
            table.HasEnumCheck<WriteChannel>("created_via");
            table.HasEnumCheck<ChangeActorKind>("revoked_by");
            table.HasCheckConstraint("ck_shared_graph_views_max_depth", $"max_depth BETWEEN {SharingLimits.MinDepth} AND {SharingLimits.MaxDepth}");
            table.HasCheckConstraint("ck_shared_graph_views_access_day_count", "access_day_count >= 0");

            // Only Purge takes the root away, and it revokes the share as it does (DA-075).
            table.HasCheckConstraint("ck_shared_graph_views_root_node_id", "root_node_id IS NOT NULL OR revoked_at IS NOT NULL");
            table.HasCheckConstraint("ck_shared_graph_views_revoked_by", "(revoked_at IS NULL) = (revoked_by IS NULL)");
        });
        share.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        share.Property(entity => entity.Id).ValueGeneratedNever();
        share.Property(entity => entity.CreatedBy).StoredAsSnakeCase();
        share.Property(entity => entity.CreatedVia).StoredAsSnakeCase();
        share.Property(entity => entity.RevokedBy).StoredAsSnakeCase();

        // Within the Account only (DB-004).
        share.HasOne<Node>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.RootNodeId })
            .HasPrincipalKey(node => new { node.AccountId, node.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_shared_graph_views_root_node");
        share.HasIndex(entity => new { entity.AccountId, entity.RootNodeId })
            .HasDatabaseName("ix_shared_graph_views_account_id_root_node_id");
    }
}

internal sealed class SharedGraphViewNodeConfiguration : IEntityTypeConfiguration<SharedGraphViewNode>
{
    public void Configure(EntityTypeBuilder<SharedGraphViewNode> member)
    {
        member.ToTable("shared_graph_view_nodes");
        member.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        member.Property(entity => entity.Id).ValueGeneratedNever();
        member.HasIndex(entity => new { entity.SharedGraphViewId, entity.NodeId })
            .IsUnique()
            .HasDatabaseName("uq_shared_graph_view_nodes_shared_graph_view_id_node_id");
        member.HasIndex(entity => new { entity.AccountId, entity.NodeId })
            .HasDatabaseName("ix_shared_graph_view_nodes_account_id_node_id");

        // Purge of the Node removes its rows (GraphPurge), as it does a Collection membership.
        member.HasOne<Node>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.NodeId })
            .HasPrincipalKey(node => new { node.AccountId, node.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_shared_graph_view_nodes_node");
        member.HasOne<SharedGraphView>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.SharedGraphViewId })
            .HasPrincipalKey(share => new { share.AccountId, share.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_shared_graph_view_nodes_shared_graph_view");
    }
}

internal sealed class SharedGraphViewPropertyConfiguration : IEntityTypeConfiguration<SharedGraphViewProperty>
{
    public void Configure(EntityTypeBuilder<SharedGraphViewProperty> property)
    {
        property.ToTable("shared_graph_view_properties");
        property.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        property.Property(entity => entity.Id).ValueGeneratedNever();
        property.HasIndex(entity => new { entity.SharedGraphViewId, entity.PropertyDefinitionId })
            .IsUnique()
            .HasDatabaseName("uq_shared_graph_view_properties_shared_graph_view_id_property_definition_id");
        property.HasIndex(entity => new { entity.AccountId, entity.PropertyDefinitionId })
            .HasDatabaseName("ix_shared_graph_view_properties_account_id_property_definition_id");

        // A Property Definition the person removes leaves every allowlist with it: it is never
        // shown again, even if an Undo brings the definition back.
        property.HasOne<PropertyDefinition>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.PropertyDefinitionId })
            .HasPrincipalKey(definition => new { definition.AccountId, definition.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_shared_graph_view_properties_property_definition");
        property.HasOne<SharedGraphView>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.SharedGraphViewId })
            .HasPrincipalKey(share => new { share.AccountId, share.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_shared_graph_view_properties_shared_graph_view");
    }
}

/// <summary>
/// The share directory (DA-217): no RLS, since the visitor's token is resolved before any
/// Account is in context (the exception is recorded in E11). The application role reads and
/// inserts; it neither updates nor deletes (the migration revokes both), and a row goes with its
/// share by the foreign key's cascade.
/// </summary>
internal sealed class SharedGraphViewDirectoryConfiguration : IEntityTypeConfiguration<SharedGraphViewDirectoryEntry>
{
    public const int TokenHashBytes = 32;

    public const int PublicRefMaxLength = 32;

    public void Configure(EntityTypeBuilder<SharedGraphViewDirectoryEntry> entry)
    {
        entry.ToTable("shared_graph_view_directory", table =>
            table.HasCheckConstraint("ck_shared_graph_view_directory_token_hash", $"octet_length(token_hash) = {TokenHashBytes}"));
        entry.HasKey(entity => entity.SharedGraphViewId).HasName("pk_shared_graph_view_directory");
        entry.Property(entity => entity.SharedGraphViewId).ValueGeneratedNever();
        entry.Property(entity => entity.PublicRef).HasMaxLength(PublicRefMaxLength);
        entry.HasIndex(entity => entity.TokenHash).IsUnique().HasDatabaseName("uq_shared_graph_view_directory_token_hash");
        entry.HasIndex(entity => entity.PublicRef).IsUnique().HasDatabaseName("uq_shared_graph_view_directory_public_ref");
        entry.HasIndex(entity => new { entity.AccountId, entity.SharedGraphViewId })
            .IsUnique()
            .HasDatabaseName("uq_shared_graph_view_directory_account_id_shared_graph_view_id");
        entry.HasOne<SharedGraphView>()
            .WithOne()
            .HasForeignKey<SharedGraphViewDirectoryEntry>(entity => new { entity.AccountId, entity.SharedGraphViewId })
            .HasPrincipalKey<SharedGraphView>(share => new { share.AccountId, share.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_shared_graph_view_directory_shared_graph_view");
    }
}
