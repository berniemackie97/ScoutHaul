using HaulScout.Application.Services;

namespace HaulScout.Tests;

/// <summary>
/// The store domain is how the public storefront finds an organization, so what an operator pastes has to reduce to
/// exactly one bare host — and anything that isn't a real host must be refused rather than silently stored.
/// </summary>
public class StoreDomainNormalizerTests
{
    [Theory]
    // Whatever the operator pastes, the stored key is the bare host.
    [InlineData("overhaultrading.com", "overhaultrading.com")]
    [InlineData("  OverhaulTrading.COM  ", "overhaultrading.com")]
    [InlineData("https://overhaultrading.com", "overhaultrading.com")]
    [InlineData("http://www.overhaultrading.com", "overhaultrading.com")]
    [InlineData("https://www.overhaultrading.com/shop/items?q=1#top", "overhaultrading.com")]
    [InlineData("overhaultrading.com:8443", "overhaultrading.com")]
    [InlineData("overhaultrading.com.", "overhaultrading.com")]
    [InlineData("shop.overhaultrading.co.uk", "shop.overhaultrading.co.uk")]
    public void Reduces_operator_input_to_a_bare_host(string input, string expected) =>
        Assert.Equal(expected, StoreDomainNormalizer.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a domain")]
    [InlineData("localhost")]          // no TLD
    [InlineData("overhaultrading")]    // no TLD
    [InlineData("-bad.com")]           // label cannot start with a hyphen
    [InlineData("bad-.com")]           // label cannot end with a hyphen
    [InlineData("overhaultrading.c")]  // TLD must be at least two letters
    [InlineData("https://")]
    public void Refuses_anything_that_is_not_a_host(string input) =>
        Assert.Null(StoreDomainNormalizer.Normalize(input));

    /// <summary>Two spellings of the same store must normalize to the same key, or uniqueness means nothing.</summary>
    [Fact]
    public void Different_spellings_of_the_same_store_collapse_to_one_key()
    {
        string? a = StoreDomainNormalizer.Normalize("HTTPS://WWW.OverhaulTrading.com/");
        string? b = StoreDomainNormalizer.Normalize("overhaultrading.com");

        Assert.Equal(a, b);
        Assert.Equal("overhaultrading.com", a);
    }
}
