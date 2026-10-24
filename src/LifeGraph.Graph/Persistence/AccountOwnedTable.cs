using System.Linq.Expressions;
using LifeGraph.Infrastructure.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

/// <summary>
/// The keys every graph table shares. Besides the Account foreign key, each table exposes
/// (account_id, id) as a key, and graph rows reference each other through it: Postgres
/// checks foreign keys past RLS, so a plain id reference could point at another Account's
/// row. With the Account in the key it cannot (DB-004).
/// </summary>
internal static class AccountOwnedTable
{
    public const string AccountIdProperty = "AccountId";

    public static void HasAccountKeys<TEntity>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, object?>> id,
        Expression<Func<TEntity, object?>> accountAndId)
        where TEntity : class
    {
        var table = entity.Metadata.GetTableName();
        entity.HasKey(id).HasName($"pk_{table}");
        entity.HasAlternateKey(accountAndId).HasName($"ak_{table}_account_id_id");
        HasAccountForeignKey(entity);
    }

    public static void HasAccountForeignKey<TEntity>(this EntityTypeBuilder<TEntity> entity)
        where TEntity : class =>
        entity.HasOne<Account>().WithMany().HasForeignKey(AccountIdProperty).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName($"fk_{entity.Metadata.GetTableName()}_account_id");
}

internal static class RowVersion
{
    /// <summary>
    /// Optimistic concurrency on Postgres' own row version (<c>xmin</c>), for entities with no
    /// version of their own: two writes changing the same row cannot both succeed.
    /// </summary>
    public static void HasRowVersion<TEntity>(this EntityTypeBuilder<TEntity> entity)
        where TEntity : class =>
        entity.Property<uint>("RowVersion").IsRowVersion();
}
