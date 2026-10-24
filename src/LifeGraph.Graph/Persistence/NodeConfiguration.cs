using LifeGraph.Graph.Contracts;
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
        node.Property(entity => entity.SuggestedType).HasMaxLength(GraphLimits.NameMaxLength);
        node.Property(entity => entity.HumanEditedFields).HasDefaultValueSql("'{}'::text[]");

        // The agent's own creation (DA-133); the foreign key to agent_identities is in the
        // migration only, as the Graph's model cannot name the Accounts' entity (DA-121).
        node.HasIndex(entity => entity.CreatedByAgentIdentityId)
            .HasFilter("created_by_agent_identity_id IS NOT NULL")
            .HasDatabaseName("ix_nodes_created_by_agent_identity_id");

        // One live Resource per link and Account (DA-060, DA-154); the tombstones apart, so a
        // capture finds the one to restore.
        node.Property(entity => entity.CanonicalUrl).HasMaxLength(GraphLimits.UrlValueMaxLength);
        node.HasIndex(entity => new { entity.AccountId, entity.CanonicalUrl })
            .IsUnique()
            .HasFilter("canonical_url IS NOT NULL AND deleted_at IS NULL")
            .HasDatabaseName(GraphConstraints.NodeCanonicalUrlUnique);
        node.HasIndex(entity => new { entity.AccountId, entity.CanonicalUrl, entity.DeletedAt })
            .HasFilter("canonical_url IS NOT NULL AND deleted_at IS NOT NULL")
            .HasDatabaseName("ix_nodes_account_id_canonical_url_deleted_at");

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
        relation.ToTable(RelationReadGate.TableName, table =>
        {
            table.HasEnumCheck<RelationAssertion>("assertion");
            table.HasEnumCheck<RelationOrigin>("origin");
            table.HasCheckConstraint("ck_relations_not_self", "source_node_id <> target_node_id");
            table.HasCheckConstraint("ck_relations_strength", "strength >= 0 AND strength <= 1");
            table.HasCheckConstraint("ck_relations_confidence", "confidence IS NULL OR (confidence >= 0 AND confidence <= 1)");
            // Only the system infers; a Soft Relation always says how sure it is (DA-014), how it was
            // inferred and by which model (DA-070), and is the unordered pair stored as related_to
            // with the lower id as source (DA-177, DA-186).
            table.HasCheckConstraint(
                "ck_relations_soft_is_inferred",
                $"assertion = '{RelationReadGate.HardAssertion}' OR (origin = 'system' AND confidence IS NOT NULL"
                + " AND inference_method IS NOT NULL AND inference_model_id IS NOT NULL"
                + $" AND kind = '{Relation.SoftKind}' AND source_node_id < target_node_id)");
        });
        relation.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        relation.Property(entity => entity.Id).ValueGeneratedNever();
        relation.Property(entity => entity.Kind).HasMaxLength(GraphLimits.RelationKindMaxLength);
        relation.Property(entity => entity.InferenceMethod).HasMaxLength(Relation.InferenceMethodMaxLength);
        relation.Property(entity => entity.InferenceModelId).HasMaxLength(Relation.InferenceModelIdMaxLength);

        // The model sees only Hard Relations; the Soft ones come only through the gate (DA-178).
        relation.HasQueryFilter(RelationReadGate.HardOnlyFilter, entity => entity.Assertion == RelationAssertion.Hard);

        // One Soft Relation per pair, stored with the lower id as the source (DA-177).
        relation.HasIndex(entity => new { entity.AccountId, entity.SourceNodeId, entity.TargetNodeId }, "uq_relations_soft_pair")
            .IsUnique()
            .HasFilter($"assertion = '{RelationReadGate.SoftAssertion}'")
            .HasDatabaseName("uq_relations_soft_pair");

        // Kept for every Relation's source key, Hard ones included: the filtered index above does not serve them.
        relation.HasIndex(entity => new { entity.AccountId, entity.SourceNodeId })
            .HasDatabaseName("ix_relations_account_id_source_node_id");
        relation.HasIndex(entity => entity.CreatedByAgentIdentityId)
            .HasFilter("created_by_agent_identity_id IS NOT NULL")
            .HasDatabaseName("ix_relations_created_by_agent_identity_id");
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
