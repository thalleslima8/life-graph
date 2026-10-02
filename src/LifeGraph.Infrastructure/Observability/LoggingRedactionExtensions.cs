using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LifeGraph.Infrastructure.Observability;

public static class LoggingRedactionExtensions
{
    /// <summary>
    /// Erases every classified log parameter. Erasing (not masking or hashing) because a
    /// hash of short personal data is reversible by brute force.
    /// </summary>
    public static ILoggingBuilder AddLifeGraphRedaction(this ILoggingBuilder logging)
    {
        logging.EnableRedaction();
        logging.Services.AddRedaction(redaction => redaction.SetRedactor<ErasingRedactor>(
            new DataClassificationSet(LifeGraphDataClassifications.Personal),
            new DataClassificationSet(LifeGraphDataClassifications.Secret)));

        return logging;
    }
}
