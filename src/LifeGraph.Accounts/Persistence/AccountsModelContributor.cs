using LifeGraph.Accounts.Domain;
using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenIddict.EntityFrameworkCore.Models;

namespace LifeGraph.Accounts.Persistence;

/// <summary>The Accounts module's share of the one model (DA-111): the AgentIdentities.</summary>
public sealed class AccountsModelContributor : IModelContributor
{
    public void Configure(ModelBuilder modelBuilder) => modelBuilder.ApplyConfiguration(new AgentIdentityConfiguration());
}

/// <summary>
/// Account data, so it has <c>account_id</c> and the isolation policy like every Account
/// table (DA-004); unlike the oidc_* tables of the issuer, which stay with the credential
/// directory (DA-119).
/// </summary>
internal sealed class AgentIdentityConfiguration : IEntityTypeConfiguration<AgentIdentity>
{
    public const string TableName = "agent_identities";
    public const string ClientUniqueIndex = "uq_agent_identities_account_id_client_id";

    public void Configure(EntityTypeBuilder<AgentIdentity> identity)
    {
        identity.ToTable(TableName);
        identity.HasKey(entity => entity.Id).HasName("pk_agent_identities");
        identity.Property(entity => entity.Id).ValueGeneratedNever();
        identity.HasOne<Account>().WithMany().HasForeignKey(entity => entity.AccountId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_agent_identities_account_id");

        // The grant behind the connection (DB-004). A grant the issuer deletes (an Account purge,
        // OpenIddict's pruning of a revoked one) leaves the connection without one, never dangling.
        identity.HasOne<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().WithMany().HasForeignKey(entity => entity.AuthorizationId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_agent_identities_authorization_id");

        identity.Property(entity => entity.ClientId).HasMaxLength(AgentIdentity.ClientIdMaxLength);
        identity.Property(entity => entity.ClientName).HasMaxLength(AgentIdentity.ClientNameMaxLength);
        identity.Property(entity => entity.Name).HasMaxLength(AgentIdentity.NameMaxLength);
        identity.Ignore(entity => entity.Scopes);
        identity.Ignore(entity => entity.IsActive);
        identity.Property<List<string>>("_scopes").HasColumnName("scopes");

        identity.HasIndex(entity => new { entity.AccountId, entity.ClientId }).IsUnique().HasDatabaseName(ClientUniqueIndex);
        identity.Property<uint>("RowVersion").IsRowVersion();
    }
}
