using System.Security.Cryptography;
using System.Text;

namespace LifeGraph.Infrastructure.Purging;

/// <summary>What a purge ledger row records the purge of (DA-084, DA-254).</summary>
public enum PurgedSubjectKind
{
    Account,
    Node,
}

/// <summary>
/// One row of the purge ledger (DA-084): a purged Account or Node, written in the purge's own
/// transaction, with no personal data. The subject's id is kept only as
/// <c>SHA-256(domain || kind || id)</c> (DA-253), so E14 can recompute it over restored ids
/// and purge them again; the kind, the date and the hash version stay in clear, outside the
/// hash. An Account's row also holds the random reference of the receipt the person gets,
/// which says nothing about the Account itself.
/// </summary>
public sealed class PurgeLedgerEntry
{
    /// <summary>The first hash version: no row carries an older one.</summary>
    public const short MinHashVersion = 1;

    /// <summary>
    /// The version of <see cref="SubjectHashOf"/> the rows written today use: still the first.
    /// A new hash gets the next number here; <see cref="MinHashVersion"/> stays.
    /// </summary>
    public const short CurrentHashVersion = MinHashVersion;

    public const int SubjectHashLength = 32;

    /// <summary>The longest <see cref="KindName"/> stored, with room for kinds to come.</summary>
    public const int SubjectKindMaxLength = 16;

    /// <summary>How many groups of hex digits a receipt reference has.</summary>
    public const int ReceiptReferenceGroupCount = 4;

    /// <summary>How many hex digits each group of a receipt reference has.</summary>
    public const int ReceiptReferenceGroupLength = 4;

    /// <summary>The groups and the hyphens between them: <c>XXXX-XXXX-XXXX-XXXX</c>.</summary>
    public const int ReceiptReferenceMaxLength =
        (ReceiptReferenceGroupCount * ReceiptReferenceGroupLength) + (ReceiptReferenceGroupCount - 1);

    // Domain separation: the hash of a ledger subject never equals a hash of the same id made elsewhere.
    private const string HashDomain = "lifegraph.purge_ledger.v1";

    private const char ReceiptReferenceSeparator = '-';

    private const int HexDigitsPerByte = 2;

    // Exactly the bytes whose hex digits fill every group.
    private const int ReceiptReferenceBytes = ReceiptReferenceGroupCount * ReceiptReferenceGroupLength / HexDigitsPerByte;

    private PurgeLedgerEntry(Guid id, PurgedSubjectKind subjectKind, byte[] subjectHash, DateTimeOffset purgedAt, string? receiptReference)
    {
        Id = id;
        SubjectKind = subjectKind;
        SubjectHash = subjectHash;
        HashVersion = CurrentHashVersion;
        PurgedAt = purgedAt;
        ReceiptReference = receiptReference;
    }

    public Guid Id { get; private set; }

    public PurgedSubjectKind SubjectKind { get; private set; }

    public byte[] SubjectHash { get; private set; }

    public short HashVersion { get; private set; }

    public DateTimeOffset PurgedAt { get; private set; }

    /// <summary>The receipt's reference, on an Account's row only.</summary>
    public string? ReceiptReference { get; private set; }

    /// <summary>The row of an Account deleted for good, with a new receipt reference.</summary>
    public static PurgeLedgerEntry ForAccount(Guid accountId, DateTimeOffset purgedAt) =>
        new(Guid.CreateVersion7(), PurgedSubjectKind.Account, SubjectHashOf(PurgedSubjectKind.Account, accountId), purgedAt, NewReceiptReference());

    /// <summary>The row of a Node purged at the end of its delete window (DA-254).</summary>
    public static PurgeLedgerEntry ForNode(Guid nodeId, DateTimeOffset purgedAt) =>
        new(Guid.CreateVersion7(), PurgedSubjectKind.Node, SubjectHashOf(PurgedSubjectKind.Node, nodeId), purgedAt, receiptReference: null);

    /// <summary>
    /// Hash version 1: SHA-256 over the UTF-8 text <c>lifegraph.purge_ledger.v1:{kind}:{id}</c>,
    /// the kind in lower case and the id in its hyphenated form (DA-253, GEN-062).
    /// </summary>
    public static byte[] SubjectHashOf(PurgedSubjectKind kind, Guid subjectId) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{HashDomain}:{KindName(kind)}:{subjectId:D}"));

    public static string KindName(PurgedSubjectKind kind) => kind switch
    {
        PurgedSubjectKind.Account => "account",
        PurgedSubjectKind.Node => "node",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only Accounts and Nodes enter the purge ledger."),
    };

    // Groups of hex digits from a CSPRNG (GEN-062): random, so it cannot be traced back to the Account.
    private static string NewReceiptReference()
    {
        var digits = Convert.ToHexString(RandomNumberGenerator.GetBytes(ReceiptReferenceBytes));
        return string.Join(
            ReceiptReferenceSeparator,
            Enumerable.Range(0, ReceiptReferenceGroupCount)
                .Select(group => digits.Substring(group * ReceiptReferenceGroupLength, ReceiptReferenceGroupLength)));
    }
}
