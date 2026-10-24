using LifeGraph.Semantic.Embedding;

namespace LifeGraph.UnitTests.Semantic;

public sealed class E5EncodingTests
{
    [Fact]
    public void The_pieces_are_shifted_to_the_xlm_r_vocabulary_between_the_sentence_marks()
    {
        var ids = E5Encoding.ModelInputIds([35377, 8998, 0, 5]);

        Assert.Equal([E5Encoding.BeginOfSentence, 35378, 8999, E5Encoding.Unknown, 6, E5Encoding.EndOfSentence], ids);
    }

    [Fact]
    public void A_text_longer_than_the_model_takes_is_cut_to_fit_with_its_end_mark()
    {
        var pieces = Enumerable.Range(10, 600).ToList();

        var ids = E5Encoding.ModelInputIds(pieces);

        Assert.Equal(E5Encoding.MaxTokens, ids.Length);
        Assert.Equal(11, ids[1]);
        Assert.Equal(E5Encoding.EndOfSentence, ids[^1]);
    }

    [Fact]
    public void The_embedding_is_the_unit_mean_of_the_tokens_the_mask_keeps()
    {
        float[] tokenVectors = [3, 0, 1, 4, 100, 100];

        var embedding = E5Encoding.MeanPooledUnitVector(tokenVectors, [1, 1, 0], dimension: 2);

        // Mean of (3, 0) and (1, 4) is (2, 2); the masked (100, 100) does not count.
        Assert.Equal(1 / MathF.Sqrt(2), embedding[0], 5);
        Assert.Equal(1 / MathF.Sqrt(2), embedding[1], 5);
    }

    [Fact]
    public void An_all_zero_output_is_a_failure_of_the_model()
    {
        Assert.Throws<EmbedderUnavailableException>(() => E5Encoding.MeanPooledUnitVector(new float[4], [1, 1], dimension: 2));
    }
}
