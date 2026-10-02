using Microsoft.Extensions.Compliance.Classification;

namespace LifeGraph.Infrastructure.Observability;

/// <summary>
/// Data that must never reach logs, metrics or traces in clear text (GEN-043). Tag a
/// <c>[LoggerMessage]</c> parameter with one of the attributes below and the redactor
/// erases it before any logging provider sees it.
/// </summary>
public static class LifeGraphDataClassifications
{
    public const string TaxonomyName = "LifeGraph";

    /// <summary>User content and personal data: Node content, Property values, URLs, e-mail.</summary>
    public static DataClassification Personal { get; } = new(TaxonomyName, nameof(Personal));

    /// <summary>Credentials: passwords, tokens, keys.</summary>
    public static DataClassification Secret { get; } = new(TaxonomyName, nameof(Secret));
}

public sealed class PersonalDataAttribute() : DataClassificationAttribute(LifeGraphDataClassifications.Personal);

public sealed class SecretDataAttribute() : DataClassificationAttribute(LifeGraphDataClassifications.Secret);
