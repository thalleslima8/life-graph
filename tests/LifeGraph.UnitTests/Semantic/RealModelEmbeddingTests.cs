using LifeGraph.Graph.Contracts;
using LifeGraph.Semantic.Embedding;
using Microsoft.ML.Tokenizers;

namespace LifeGraph.UnitTests.Semantic;

/// <summary>
/// The real multilingual-e5-small on a fixed Portuguese fixture (DA-175). Opt-in: every test is
/// skipped when the model files are not in <c>Semantic__Embeddings__ModelDirectory</c> (by
/// default <c>~/models/multilingual-e5-small</c>, where <c>scripts/download-embedding-model.sh</c>
/// puts them), so the CI, which has no model, skips them. Run it before closing a change to the
/// model or to the threshold.
/// </summary>
[Trait("Category", "RealModel")]
public sealed class RealModelEmbeddingTests(ITestOutputHelper output)
{
    /// <summary>The Soft Relation threshold calibrated for this model on the fixture below (DA-175).</summary>
    public const double CalibratedThreshold = 0.89;

    /// <summary>The search's minimum score calibrated for this model on the query fixture below (DA-175).</summary>
    public const double CalibratedSearchThreshold = 0.835;

    private const string ModelId = "intfloat/multilingual-e5-small";

    private static readonly string ModelDirectory =
        Environment.GetEnvironmentVariable("Semantic__Embeddings__ModelDirectory") is { Length: > 0 } directory
            ? directory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "models", "multilingual-e5-small");

    private static readonly EmbeddingOptions Options = new()
    {
        Provider = EmbeddingProvider.Onnx,
        ModelId = ModelId,
        ModelDirectory = ModelDirectory,
    };

    private static readonly (string Left, string Right)[] Related =
    [
        ("Marco Aurélio: Meditações, vídeo sobre a vida do imperador filósofo", "Estoicismo: a filosofia de Sêneca, Epicteto e Marco Aurélio"),
        ("Receita de pão de fermentação natural", "Como alimentar o levain para fazer pão caseiro"),
        ("Treino de corrida para a primeira meia maratona", "Planilha de treinos para correr 21 km"),
        ("Viagem a Lisboa: roteiro de três dias", "O que visitar em Portugal: Lisboa, Sintra e Porto"),
        ("Declaração do imposto de renda 2026", "Documentos para declarar o IR: informes de rendimentos e recibos médicos"),
    ];

    private static readonly (string Left, string Right)[] Unrelated =
    [
        ("Marco Aurélio: Meditações, vídeo sobre a vida do imperador filósofo", "Receita de pão de fermentação natural"),
        ("Treino de corrida para a primeira meia maratona", "Declaração do imposto de renda 2026"),
        ("Viagem a Lisboa: roteiro de três dias", "Configurar o roteador Wi-Fi de casa"),
        ("Estoicismo: a filosofia de Sêneca, Epicteto e Marco Aurélio", "Trocar o óleo do carro a cada dez mil quilômetros"),
        ("Reunião com o contador sobre a empresa", "Como alimentar o levain para fazer pão caseiro"),
        ("Aniversário da Ana no sábado", "Planilha de treinos para correr 21 km"),
    ];

    private static readonly (string Left, string Right)[] NearDuplicates =
    [
        ("Viagem a Lisboa: roteiro de três dias", "Viagem para Lisboa - roteiro de 3 dias"),
        ("Receita de pão de fermentação natural", "Receita de pão com fermentação natural"),
    ];

    private static readonly (string Query, string Passage)[] QueryMatches =
    [
        ("filosofia estoica", "Estoicismo: a filosofia de Sêneca, Epicteto e Marco Aurélio"),
        ("pão caseiro", "Como alimentar o levain para fazer pão caseiro"),
        ("fermentação natural", "Receita de pão de fermentação natural"),
        ("meia maratona", "Planilha de treinos para correr 21 km"),
        ("treino de corrida", "Treino de corrida para a primeira meia maratona"),
        ("o que fazer em Portugal", "Viagem a Lisboa: roteiro de três dias"),
        ("imposto de renda", "Documentos para declarar o IR: informes de rendimentos e recibos médicos"),
    ];

    /// <summary>
    /// A match only through knowledge the passage does not state: it scores in the band of the
    /// unrelated passages, so the threshold, which keeps every unrelated passage out, leaves it to
    /// the words. Logged, not asserted.
    /// </summary>
    private static readonly (string Query, string Passage)[] IndirectQueryMatches =
    [
        ("estoicismo", "Marco Aurélio: Meditações, vídeo sobre a vida do imperador filósofo"),
    ];

    private static readonly (string Query, string Passage)[] QueryMisses =
    [
        ("estoicismo", "Receita de pão de fermentação natural"),
        ("filosofia estoica", "Configurar o roteador Wi-Fi de casa"),
        ("pão caseiro", "Declaração do imposto de renda 2026"),
        ("meia maratona", "Viagem a Lisboa: roteiro de três dias"),
        ("treino de corrida", "Reunião com o contador sobre a empresa"),
        ("o que fazer em Portugal", "Trocar o óleo do carro a cada dez mil quilômetros"),
        ("imposto de renda", "Aniversário da Ana no sábado"),
        ("roteador", "Marco Aurélio: Meditações, vídeo sobre a vida do imperador filósofo"),
    ];

    private static bool ModelIsPresent =>
        File.Exists(Options.ModelPath) && File.Exists(Options.TokenizerPath);

    [Fact]
    public void The_tokenizer_gives_the_ids_of_the_xlm_r_vocabulary()
    {
        Assert.SkipUnless(ModelIsPresent, $"No embedding model in {ModelDirectory}.");
        using var model = File.OpenRead(Options.TokenizerPath);
        var tokenizer = SentencePieceTokenizer.Create(model, addBeginningOfSentence: false, addEndOfSentence: false);

        var ids = E5Encoding.ModelInputIds(tokenizer.EncodeToIds("Hello world!", addBeginningOfSentence: false, addEndOfSentence: false));

        // What Hugging Face's XLMRobertaTokenizer gives for the same text.
        Assert.Equal([0, 35378, 8999, 38, 2], ids);
    }

    [Fact]
    public async Task The_model_gives_unit_vectors_of_the_stored_dimension_and_the_same_vector_for_the_same_text()
    {
        Assert.SkipUnless(ModelIsPresent, $"No embedding model in {ModelDirectory}.");
        using var embedder = new OnnxEmbedder(Options);

        var first = await embedder.EmbedPassageAsync("Estoicismo", TestContext.Current.CancellationToken);
        var second = await embedder.EmbedPassageAsync("Estoicismo", TestContext.Current.CancellationToken);

        Assert.Equal(ISemanticGraph.EmbeddingDimension, first.Length);
        Assert.Equal(1, Math.Sqrt(Cosine(first, first)), 4);
        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public async Task The_calibrated_threshold_separates_related_from_unrelated_texts()
    {
        Assert.SkipUnless(ModelIsPresent, $"No embedding model in {ModelDirectory}.");
        using var embedder = new OnnxEmbedder(Options);

        var related = await ScoresAsync(embedder, Related, "related");
        var unrelated = await ScoresAsync(embedder, Unrelated, "unrelated");
        var nearDuplicates = await ScoresAsync(embedder, NearDuplicates, "near duplicate");

        Assert.All(related, score => Assert.True(score >= CalibratedThreshold, $"A related pair scored {score:F3}."));
        Assert.All(unrelated, score => Assert.True(score < CalibratedThreshold, $"An unrelated pair scored {score:F3}."));
        Assert.True(nearDuplicates.Min() > related.Max(), "A near duplicate scored below a related pair.");
    }

    [Fact]
    public async Task The_calibrated_search_threshold_separates_matching_from_unrelated_passages_for_a_query()
    {
        Assert.SkipUnless(ModelIsPresent, $"No embedding model in {ModelDirectory}.");
        using var embedder = new OnnxEmbedder(Options);

        var matches = await QueryScoresAsync(embedder, QueryMatches, "query match");
        var misses = await QueryScoresAsync(embedder, QueryMisses, "query miss");
        await QueryScoresAsync(embedder, IndirectQueryMatches, "indirect query match");

        Assert.All(matches, score => Assert.True(score >= CalibratedSearchThreshold, $"A matching passage scored {score:F3}."));
        Assert.All(misses, score => Assert.True(score < CalibratedSearchThreshold, $"An unrelated passage scored {score:F3}."));
    }

    private async Task<List<double>> QueryScoresAsync(OnnxEmbedder embedder, (string Query, string Passage)[] pairs, string label)
    {
        var scores = new List<double>();
        foreach (var (query, passage) in pairs)
        {
            var score = Cosine(
                await embedder.EmbedQueryAsync(query, TestContext.Current.CancellationToken),
                await embedder.EmbedPassageAsync(passage, TestContext.Current.CancellationToken));
            output.WriteLine($"{label}: {score:F3} ({query})");
            scores.Add(score);
        }

        return scores;
    }

    private async Task<List<double>> ScoresAsync(OnnxEmbedder embedder, (string Left, string Right)[] pairs, string label)
    {
        var scores = new List<double>();
        foreach (var (left, right) in pairs)
        {
            var score = Cosine(
                await embedder.EmbedPassageAsync(left, TestContext.Current.CancellationToken),
                await embedder.EmbedPassageAsync(right, TestContext.Current.CancellationToken));
            output.WriteLine($"{label}: {score:F3}");
            scores.Add(score);
        }

        return scores;
    }

    private static double Cosine(ReadOnlyMemory<float> left, ReadOnlyMemory<float> right)
    {
        var sum = 0d;
        for (var index = 0; index < left.Length; index++)
        {
            sum += left.Span[index] * (double)right.Span[index];
        }

        return sum;
    }
}
