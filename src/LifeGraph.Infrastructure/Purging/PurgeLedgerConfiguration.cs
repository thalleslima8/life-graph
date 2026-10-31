using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Infrastructure.Purging;

/// <summary>
/// The purge ledger (DA-084, DA-253): no <c>account_id</c>, no foreign key and no RLS, since its
/// rows outlive the Account they record; the application role may only insert (the migration
/// revokes the rest), by exception to BE-022 recorded as DA-253.
/// </summary>
internal sealed class PurgeLedgerConfiguration : IEntityTypeConfiguration<PurgeLedgerEntry>
{
    private static readonly string AccountKind = PurgeLedgerEntry.KindName(PurgedSubjectKind.Account);

    private static readonly string NodeKind = PurgeLedgerEntry.KindName(PurgedSubjectKind.Node);

    public void Configure(EntityTypeBuilder<PurgeLedgerEntry> entry)
    {
        entry.ToTable("purge_ledger_entries", table =>
        {
            table.HasCheckConstraint("ck_purge_ledger_entries_subject_kind", $"subject_kind IN ('{AccountKind}', '{NodeKind}')");
            table.HasCheckConstraint("ck_purge_ledger_entries_subject_hash_length", $"octet_length(subject_hash) = {PurgeLedgerEntry.SubjectHashLength}");
            table.HasCheckConstraint("ck_purge_ledger_entries_hash_version", $"hash_version >= {PurgeLedgerEntry.MinHashVersion}");
            table.HasCheckConstraint("ck_purge_ledger_entries_receipt_only_for_accounts", $"(subject_kind = '{AccountKind}') = (receipt_reference IS NOT NULL)");
        });
        entry.HasKey(entity => entity.Id).HasName("pk_purge_ledger_entries");
        entry.Property(entity => entity.Id).ValueGeneratedNever();
        entry.Property(entity => entity.SubjectKind)
            .HasConversion(kind => PurgeLedgerEntry.KindName(kind), name => Enum.Parse<PurgedSubjectKind>(name, true))
            .HasMaxLength(PurgeLedgerEntry.SubjectKindMaxLength);
        entry.Property(entity => entity.SubjectHash);
        entry.Property(entity => entity.ReceiptReference).HasMaxLength(PurgeLedgerEntry.ReceiptReferenceMaxLength);

        // E14 looks a restored id up by its hash; a person quoting a receipt is found by its reference.
        entry.HasIndex(entity => entity.SubjectHash).HasDatabaseName("ix_purge_ledger_entries_subject_hash");
        entry.HasIndex(entity => entity.ReceiptReference).IsUnique().HasDatabaseName("uq_purge_ledger_entries_receipt_reference");
    }
}
