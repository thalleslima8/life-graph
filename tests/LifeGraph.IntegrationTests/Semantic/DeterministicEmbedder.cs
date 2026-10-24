using System.Security.Cryptography;
using System.Text;
using LifeGraph.Graph.Domain;
using LifeGraph.Semantic.Embedding;

namespace LifeGraph.IntegrationTests.Semantic;

/// <summary>
/// The embedding model of every automated test (DA-175, GEN-072): no model file and no network.
/// The same text always gets the same unit vector, seeded by the text's hash.
/// </summary>
public sealed class DeterministicEmbedder : IEmbedder
{
    public const string Model = "test/deterministic";

    /// <summary>The least similarity of a Soft pair by this model; two unpinned texts are far below it, the same text is above.</summary>
    public const string MinimumScore = "0.8";

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, float[]> _pinned = new(StringComparer.Ordinal);

    private int _calls;

    public string ModelId => Model;

    /// <summary>Plays a third-party model, which a Node hidden from agents never reaches (DA-072).</summary>
    public bool SendsTextOffHost { get; set; }

    /// <summary>Plays a model that is down: every call fails.</summary>
    public bool Fails { get; set; }

    /// <summary>How many texts it embedded.</summary>
    public int Calls => _calls;

    public Task<ReadOnlyMemory<float>> EmbedPassageAsync(string text, CancellationToken cancellationToken)
    {
        if (Fails)
        {
            throw new EmbedderUnavailableException("The test model is down.");
        }

        Interlocked.Increment(ref _calls);
        return Task.FromResult<ReadOnlyMemory<float>>(_pinned.TryGetValue(text, out var pinned) ? pinned : VectorOf(text));
    }

    /// <summary>
    /// The query gets the vector a passage of the same text gets, so a search for a Node's exact
    /// text is as similar as it can be, and a pinned text is found at its known angle.
    /// </summary>
    public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        if (Fails)
        {
            throw new EmbedderUnavailableException("The test model is down.");
        }

        return Task.FromResult<ReadOnlyMemory<float>>(_pinned.TryGetValue(text, out var pinned) ? pinned : VectorOf(text));
    }

    /// <summary>
    /// Gives the text a unit vector at <paramref name="angle"/> radians in one plane, so two
    /// pinned texts are exactly <c>cos(a - b)</c> similar (DA-175: fake vectors, known scores).
    /// </summary>
    public void PinAtAngle(string text, double angle)
    {
        var vector = new float[NodeEmbedding.StoredDimension];
        vector[0] = (float)Math.Cos(angle);
        vector[1] = (float)Math.Sin(angle);
        _pinned[text] = vector;
    }

    public static float[] VectorOf(string text)
    {
        var random = new Random(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0));
        var vector = new float[NodeEmbedding.StoredDimension];
        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(random.NextDouble() - 0.5);
        }

        var length = MathF.Sqrt(vector.Sum(component => component * component));
        return [.. vector.Select(component => component / length)];
    }
}
