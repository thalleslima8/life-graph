using System.Security.Cryptography;
using System.Text;
using LifeGraph.Graph.Contracts;

namespace LifeGraph.Semantic.Embedding;

/// <summary>
/// The text of a Node that is embedded (DA-069): its title, its body and the values of its text
/// Properties (Outras propriedades and a Resource's metadata included), each blank one left out,
/// in a stable order. Its hash tells whether the text changed since the stored embedding.
/// </summary>
internal static class EmbeddingText
{
    public static string Of(NodeToEmbed node) =>
        string.Join(
            "\n\n",
            new[] { node.Title, node.Body }
                .Concat(node.TextPropertyValues)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0));

    /// <summary>The SHA-256 of the text, in lowercase hexadecimal.</summary>
    public static string HashOf(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
