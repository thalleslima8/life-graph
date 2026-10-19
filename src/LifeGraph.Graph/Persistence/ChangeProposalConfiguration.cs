using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

internal sealed class ChangeProposalConfiguration : IEntityTypeConfiguration<ChangeProposal>
{
    public void Configure(EntityTypeBuilder<ChangeProposal> proposal)
    {
        proposal.ToTable("change_proposals", table =>
        {
            table.HasEnumCheck<ProposalStatus>("status");
            table.HasEnumArrayCheck<ProposalReason>("reasons");
            table.HasCheckConstraint("ck_change_proposals_operation_count", "operation_count >= 1");
            table.HasCheckConstraint("ck_change_proposals_rejected_operations", "0 <= ALL(rejected_operations)");
            // Only an application creates Nodes, and only a change does (a GraphChangeSet).
            table.HasCheckConstraint(
                "ck_change_proposals_created_nodes",
                "jsonb_typeof(created_nodes) = 'object' AND (created_nodes = '{}'::jsonb OR changeset_id IS NOT NULL)");
            // Only an applied proposal has a GraphChangeSet (none when it changed nothing).
            table.HasCheckConstraint(
                "ck_change_proposals_applied_changeset",
                "changeset_id IS NULL OR status = 'applied'");
            table.HasCheckConstraint(
                "ck_change_proposals_redacted_empty",
                "redacted_at IS NULL OR operations = '[]'::jsonb");
        });
        proposal.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        proposal.Property(entity => entity.Id).ValueGeneratedNever();
        proposal.Property(entity => entity.Status).StoredAsSnakeCase();
        proposal.Property(entity => entity.Operations).HasColumnType(JsonColumn.ColumnType);
        proposal.Property(entity => entity.ContentHash).HasMaxLength(ChangeProposal.ContentHashLength);
        proposal.Property(entity => entity.Source).HasMaxLength(Provenance.SourceMaxLength);
        proposal.Property(entity => entity.IdempotencyKey).HasMaxLength(ChangeProposal.IdempotencyKeyMaxLength);
        proposal.Property(entity => entity.ChangeSetId).HasColumnName("changeset_id");
        proposal.HasRowVersion();

        proposal.Property<List<ProposalReason>>(ChangeProposal.ReasonsField).HasColumnName("reasons").StoredAsSnakeCaseArray();
        proposal.Ignore(entity => entity.Reasons);
        proposal.Property<List<Guid>>(ChangeProposal.TargetNodeIdsField).HasColumnName("target_node_ids");
        proposal.Ignore(entity => entity.TargetNodeIds);
        proposal.Property<List<int>>(ChangeProposal.RejectedOperationsField).HasColumnName("rejected_operations");
        proposal.Ignore(entity => entity.RejectedOperations);
        proposal.Property<Dictionary<string, Guid>>(ChangeProposal.CreatedNodesField).HasColumnName("created_nodes").StoredAsJsonMap();
        proposal.Ignore(entity => entity.CreatedNodes);

        proposal.HasOne<GraphChangeSet>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.ChangeSetId })
            .HasPrincipalKey(changeSet => new { changeSet.AccountId, changeSet.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_change_proposals_changeset");

        // The queue of one agent (pending count, dedupe), Agent Changes by agent, and the
        // foreign key to agent_identities, which is in the migration only (DA-111, DA-121).
        proposal.HasIndex(entity => new { entity.AgentIdentityId, entity.Status, entity.ContentHash })
            .HasDatabaseName("ix_change_proposals_agent_identity_id_status_content_hash");
        // Content the person rejected is recognized in the whole Account (DA-043 item 8).
        proposal.HasIndex(entity => new { entity.AccountId, entity.ContentHash })
            .HasFilter("status = 'rejected'")
            .HasDatabaseName("ix_change_proposals_account_id_content_hash_rejected");
        proposal.HasIndex(ChangeProposal.TargetNodeIdsField)
            .HasMethod("gin")
            .HasDatabaseName("ix_change_proposals_target_node_ids");
    }
}
