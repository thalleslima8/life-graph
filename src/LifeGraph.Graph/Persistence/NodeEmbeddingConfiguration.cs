using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

internal sealed class NodeEmbeddingConfiguration : IEntityTypeConfiguration<NodeEmbedding>
{
    public void Configure(EntityTypeBuilder<NodeEmbedding> embedding)
    {
        embedding.ToTable("node_embeddings", table =>
        {
            table.HasCheckConstraint("ck_node_embeddings_dimension", "dimension = vector_dims(embedding)");
            table.HasCheckConstraint("ck_node_embeddings_content_hash", $"char_length(content_hash) = {NodeEmbedding.ContentHashLength}");
        });
        embedding.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        embedding.Property(entity => entity.Id).ValueGeneratedNever();
        embedding.Property(entity => entity.ModelId).HasMaxLength(NodeEmbedding.ModelIdMaxLength);
        embedding.Property(entity => entity.ContentHash).HasMaxLength(NodeEmbedding.ContentHashLength);
        embedding.Property(entity => entity.Embedding).HasColumnType($"vector({NodeEmbedding.StoredDimension})");

        // One embedding per Node and model (DA-069).
        embedding.HasIndex(entity => new { entity.AccountId, entity.NodeId, entity.ModelId })
            .IsUnique()
            .HasDatabaseName("uq_node_embeddings_account_id_node_id_model_id");

        // The nearest neighbours by cosine distance (DA-070).
        embedding.HasIndex(entity => entity.Embedding)
            .HasMethod("hnsw")
            .HasOperators("vector_cosine_ops")
            .HasDatabaseName("ix_node_embeddings_embedding");

        // Derived from the Node: the purge that removes the Node row removes its embeddings (DA-176).
        embedding.HasOne<Node>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.NodeId })
            .HasPrincipalKey(node => new { node.AccountId, node.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_node_embeddings_node");
    }
}
