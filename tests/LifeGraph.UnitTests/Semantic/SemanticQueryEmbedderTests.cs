using LifeGraph.Semantic;
using LifeGraph.Semantic.Embedding;
using LifeGraph.Semantic.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LifeGraph.UnitTests.Semantic;

/// <summary>
/// The search's query embedder never fails a search for the model (DA-183): with no score for the
/// model, a model that fails or one that takes too long, it answers nothing and the search finds
/// by words; with a score, the vector carries the model and the score.
/// </summary>
public sealed class SemanticQueryEmbedderTests
{
    private const string Model = "intfloat/multilingual-e5-small";

    [Fact]
    public async Task A_question_is_embedded_as_a_query_with_the_model_and_its_least_score()
    {
        var embedder = new QueryEmbedder();

        var embedding = await EmbedderOf(embedder, minimumScore: 0.8).EmbedQueryAsync("filosofia", TestContext.Current.CancellationToken);

        Assert.NotNull(embedding);
        Assert.Equal((Model, 0.8), (embedding.ModelId, embedding.MinimumScore));
        Assert.Equal([1f, 0f, 0f], embedding.Vector.ToArray());
        Assert.Equal(["filosofia"], embedder.Queries);
    }

    [Fact]
    public async Task A_model_with_no_score_of_its_own_finds_by_words_and_is_not_called()
    {
        var embedder = new QueryEmbedder();

        var embedding = await EmbedderOf(embedder, minimumScore: null).EmbedQueryAsync("filosofia", TestContext.Current.CancellationToken);

        Assert.Null(embedding);
        Assert.Empty(embedder.Queries);
    }

    [Fact]
    public async Task A_model_that_fails_finds_by_words()
    {
        var embedder = new QueryEmbedder { Fails = true };

        Assert.Null(await EmbedderOf(embedder, minimumScore: 0.8).EmbedQueryAsync("filosofia", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_model_that_takes_longer_than_the_timeout_finds_by_words()
    {
        var embedder = new QueryEmbedder { WaitsForCancellation = true };

        Assert.Null(await EmbedderOf(embedder, minimumScore: 0.8, TimeSpan.FromMilliseconds(100)).EmbedQueryAsync("filosofia", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_search_the_caller_cancels_is_cancelled_not_degraded()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            EmbedderOf(new QueryEmbedder { WaitsForCancellation = true }, minimumScore: 0.8).EmbedQueryAsync("filosofia", cancelled.Token));
    }

    [Fact]
    public void An_onnx_model_with_no_search_score_of_its_own_is_refused()
    {
        var failure = Assert.Throws<OptionsValidationException>(() => OptionsFrom(OnnxSettings()));

        Assert.Contains(Model, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    public void A_search_score_that_is_not_a_similarity_is_refused(string score)
    {
        var settings = OnnxSettings();
        settings[$"{SemanticSearchOptions.SectionName}:MinimumScores:{Model}"] = score;

        Assert.Throws<OptionsValidationException>(() => OptionsFrom(settings));
    }

    [Fact]
    public void A_search_score_is_read_per_model_with_the_default_timeout()
    {
        var settings = OnnxSettings();
        settings[$"{SemanticSearchOptions.SectionName}:MinimumScores:{Model}"] = "0.8";

        var options = OptionsFrom(settings);

        Assert.Equal((0.8, null, TimeSpan.FromSeconds(3)), (options.MinimumScoreOf(Model), options.MinimumScoreOf("other/model"), options.QueryTimeout));
    }

    private static SemanticQueryEmbedder EmbedderOf(IEmbedder embedder, double? minimumScore, TimeSpan? timeout = null)
    {
        var options = new SemanticSearchOptions { QueryTimeout = timeout ?? TimeSpan.FromSeconds(3) };
        if (minimumScore is { } score)
        {
            options.MinimumScores[Model] = score;
        }

        return new SemanticQueryEmbedder(embedder, Options.Create(options), NullLogger<SemanticQueryEmbedder>.Instance);
    }

    private static Dictionary<string, string?> OnnxSettings() => new()
    {
        [$"{EmbeddingOptions.SectionName}:Provider"] = "Onnx",
        [$"{EmbeddingOptions.SectionName}:ModelId"] = Model,
        [$"{EmbeddingOptions.SectionName}:ModelDirectory"] = "/models/e5",
    };

    private static SemanticSearchOptions OptionsFrom(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSemanticModule()
            .BuildServiceProvider();
        return services.GetRequiredService<IOptions<SemanticSearchOptions>>().Value;
    }

    private sealed class QueryEmbedder : IEmbedder
    {
        public string ModelId => Model;

        public bool SendsTextOffHost => false;

        public bool Fails { get; init; }

        public bool WaitsForCancellation { get; init; }

        public List<string> Queries { get; } = [];

        public Task<ReadOnlyMemory<float>> EmbedPassageAsync(string text, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(string text, CancellationToken cancellationToken)
        {
            if (Fails)
            {
                throw new EmbedderUnavailableException("The test model is down.");
            }

            if (WaitsForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            Queries.Add(text);
            return new float[] { 1, 0, 0 };
        }
    }
}
