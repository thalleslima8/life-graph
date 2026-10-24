using System.Text.Json;

namespace LifeGraph.UnitTests.Semantic;

/// <summary>
/// The thresholds the Host ships for multilingual-e5-small are the ones
/// <see cref="RealModelEmbeddingTests"/> calibrated (DA-175). This runs without the model, so a
/// change to either side that leaves the other behind fails in the CI.
/// </summary>
public sealed class CalibratedThresholdsTests
{
    private const string ModelId = "intfloat/multilingual-e5-small";

    [Fact]
    public void The_host_settings_ship_the_calibrated_soft_relation_and_search_thresholds()
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "HostSettings", "appsettings.json")));
        var semantic = settings.RootElement.GetProperty("Semantic");

        Assert.Equal(RealModelEmbeddingTests.CalibratedThreshold, semantic.GetProperty("SoftRelations").GetProperty("MinimumScores").GetProperty(ModelId).GetDouble());
        Assert.Equal(RealModelEmbeddingTests.CalibratedSearchThreshold, semantic.GetProperty("Search").GetProperty("MinimumScores").GetProperty(ModelId).GetDouble());
    }
}
