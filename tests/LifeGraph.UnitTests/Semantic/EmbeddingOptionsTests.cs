using LifeGraph.Semantic;
using LifeGraph.Semantic.Embedding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LifeGraph.UnitTests.Semantic;

public sealed class EmbeddingOptionsTests
{
    private const string Section = EmbeddingOptions.SectionName;

    [Fact]
    public void A_host_without_a_provider_is_refused()
    {
        var failure = Assert.Throws<OptionsValidationException>(() => OptionsFrom(new Dictionary<string, string?>()));

        Assert.Contains(nameof(EmbeddingOptions.Provider), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_onnx_provider_without_its_model_id_or_directory_is_refused()
    {
        var failure = Assert.Throws<OptionsValidationException>(() => OptionsFrom(new Dictionary<string, string?>
        {
            [$"{Section}:Provider"] = "Onnx",
        }));

        Assert.Contains($"{Section}:{nameof(EmbeddingOptions.ModelId)}", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"{Section}:{nameof(EmbeddingOptions.ModelDirectory)}", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timeout_out_of_range_is_refused()
    {
        Assert.Throws<OptionsValidationException>(() => OptionsFrom(new Dictionary<string, string?>
        {
            [$"{Section}:Provider"] = "None",
            [$"{Section}:EmbedTimeout"] = "00:00:00",
        }));
    }

    [Fact]
    public void The_onnx_provider_is_accepted_before_its_files_exist_and_embeds_with_its_model_id()
    {
        using var services = ServicesFrom(new Dictionary<string, string?>
        {
            [$"{Section}:Provider"] = "Onnx",
            [$"{Section}:ModelId"] = "intfloat/multilingual-e5-small",
            [$"{Section}:ModelDirectory"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
        });

        var embedder = services.GetRequiredService<IEmbedder>();

        Assert.IsType<OnnxEmbedder>(embedder);
        Assert.Equal("intfloat/multilingual-e5-small", embedder.ModelId);
        Assert.False(embedder.SendsTextOffHost);
    }

    [Fact]
    public async Task Missing_model_files_fail_the_embedding_and_not_the_host()
    {
        using var services = ServicesFrom(new Dictionary<string, string?>
        {
            [$"{Section}:Provider"] = "Onnx",
            [$"{Section}:ModelId"] = "intfloat/multilingual-e5-small",
            [$"{Section}:ModelDirectory"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
        });

        var embedder = services.GetRequiredService<IEmbedder>();

        await Assert.ThrowsAsync<EmbedderUnavailableException>(
            () => embedder.EmbedPassageAsync("Estoicismo", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void No_provider_has_no_model()
    {
        using var services = ServicesFrom(new Dictionary<string, string?> { [$"{Section}:Provider"] = "None" });

        Assert.Equal(UnavailableEmbedder.NoModelId, services.GetRequiredService<IEmbedder>().ModelId);
    }

    private static EmbeddingOptions OptionsFrom(Dictionary<string, string?> settings)
    {
        using var services = ServicesFrom(settings);
        return services.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
    }

    private static ServiceProvider ServicesFrom(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSemanticModule()
            .BuildServiceProvider();
    }
}
