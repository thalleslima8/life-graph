using System.Security.Cryptography;
using System.Text;
using LifeGraph.Infrastructure.Purging;

namespace LifeGraph.UnitTests.Purging;

/// <summary>The PII-free purge ledger row (DA-084, DA-253, DA-254).</summary>
public sealed class PurgeLedgerEntryTests
{
    private static readonly Guid SubjectId = Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");
    private static readonly DateTimeOffset PurgedAt = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_subject_hash_is_sha256_of_the_domain_the_kind_and_the_id_so_e14_can_recompute_it()
    {
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes("lifegraph.purge_ledger.v1:account:0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b"));

        var entry = PurgeLedgerEntry.ForAccount(SubjectId, PurgedAt);

        Assert.Equal(expected, entry.SubjectHash);
        Assert.Equal(PurgeLedgerEntry.CurrentHashVersion, entry.HashVersion);
        Assert.Equal(PurgedAt, entry.PurgedAt);
        Assert.Equal(PurgedSubjectKind.Account, entry.SubjectKind);
    }

    [Fact]
    public void The_same_id_hashes_differently_as_an_account_and_as_a_node()
    {
        Assert.NotEqual(
            PurgeLedgerEntry.SubjectHashOf(PurgedSubjectKind.Account, SubjectId),
            PurgeLedgerEntry.SubjectHashOf(PurgedSubjectKind.Node, SubjectId));
    }

    [Fact]
    public void An_accounts_row_carries_a_new_random_receipt_reference_each_time()
    {
        var first = PurgeLedgerEntry.ForAccount(SubjectId, PurgedAt);
        var second = PurgeLedgerEntry.ForAccount(SubjectId, PurgedAt);

        Assert.Matches("^[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", first.ReceiptReference);
        Assert.Equal(PurgeLedgerEntry.ReceiptReferenceMaxLength, first.ReceiptReference!.Length);
        Assert.NotEqual(first.ReceiptReference, second.ReceiptReference);
    }

    [Fact]
    public void A_nodes_row_has_no_receipt()
    {
        var entry = PurgeLedgerEntry.ForNode(SubjectId, PurgedAt);

        Assert.Equal(PurgedSubjectKind.Node, entry.SubjectKind);
        Assert.Null(entry.ReceiptReference);
        Assert.Equal(PurgeLedgerEntry.SubjectHashOf(PurgedSubjectKind.Node, SubjectId), entry.SubjectHash);
    }
}
