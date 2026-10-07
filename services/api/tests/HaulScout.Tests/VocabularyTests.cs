using HaulScout.Domain;

namespace HaulScout.Tests;

/// <summary>Free text becomes a listed term, or nothing.</summary>
public class VocabularyTests
{
    [Theory]
    [InlineData("black", "Black")]
    [InlineData("  GREY ", "Gray")]
    [InlineData("Off-White", "Cream")]
    [InlineData("navy blue", "Navy")]
    [InlineData("Rose gold tone", "Rose gold")]
    [InlineData("heather grey", "Gray")]
    [InlineData("Chartreuse-ish", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_color_is_read_as_the_listed_term_it_names(string? written, string? expected) =>
        Assert.Equal(expected, ItemColors.Standard.Canonical(written));

    [Fact]
    public void A_list_is_split_however_it_was_written_and_kept_in_order_once()
    {
        Assert.Equal(["Blue", "White"], ItemColors.Standard.CanonicalList(["Blue, White"], ItemColors.Max));
        Assert.Equal(["Black", "Gold"], ItemColors.Standard.CanonicalList(["black and gold"], ItemColors.Max));
        Assert.Equal(["Gray"], ItemColors.Standard.CanonicalList(["Grey", "gray"], ItemColors.Max));
        Assert.Equal(["Red", "Blue"], ItemColors.Standard.CanonicalList(["Red / Blue / Green"], ItemColors.Max));
    }

    [Fact]
    public void A_word_inside_another_word_is_not_a_match()
    {
        Vocabulary departments = new(["Women", "Men"]);

        Assert.Equal("Women", departments.Canonical("Women's tops"));
        Assert.Null(departments.Canonical("Menswear-inspired"));
    }

    [Fact]
    public void An_other_spelling_for_a_term_the_list_lacks_is_ignored()
    {
        Vocabulary watches = ItemMaterials.Watches;

        Assert.Null(watches.Canonical("oak"));
        Assert.Equal("Wood", ItemMaterials.Home.Canonical("oak"));
    }
}
