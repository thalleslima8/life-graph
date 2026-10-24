using LifeGraph.Semantic;
using LifeGraph.Semantic.Embedding;
using LifeGraph.Semantic.SoftRelations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LifeGraph.UnitTests.Semantic;

public sealed class SoftRelationOptionsTests
{
    private const string Model = "intfloat/multilingual-e5-small";

    [Fact]
    public void An_onnx_model_with_no_minimum_score_of_its_own_is_refused()
    {
        var failure = Assert.Throws<OptionsValidationException>(() => OptionsFrom(OnnxSettings()));

        Assert.Contains(Model, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    public void A_minimum_score_that_is_not_a_similarity_is_refused(string score)
    {
        var settings = OnnxSettings();
        settings[$"{SoftRelationOptions.SectionName}:MinimumScores:{Model}"] = score;

        Assert.Throws<OptionsValidationException>(() => OptionsFrom(settings));
    }

    [Fact]
    public void A_minimum_score_is_read_per_model_with_the_default_top_k()
    {
        var settings = OnnxSettings();
        settings[$"{SoftRelationOptions.SectionName}:MinimumScores:{Model}"] = "0.89";

        var options = OptionsFrom(settings);

        Assert.Equal((0.89, null, 5), (options.MinimumScoreOf(Model), options.MinimumScoreOf("other/model"), options.TopK));
    }

    private static Dictionary<string, string?> OnnxSettings() => new()
    {
        [$"{EmbeddingOptions.SectionName}:Provider"] = "Onnx",
        [$"{EmbeddingOptions.SectionName}:ModelId"] = Model,
        [$"{EmbeddingOptions.SectionName}:ModelDirectory"] = "/models/e5",
    };

    private static SoftRelationOptions OptionsFrom(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSemanticModule()
            .BuildServiceProvider();
        return services.GetRequiredService<IOptions<SoftRelationOptions>>().Value;
    }
}
