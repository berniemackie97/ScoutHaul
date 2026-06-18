using Microsoft.Extensions.Logging.Abstractions;
using HaulScout.Application.Contracts;
using HaulScout.Infrastructure.Marketplace;

namespace HaulScout.Tests.Marketplace;

public sealed class CompositeBarcodeLookupTests
{
    // ── Stub provider ───────────────────────────────────────────────
    private sealed class StubBarcodeLookupProvider(
        string name,
        bool isConfigured,
        BarcodeLookupResult? result) : IBarcodeLookupProvider
    {
        public string Name => name;
        public bool IsConfigured => isConfigured;
        public int CallCount { get; private set; }

        public Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingBarcodeLookupProvider(string name) : IBarcodeLookupProvider
    {
        public string Name => name;
        public bool IsConfigured => true;
        public int CallCount { get; private set; }

        public Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new HttpRequestException("Simulated provider failure");
        }
    }

    private static BarcodeLookupResult MakeResult(string barcode, string title, string? brand = null) =>
        new(barcode, title, brand, null, null, null, barcode, null);

    // ── Tests ────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_first_successful_result()
    {
        var first = new StubBarcodeLookupProvider("First", true, MakeResult("123", "Widget"));
        var second = new StubBarcodeLookupProvider("Second", true, MakeResult("123", "Other Widget"));

        var sut = new CompositeBarcodeLookupService(
            [first, second],
            NullLogger<CompositeBarcodeLookupService>.Instance);

        BarcodeLookupResult? result = await sut.LookupAsync("123");

        Assert.NotNull(result);
        Assert.Equal("Widget", result.Title);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount); // never called — first succeeded
    }

    [Fact]
    public async Task Skips_unconfigured_providers()
    {
        var unconfigured = new StubBarcodeLookupProvider("Unconfigured", false, MakeResult("123", "Should not be returned"));
        var configured = new StubBarcodeLookupProvider("Configured", true, MakeResult("123", "Correct"));

        var sut = new CompositeBarcodeLookupService(
            [unconfigured, configured],
            NullLogger<CompositeBarcodeLookupService>.Instance);

        BarcodeLookupResult? result = await sut.LookupAsync("123");

        Assert.NotNull(result);
        Assert.Equal("Correct", result.Title);
        Assert.Equal(0, unconfigured.CallCount);
    }

    [Fact]
    public async Task Falls_through_when_provider_returns_null()
    {
        var empty = new StubBarcodeLookupProvider("Empty", true, null);
        var good = new StubBarcodeLookupProvider("Good", true, MakeResult("123", "Found"));

        var sut = new CompositeBarcodeLookupService(
            [empty, good],
            NullLogger<CompositeBarcodeLookupService>.Instance);

        BarcodeLookupResult? result = await sut.LookupAsync("123");

        Assert.NotNull(result);
        Assert.Equal("Found", result.Title);
        Assert.Equal(1, empty.CallCount);
        Assert.Equal(1, good.CallCount);
    }

    [Fact]
    public async Task Falls_through_when_provider_returns_empty_title()
    {
        var emptyTitle = new StubBarcodeLookupProvider("EmptyTitle", true,
            new BarcodeLookupResult("123", "  ", null, null, null, null, null, null));
        var good = new StubBarcodeLookupProvider("Good", true, MakeResult("123", "Real Product"));

        var sut = new CompositeBarcodeLookupService(
            [emptyTitle, good],
            NullLogger<CompositeBarcodeLookupService>.Instance);

        BarcodeLookupResult? result = await sut.LookupAsync("123");

        Assert.NotNull(result);
        Assert.Equal("Real Product", result.Title);
    }

    [Fact]
    public async Task Survives_provider_exception_and_continues()
    {
        var throwing = new ThrowingBarcodeLookupProvider("Broken");
        var good = new StubBarcodeLookupProvider("Good", true, MakeResult("123", "Survived"));

        var sut = new CompositeBarcodeLookupService(
            [throwing, good],
            NullLogger<CompositeBarcodeLookupService>.Instance);

        BarcodeLookupResult? result = await sut.LookupAsync("123");

        Assert.NotNull(result);
        Assert.Equal("Survived", result.Title);
        Assert.Equal(1, throwing.CallCount);
    }

    [Fact]
    public async Task Returns_null_when_all_providers_fail()
    {
        var empty = new StubBarcodeLookupProvider("Empty", true, null);
        var throwing = new ThrowingBarcodeLookupProvider("Broken");

        var sut = new CompositeBarcodeLookupService(
            [empty, throwing],
            NullLogger<CompositeBarcodeLookupService>.Instance);

        BarcodeLookupResult? result = await sut.LookupAsync("123");

        Assert.Null(result);
    }

    [Fact]
    public async Task Returns_null_for_blank_barcode()
    {
        var provider = new StubBarcodeLookupProvider("Provider", true, MakeResult("123", "Thing"));

        var sut = new CompositeBarcodeLookupService(
            [provider],
            NullLogger<CompositeBarcodeLookupService>.Instance);

        Assert.Null(await sut.LookupAsync("  "));
        Assert.Null(await sut.LookupAsync(""));
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Trims_barcode_whitespace()
    {
        string? capturedBarcode = null;
        var provider = new StubBarcodeLookupProvider("Provider", true, null);

        // Use a capturing provider to verify trimming
        var capturing = new CapturingProvider(b => capturedBarcode = b);
        var sut = new CompositeBarcodeLookupService(
            [capturing],
            NullLogger<CompositeBarcodeLookupService>.Instance);

        await sut.LookupAsync("  071100000740  ");

        Assert.Equal("071100000740", capturedBarcode);
    }

    private sealed class CapturingProvider(Action<string> onLookup) : IBarcodeLookupProvider
    {
        public string Name => "Capturing";
        public bool IsConfigured => true;

        public Task<BarcodeLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
        {
            onLookup(barcode);
            return Task.FromResult<BarcodeLookupResult?>(null);
        }
    }
}
