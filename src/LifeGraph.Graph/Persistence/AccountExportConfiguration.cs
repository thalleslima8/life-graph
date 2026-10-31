using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

internal sealed class AccountExportConfiguration : IEntityTypeConfiguration<AccountExport>
{
    private static readonly string FailedState = EnumColumn.NameOf(AccountExportState.Failed);

    private static readonly string ReadyState = EnumColumn.NameOf(AccountExportState.Ready);

    private static readonly string SizeCapFailure = EnumColumn.NameOf(AccountExportFailure.SizeCap);

    public void Configure(EntityTypeBuilder<AccountExport> export)
    {
        export.ToTable("account_exports", table =>
        {
            table.HasEnumCheck<AccountExportState>("state");
            table.HasEnumCheck<AccountExportFailure>("failure");
            table.HasEnumCheck<AccountExportData>("oversized_data");
            table.HasCheckConstraint("ck_account_exports_failed_with_failure", $"(state = '{FailedState}') = (failure IS NOT NULL)");
            table.HasCheckConstraint("ck_account_exports_size_cap_with_data", $"((failure = '{SizeCapFailure}') IS TRUE) = (oversized_data IS NOT NULL)");
            table.HasCheckConstraint("ck_account_exports_content", $"content IS NULL OR state = '{ReadyState}'");
            table.HasCheckConstraint("ck_account_exports_size_bytes", "size_bytes IS NULL OR size_bytes >= 0");
        });
        export.HasKey(entity => entity.Id).HasName("pk_account_exports");
        export.HasAccountForeignKey();
        export.Property(entity => entity.Id).ValueGeneratedNever();
        export.Property(entity => entity.State).StoredAsSnakeCase();
        export.Property(entity => entity.Failure).StoredAsSnakeCase();
        export.Property(entity => entity.OversizedData).StoredAsSnakeCase();

        // One export per Account (DA-248): a new request replaces the previous one.
        export.HasIndex(entity => entity.AccountId).IsUnique().HasDatabaseName("uq_account_exports_account_id");
    }
}
