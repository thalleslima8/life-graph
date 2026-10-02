using LifeGraph.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace LifeGraph.UnitTests.Observability;

public sealed partial class LoggingRedactionTests
{
    [Fact]
    public void Classified_parameters_are_erased_and_unclassified_ones_are_kept()
    {
        using var services = new ServiceCollection()
            .AddLogging(logging => logging.AddFakeLogging().AddLifeGraphRedaction())
            .BuildServiceProvider();
        var logger = services.GetRequiredService<ILogger<LoggingRedactionTests>>();

        LogCapture(logger, "Consulta cardiologia", "tok_live_123", 3);

        var record = services.GetFakeLogCollector().LatestRecord;
        Assert.DoesNotContain("Consulta cardiologia", record.Message);
        Assert.DoesNotContain("tok_live_123", record.Message);
        Assert.Equal(string.Empty, record.GetStructuredStateValue("Title"));
        Assert.Equal(string.Empty, record.GetStructuredStateValue("Token"));
        Assert.Equal("3", record.GetStructuredStateValue("NodeCount"));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Captured {Title} with {Token} into {NodeCount} nodes")]
    private static partial void LogCapture(
        ILogger logger,
        [PersonalData] string title,
        [SecretData] string token,
        int nodeCount);
}
