using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

internal sealed class SoftDismissalConfiguration : IEntityTypeConfiguration<SoftDismissal>
{
    public void Configure(EntityTypeBuilder<SoftDismissal> dismissal)
    {
        dismissal.ToTable("soft_dismissals", table =>
            table.HasCheckConstraint("ck_soft_dismissals_ordered_pair", "first_node_id < second_node_id"));
        dismissal.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        dismissal.Property(entity => entity.Id).ValueGeneratedNever();

        // One Ignore per pair (DA-071).
        dismissal.HasIndex(entity => new { entity.AccountId, entity.FirstNodeId, entity.SecondNodeId })
            .IsUnique()
            .HasDatabaseName("uq_soft_dismissals_account_id_first_node_id_second_node_id");
        dismissal.HasIndex(entity => new { entity.AccountId, entity.SecondNodeId })
            .HasDatabaseName("ix_soft_dismissals_account_id_second_node_id");

        // The pair goes with either of its Nodes when the Node is purged.
        dismissal.HasOne<Node>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.FirstNodeId })
            .HasPrincipalKey(node => new { node.AccountId, node.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_soft_dismissals_first_node");
        dismissal.HasOne<Node>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.SecondNodeId })
            .HasPrincipalKey(node => new { node.AccountId, node.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_soft_dismissals_second_node");
    }
}
