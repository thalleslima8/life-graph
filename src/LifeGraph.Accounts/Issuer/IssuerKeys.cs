using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace LifeGraph.Accounts.Issuer;

/// <summary>
/// The issuer's persistent RSA keys (DA-028): PEM files in a directory outside the
/// repository, created on the first start and readable only by the owner. Tokens survive a
/// restart, so a connected agent does not have to authorize again after every run.
/// </summary>
internal static class IssuerKeys
{
    public const string SigningKeyFile = "signing-key.pem";
    public const string EncryptionKeyFile = "encryption-key.pem";

    private const int RsaKeySize = 2048;

    public static (RsaSecurityKey Signing, RsaSecurityKey Encryption) LoadOrCreate(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(directory);
            }
            else
            {
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        return (LoadOrCreateKey(Path.Combine(directory, SigningKeyFile)), LoadOrCreateKey(Path.Combine(directory, EncryptionKeyFile)));
    }

    /// <summary>Generates a key the way <see cref="LoadOrCreate"/> stores it: an id derived from the public key.</summary>
    public static RsaSecurityKey Ephemeral() => WithKeyId(RSA.Create(RsaKeySize));

    private static RsaSecurityKey LoadOrCreateKey(string path)
    {
        var rsa = RSA.Create();
        if (File.Exists(path))
        {
            rsa.ImportFromPem(File.ReadAllText(path));
            return WithKeyId(rsa);
        }

        rsa.KeySize = RsaKeySize;
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var writer = new StreamWriter(path, options))
        {
            writer.Write(pem);
        }

        return WithKeyId(rsa);
    }

    // A stable kid: the same key keeps the same id across restarts, so JWKS consumers cache it.
    private static RsaSecurityKey WithKeyId(RSA rsa)
    {
        var thumbprint = SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());
        return new RsaSecurityKey(rsa) { KeyId = Base64UrlEncoder.Encode(thumbprint[..16]) };
    }
}
