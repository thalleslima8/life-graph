using System.Buffers.Text;
using System.Security.Cryptography;

namespace LifeGraph.Graph.Domain;

/// <summary>
/// The capability token of a SharedGraphView (DA-079, DA-221): random bytes from the system's
/// cryptographic generator, written base64url for the URL fragment. Only its SHA-256 hash is
/// stored; a token this random needs no adaptive hash (GEN-062), and the hash lets the
/// directory find the share by equality.
/// </summary>
public sealed class ShareToken
{
    private ShareToken(string value, byte[] hash)
    {
        Value = value;
        Hash = hash;
    }

    /// <summary>The token as the link carries it. Shown once, never stored nor logged (GEN-043).</summary>
    public string Value { get; }

    public byte[] Hash { get; }

    public static ShareToken New() => FromBytes(RandomNumberGenerator.GetBytes(SharingLimits.TokenBytes));

    /// <summary>The token of these random bytes; <see cref="New"/> is the only caller outside tests.</summary>
    internal static ShareToken FromBytes(ReadOnlySpan<byte> randomBytes) =>
        new(Base64Url.EncodeToString(randomBytes), HashOf(randomBytes));

    /// <summary>
    /// The hash of a token a visitor presents, or <c>null</c> when it is not one this class
    /// writes: wrong alphabet or wrong length, so it can match no share.
    /// </summary>
    public static byte[]? HashOfPresented(string? presented)
    {
        if (string.IsNullOrEmpty(presented)
            || presented.Length != Base64Url.GetEncodedLength(SharingLimits.TokenBytes)
            || !Base64Url.IsValid(presented, out var decodedLength)
            || decodedLength != SharingLimits.TokenBytes)
        {
            return null;
        }

        return HashOf(Base64Url.DecodeFromChars(presented));
    }

    /// <summary>A new opaque reference for the operator and the abuse report (DA-217, DA-218); it says nothing of the token.</summary>
    public static string NewPublicRef() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SharingLimits.PublicRefBytes));

    private static byte[] HashOf(ReadOnlySpan<byte> randomBytes) => SHA256.HashData(randomBytes);
}
