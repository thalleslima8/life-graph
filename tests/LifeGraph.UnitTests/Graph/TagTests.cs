using LifeGraph.Graph.Domain;

namespace LifeGraph.UnitTests.Graph;

/// <summary>The rules of a Tag name (DA-161) and its rename.</summary>
public sealed class TagTests
{
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly DateTimeOffset Created = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddMinutes(5);

    [Theory]
    [InlineData("Leitura", "Leitura")]
    [InlineData("  #Leitura  ", "Leitura")]
    [InlineData("# leitura   lenta", "leitura lenta")]
    [InlineData("Ficção\tcientífica", "Ficção científica")]
    public void The_name_is_trimmed_without_the_leading_hash_and_with_inner_spaces_collapsed(string typed, string stored)
    {
        var tag = Tag.Create(Guid.CreateVersion7(), Account, typed, Created);

        Assert.True(tag.IsSuccess);
        Assert.Equal(stored, tag.Value!.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("a#b")]
    [InlineData("##leitura")]
    public void An_empty_name_or_a_hash_after_the_start_is_refused(string? typed)
    {
        Assert.False(Tag.Create(Guid.CreateVersion7(), Account, typed, Created).IsSuccess);
    }

    [Fact]
    public void A_name_is_at_most_fifty_characters()
    {
        Assert.True(Tag.Create(Guid.CreateVersion7(), Account, new string('a', GraphLimits.TagNameMaxLength), Created).IsSuccess);
        Assert.False(Tag.Create(Guid.CreateVersion7(), Account, new string('a', GraphLimits.TagNameMaxLength + 1), Created).IsSuccess);
    }

    [Theory]
    [InlineData("Ficção", "ficcao")]
    [InlineData("FICÇÃO", "ficcao")]
    [InlineData("Ação Política", "acao politica")]
    [InlineData("Fic\u0063\u0327a\u0303o", "ficcao")]
    [InlineData("Ærø Łódź", "æro lodz")]
    public void The_key_ignores_case_and_accents(string typed, string key)
    {
        Assert.Equal(key, Tag.Create(Guid.CreateVersion7(), Account, typed, Created).Value!.NameKey);
    }

    [Fact]
    public void Renaming_reports_whether_the_name_changed_and_keeps_an_invalid_name_out()
    {
        var tag = Tag.Create(Guid.CreateVersion7(), Account, "Leitura", Created).Value!;

        Assert.False(tag.Rename(" #Leitura ", Later).Value);
        Assert.Equal(Created, tag.UpdatedAt);
        Assert.True(tag.Rename("leitura", Later).Value);
        Assert.Equal(("leitura", "leitura", Later), (tag.Name, tag.NameKey, tag.UpdatedAt));
        Assert.False(tag.Rename("a#b", Later).IsSuccess);
        Assert.Equal("leitura", tag.Name);
    }

    [Fact]
    public void Every_accented_letter_of_the_folding_table_folds_to_one_plain_letter()
    {
        Assert.Equal(TagName.AccentedLetters.Length, TagName.PlainLetters.Length);
        Assert.All(TagName.AccentedLetters, accented => Assert.Matches("^[a-z]$", TagName.KeyOf(accented.ToString())));
    }
}
