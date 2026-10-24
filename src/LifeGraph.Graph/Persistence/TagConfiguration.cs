using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> tag)
    {
        tag.ToTable("tags");
        tag.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        tag.Property(entity => entity.Id).ValueGeneratedNever();
        tag.Property(entity => entity.Name).HasMaxLength(GraphLimits.TagNameMaxLength);
        tag.Property(entity => entity.NameKey).HasMaxLength(GraphLimits.TagNameKeyMaxLength);
        tag.HasRowVersion();
        tag.HasIndex(entity => new { entity.AccountId, entity.NameKey })
            .IsUnique()
            .HasDatabaseName(GraphConstraints.TagNameKeyUnique);
    }
}

internal sealed class NodeTagConfiguration : IEntityTypeConfiguration<NodeTag>
{
    public void Configure(EntityTypeBuilder<NodeTag> link)
    {
        link.ToTable("node_tags");
        link.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        link.Property(entity => entity.Id).ValueGeneratedNever();
        link.HasIndex(entity => new { entity.NodeId, entity.TagId })
            .IsUnique()
            .HasDatabaseName("uq_node_tags_node_id_tag_id");

        // Within the Account only (DB-004): a link never points at another Account's Node or Tag.
        link.HasOne<Node>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.NodeId })
            .HasPrincipalKey(node => new { node.AccountId, node.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_node_tags_node");
        link.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.TagId })
            .HasPrincipalKey(tag => new { tag.AccountId, tag.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_node_tags_tag");
    }
}
