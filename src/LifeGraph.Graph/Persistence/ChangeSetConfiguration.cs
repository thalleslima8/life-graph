using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

internal sealed class GraphChangeSetConfiguration : IEntityTypeConfiguration<GraphChangeSet>
{
    public void Configure(EntityTypeBuilder<GraphChangeSet> changeSet)
    {
        changeSet.ToTable("changesets", table =>
        {
            table.HasEnumCheck<ChangeSetStatus>("status");
            table.HasEnumCheck<ChangeActorKind>("actor_kind");
            table.HasEnumCheck<WriteChannel>("channel");
            // Provenance (DA-013): an agent change always names its AgentIdentity, a human one never does.
            table.HasCheckConstraint(
                "ck_changesets_agent_identity",
                "(actor_kind = 'agent_identity') = (agent_identity_id IS NOT NULL)");
        });
        changeSet.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        changeSet.Property(entity => entity.Id).ValueGeneratedNever();
        changeSet.Property(entity => entity.Status).StoredAsSnakeCase();
        changeSet.Property(entity => entity.ActorKind).StoredAsSnakeCase();
        changeSet.Property(entity => entity.Channel).StoredAsSnakeCase();
        changeSet.Property(entity => entity.Source).HasMaxLength(Provenance.SourceMaxLength);
        changeSet.HasRowVersion();

        // An Undo points at the GraphChangeSet it undoes, within the same Account (DA-020).
        changeSet.Property(entity => entity.RevertsChangeSetId).HasColumnName("reverts_changeset_id");
        changeSet.HasOne<GraphChangeSet>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.RevertsChangeSetId })
            .HasPrincipalKey(entity => new { entity.AccountId, entity.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_changesets_reverts_changeset");
        changeSet.HasIndex(entity => new { entity.AccountId, entity.RevertsChangeSetId })
            .IsUnique()
            .HasFilter("reverts_changeset_id IS NOT NULL")
            .HasDatabaseName("uq_changesets_account_id_reverts_changeset_id");

        changeSet.HasMany(entity => entity.Entries)
            .WithOne()
            .HasForeignKey(entry => new { entry.AccountId, entry.ChangeSetId })
            .HasPrincipalKey(entity => new { entity.AccountId, entity.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_change_entries_changeset");
        changeSet.Navigation(entity => entity.Entries).HasField("_entries");
    }
}

internal sealed class ChangeEntryConfiguration : IEntityTypeConfiguration<ChangeEntry>
{
    public void Configure(EntityTypeBuilder<ChangeEntry> entry)
    {
        entry.ToTable("change_entries", table =>
        {
            table.HasEnumCheck<GraphEntityKind>("entity_kind");
            table.HasEnumCheck<GraphChangeOperation>("operation");
            table.HasCheckConstraint("ck_change_entries_sequence", "sequence >= 0");
        });
        entry.HasKey(entity => entity.Id).HasName("pk_change_entries");
        entry.HasAccountForeignKey();
        entry.Property(entity => entity.Id).ValueGeneratedNever();
        entry.Property(entity => entity.ChangeSetId).HasColumnName("changeset_id");
        entry.Property(entity => entity.EntityKind).StoredAsSnakeCase();
        entry.Property(entity => entity.Operation).StoredAsSnakeCase();

        // The state before and after, emptied by Purge (DA-021).
        entry.Property(entity => entity.Before).HasColumnType(JsonColumn.ColumnType);
        entry.Property(entity => entity.After).HasColumnType(JsonColumn.ColumnType);
        entry.ToTable(table => table.HasCheckConstraint(
            "ck_change_entries_purged_empty",
            "purged_at IS NULL OR (before IS NULL AND after IS NULL)"));

        // The root a cascaded change came with, in the same GraphChangeSet (DA-115).
        entry.HasAlternateKey(entity => new { entity.ChangeSetId, entity.Id }).HasName("ak_change_entries_changeset_id_id");
        entry.HasOne<ChangeEntry>()
            .WithMany()
            .HasForeignKey(entity => new { entity.ChangeSetId, entity.CascadeOf })
            .HasPrincipalKey(entity => new { entity.ChangeSetId, entity.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_change_entries_cascade_of");

        entry.HasIndex(entity => new { entity.ChangeSetId, entity.Sequence })
            .IsUnique()
            .HasDatabaseName("uq_change_entries_changeset_id_sequence");
        // The history of one entity: its Provenance in the Inspector, conflicts for Undo, Purge.
        entry.HasIndex(entity => new { entity.AccountId, entity.EntityId })
            .HasDatabaseName("ix_change_entries_account_id_entity_id");
    }
}
