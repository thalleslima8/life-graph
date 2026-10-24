using LifeGraph.Resources.Domain;
using LifeGraph.Resources.Extraction;

namespace LifeGraph.UnitTests.Resources;

/// <summary>A response read only for its headers still says what kind of Resource it is (DA-058).</summary>
public sealed class MediaTypeKindsTests
{
    [Theory]
    [InlineData("application/pdf", ResourceKind.Pdf)]
    [InlineData("APPLICATION/PDF", ResourceKind.Pdf)]
    [InlineData("image/png", ResourceKind.Image)]
    [InlineData("video/mp4", ResourceKind.Video)]
    [InlineData("text/plain", ResourceKind.Document)]
    public void A_known_media_type_names_its_kind(string mediaType, ResourceKind expected)
    {
        Assert.Equal(expected, MediaTypeKinds.KindOf(mediaType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/octet-stream")]
    public void An_unknown_media_type_names_no_kind(string? mediaType)
    {
        Assert.Null(MediaTypeKinds.KindOf(mediaType));
    }

    [Theory]
    [InlineData("  um   título\n\tcom espaços  ", 100, "um título com espaços")]
    [InlineData("a\u0000b​c", 100, "abc")]
    [InlineData("   ", 100, null)]
    [InlineData(null, 100, null)]
    [InlineData("abcdef", 3, "abc")]
    [InlineData("ab\U0001F600", 3, "ab")]
    public void Clean_collapses_whitespace_drops_invisible_characters_and_cuts_safely(string? text, int max, string? expected)
    {
        Assert.Equal(expected, ExtractedMetadata.Clean(text, max));
    }
}
