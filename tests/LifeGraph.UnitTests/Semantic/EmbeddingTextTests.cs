using LifeGraph.Graph.Contracts;
using LifeGraph.Semantic.Embedding;

namespace LifeGraph.UnitTests.Semantic;

public sealed class EmbeddingTextTests
{
    [Fact]
    public void The_text_is_the_title_the_body_and_the_text_property_values_in_order_without_blanks()
    {
        var node = NodeWith("  Viagem a Lisboa ", "Roteiro de três dias", ["Portugal", "   ", "férias"]);

        Assert.Equal("Viagem a Lisboa\n\nRoteiro de três dias\n\nPortugal\n\nférias", EmbeddingText.Of(node));
    }

    [Fact]
    public void An_empty_body_is_left_out()
    {
        Assert.Equal("Viagem a Lisboa", EmbeddingText.Of(NodeWith("Viagem a Lisboa", string.Empty, [])));
    }

    [Fact]
    public void The_hash_is_the_lowercase_sha256_of_the_text_and_the_same_text_has_the_same_hash()
    {
        var hash = EmbeddingText.HashOf("abc");

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        Assert.Equal(hash, EmbeddingText.HashOf("abc"));
        Assert.NotEqual(hash, EmbeddingText.HashOf("abd"));
    }

    [Fact]
    public void Hidden_flag_and_stored_hash_do_not_change_the_text()
    {
        var visible = NodeWith("Livro", "Notas", ["autor"]);
        var hidden = visible with { HiddenFromAgents = true, StoredContentHash = new string('0', 64) };

        Assert.Equal(EmbeddingText.Of(visible), EmbeddingText.Of(hidden));
    }

    private static NodeToEmbed NodeWith(string title, string body, IReadOnlyList<string> values) =>
        new(Guid.CreateVersion7(), title, body, values, HiddenFromAgents: false, StoredContentHash: null);
}
