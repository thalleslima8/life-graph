using LifeGraph.Infrastructure.Accounts;
using LifeGraph.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;

namespace LifeGraph.Infrastructure.Persistence;

public sealed class LifeGraphDbContext(
    DbContextOptions<LifeGraphDbContext> options,
    IAccountContext accountContext) : IdentityUserContext<LifeGraphUser, Guid>(options)
{
    internal Guid? AccountId => accountContext.AccountId;

    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasPostgresExtension("unaccent");

        modelBuilder.Entity<Account>(account =>
        {
            account.ToTable("accounts");
            account.HasKey(entity => entity.Id);
            account.Property(entity => entity.Id).ValueGeneratedNever();
        });

        ConfigureIdentityTables(modelBuilder);
        ConfigureOpenIddictTables(modelBuilder);
    }

    // The credential directory has no RLS policy by exception (DA-098): only the Accounts
    // module reaches it, and Account data never lives here, only the link from a user to
    // its Account. The oidc_* tables have no account_id yet; E3 decides their isolation.
    private static void ConfigureIdentityTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LifeGraphUser>(user =>
        {
            user.ToTable("users");
            user.HasOne<Account>()
                .WithOne()
                .HasForeignKey<LifeGraphUser>(entity => entity.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
            user.HasIndex(entity => entity.AccountId).IsUnique();

            // Keep only the personal data the login needs (DA-098). No phone and no two-factor
            // in the MVP, and no Identity lockout (DA-097): unmapped, they stay at their
            // defaults (empty, off, zero failures), which is what the sign-in reads.
            user.Ignore(entity => entity.PhoneNumber);
            user.Ignore(entity => entity.PhoneNumberConfirmed);
            user.Ignore(entity => entity.TwoFactorEnabled);
            user.Ignore(entity => entity.LockoutEnd);
            user.Ignore(entity => entity.LockoutEnabled);
            user.Ignore(entity => entity.AccessFailedCount);

            // Identity names these in PascalCase; keep the schema in one convention (DB-001).
            user.HasIndex(entity => entity.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
            user.HasIndex(entity => entity.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");
        });

        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
    }

    private static void ConfigureOpenIddictTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable("oidc_applications");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().ToTable("oidc_authorizations");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>().ToTable("oidc_scopes");
        modelBuilder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>().ToTable("oidc_tokens");
    }
}
