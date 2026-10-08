using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

internal sealed class AgentReadConfiguration : IEntityTypeConfiguration<AgentRead>
{
    public void Configure(EntityTypeBuilder<AgentRead> read)
    {
        read.ToTable("agent_reads", table => table.HasEnumCheck<AgentReadOperation>("operation"));
        read.HasKey(entity => entity.Id).HasName("pk_agent_reads");
        read.HasAccountForeignKey();
        read.Property(entity => entity.Id).ValueGeneratedNever();
        read.Property(entity => entity.Operation).StoredAsSnakeCase();
        read.Property(entity => entity.Arguments).HasColumnType(JsonColumn.ColumnType);

        // The audit is read by agent and by time, and the index also serves the foreign key to
        // agent_identities (DB-030); RLS keeps it to the Account. The retention cut (E12/E14) is by time.
        // The foreign key itself is in the migration only: the Graph's model cannot name the
        // Accounts' entity (DA-111, DA-121).
        read.HasIndex(entity => new { entity.AgentIdentityId, entity.CreatedAt })
            .HasDatabaseName("ix_agent_reads_agent_identity_id_created_at");
        read.HasIndex(entity => entity.CreatedAt).HasDatabaseName("ix_agent_reads_created_at");
    }
}
