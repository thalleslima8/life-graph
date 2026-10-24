namespace LifeGraph.Semantic.Embedding;

/// <summary>
/// What the e5 models expect around the ONNX call, kept apart from it so a unit test checks it
/// with no model file (GEN-072). The text goes in with its role's prefix (<c>passage: </c> for a
/// Node, <c>query: </c> for a search), the SentencePiece ids are shifted to the XLM-R vocabulary
/// and wrapped in its sentence marks, and the token vectors come out mean-pooled over the
/// attention mask and normalized, so the cosine of two embeddings is their dot product.
/// </summary>
internal static class E5Encoding
{
    public const string PassagePrefix = "passage: ";

    public const string QueryPrefix = "query: ";

    /// <summary>The longest input the model takes, sentence marks included.</summary>
    public const int MaxTokens = 512;

    // The XLM-R vocabulary puts four tokens of its own first (<s>, <pad>, </s>, <unk>) and every
    // SentencePiece piece one place after its id in the SentencePiece model, whose id 0 is its
    // own <unk>: the shift Hugging Face's XLMRobertaTokenizer applies.
    public const long BeginOfSentence = 0;

    public const long EndOfSentence = 2;

    public const long Unknown = 3;

    private const int VocabularyOffset = 1;

    /// <summary>The model's input ids: the pieces shifted, cut to fit, between the sentence marks.</summary>
    public static long[] ModelInputIds(IReadOnlyList<int> sentencePieceIds)
    {
        var kept = Math.Min(sentencePieceIds.Count, MaxTokens - 2);
        var ids = new long[kept + 2];
        ids[0] = BeginOfSentence;
        for (var index = 0; index < kept; index++)
        {
            var piece = sentencePieceIds[index];
            ids[index + 1] = piece == 0 ? Unknown : piece + VocabularyOffset;
        }

        ids[^1] = EndOfSentence;
        return ids;
    }

    /// <summary>
    /// The mean of the token vectors the mask keeps, scaled to length 1. <paramref name="tokenVectors"/>
    /// holds one row of <paramref name="dimension"/> numbers per token.
    /// </summary>
    public static float[] MeanPooledUnitVector(ReadOnlySpan<float> tokenVectors, ReadOnlySpan<long> attentionMask, int dimension)
    {
        if (tokenVectors.Length != attentionMask.Length * dimension)
        {
            throw new ArgumentException("The token vectors do not match the mask and the dimension.", nameof(tokenVectors));
        }

        var pooled = new float[dimension];
        var kept = 0;
        for (var token = 0; token < attentionMask.Length; token++)
        {
            if (attentionMask[token] == 0)
            {
                continue;
            }

            kept++;
            var row = tokenVectors.Slice(token * dimension, dimension);
            for (var index = 0; index < dimension; index++)
            {
                pooled[index] += row[index];
            }
        }

        var squaredLength = 0d;
        for (var index = 0; index < dimension; index++)
        {
            pooled[index] /= Math.Max(kept, 1);
            squaredLength += pooled[index] * (double)pooled[index];
        }

        var length = Math.Sqrt(squaredLength);
        if (length == 0)
        {
            throw new EmbedderUnavailableException("The model returned an empty embedding.");
        }

        for (var index = 0; index < dimension; index++)
        {
            pooled[index] = (float)(pooled[index] / length);
        }

        return pooled;
    }
}
