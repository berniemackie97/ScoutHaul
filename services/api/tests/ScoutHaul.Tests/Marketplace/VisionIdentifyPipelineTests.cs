using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Infrastructure.Marketplace;

namespace ScoutHaul.Tests.Marketplace;

/// <summary>
/// Tests the full Vision identification pipeline:
/// Google Cloud Vision API response → ParseResponse → VisionIdentifyResult → search query.
///
/// Each test uses a realistic Cloud Vision JSON response modeled after actual API output
/// for specific product types. These fixtures represent what Google returns for product photos
/// taken in real scouting scenarios.
/// </summary>
public sealed class VisionIdentifyPipelineTests
{
    // ── Helpers ──────────────────────────────────────────────────────

    private static GoogleCloudVisionService CreateService(string apiKey = "test-key")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleCloudVision:ApiKey"] = apiKey,
            })
            .Build();

        return new GoogleCloudVisionService(
            new HttpClient(new FakeHandler("")), // not used — we call via reflection
            config,
            NullLogger<GoogleCloudVisionService>.Instance);
    }

    /// <summary>
    /// Invokes the private ParseResponse method via the public IdentifyAsync path
    /// by injecting a fake HTTP handler that returns the given JSON.
    /// </summary>
    private static async Task<VisionIdentifyResult> ParseVisionResponse(string json)
    {
        var handler = new FakeHandler(json);
        var httpClient = new HttpClient(handler);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleCloudVision:ApiKey"] = "test-key",
            })
            .Build();

        var service = new GoogleCloudVisionService(
            httpClient, config, NullLogger<GoogleCloudVisionService>.Instance);

        // Send a minimal base64 payload (just needs to be > 100 chars to pass validation)
        string dummyBase64 = new('A', 200);
        return await service.IdentifyAsync(dummyBase64, CancellationToken.None);
    }

    private sealed class FakeHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 1: Kid's Art Easel (branded product with text, matching pages)
    // This is the exact product type that should return high-confidence results.
    // Google Vision should identify it via web detection + text on the product.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Kids_art_easel_returns_high_confidence_with_search_query()
    {
        VisionIdentifyResult result = await ParseVisionResponse(KidsEaselVisionJson);

        Assert.NotNull(result.BestGuessLabel);
        Assert.Contains("easel", result.BestGuessLabel, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.WebEntities);
        Assert.NotEmpty(result.MatchingPages);
        Assert.NotEmpty(result.Labels);
        Assert.NotNull(result.SearchQuery);
        Assert.True(result.SearchQuery.Length >= 3, "Search query should be substantive");
    }

    [Fact]
    public async Task Kids_art_easel_extracts_matching_marketplace_pages()
    {
        VisionIdentifyResult result = await ParseVisionResponse(KidsEaselVisionJson);

        Assert.True(result.MatchingPages.Count >= 2, "Should find marketplace pages");
        Assert.Contains(result.MatchingPages, p => p.Url.Contains("amazon.com"));
        Assert.Contains(result.MatchingPages, p => p.Url.Contains("ebay.com"));
    }

    [Fact]
    public async Task Kids_art_easel_detects_text_on_product()
    {
        VisionIdentifyResult result = await ParseVisionResponse(KidsEaselVisionJson);

        Assert.NotNull(result.DetectedText);
        Assert.Contains("Painting", result.DetectedText, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 2: Plain Grey Chair (no text, no brand — label-only detection)
    // This tests the label fallback path. Vision returns labels like "Chair",
    // "Furniture" but no bestGuessLabel or web entities with descriptions.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Plain_chair_uses_labels_as_search_query_fallback()
    {
        VisionIdentifyResult result = await ParseVisionResponse(PlainChairVisionJson);

        Assert.NotNull(result.SearchQuery);
        Assert.True(result.SearchQuery.Length >= 3, "Label fallback should produce a usable query");
        // Should contain "Chair" or "Furniture" from labels
        Assert.True(
            result.SearchQuery.Contains("Chair", StringComparison.OrdinalIgnoreCase)
            || result.SearchQuery.Contains("Furniture", StringComparison.OrdinalIgnoreCase),
            $"Query '{result.SearchQuery}' should reference chair or furniture");
    }

    [Fact]
    public async Task Plain_chair_returns_labels_even_without_web_detection()
    {
        VisionIdentifyResult result = await ParseVisionResponse(PlainChairVisionJson);

        Assert.NotEmpty(result.Labels);
        Assert.Contains(result.Labels, l => l.Contains("Chair", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 3: Sony Headphones (branded electronics with logo)
    // Logo detection + web detection should yield very high confidence.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Sony_headphones_returns_brand_and_product_name()
    {
        VisionIdentifyResult result = await ParseVisionResponse(SonyHeadphonesVisionJson);

        Assert.NotNull(result.BestGuessLabel);
        Assert.NotEmpty(result.DetectedLogos);
        Assert.Contains(result.DetectedLogos, l => l.Contains("Sony", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(result.SearchQuery);
        Assert.True(result.SearchQuery.Length >= 5);
    }

    [Fact]
    public async Task Sony_headphones_has_web_entities_with_scores()
    {
        VisionIdentifyResult result = await ParseVisionResponse(SonyHeadphonesVisionJson);

        Assert.True(result.WebEntities.Count >= 3);
        Assert.All(result.WebEntities, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Description));
            Assert.True(e.Score >= 0);
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 4: Vintage Clothing (no brand, no text — visual labels only)
    // Similar to the chair scenario but for clothing.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Vintage_clothing_produces_searchable_query_from_labels()
    {
        VisionIdentifyResult result = await ParseVisionResponse(VintageClothingVisionJson);

        Assert.NotNull(result.SearchQuery);
        Assert.True(result.SearchQuery.Length >= 3);
        // Should use labels, not return empty
        Assert.NotEmpty(result.Labels);
    }

    // ═══════════════════════════════════════════════════════════════
    // Test 5: Board Game Box (text-heavy, recognizable product)
    // Should have strong text detection + web detection.
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Board_game_box_has_best_guess_and_text()
    {
        VisionIdentifyResult result = await ParseVisionResponse(BoardGameVisionJson);

        Assert.NotNull(result.BestGuessLabel);
        Assert.NotNull(result.DetectedText);
        Assert.NotNull(result.SearchQuery);
        Assert.True(result.SearchQuery.Length >= 5);
    }

    // ═══════════════════════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Empty_vision_response_returns_empty_result()
    {
        VisionIdentifyResult result = await ParseVisionResponse("""{"responses": [{}]}""");

        Assert.Null(result.BestGuessLabel);
        Assert.Empty(result.WebEntities);
        Assert.Empty(result.Labels);
        Assert.Null(result.SearchQuery);
    }

    [Fact]
    public async Task Malformed_json_returns_empty_result()
    {
        // Invalid JSON — service should not throw
        VisionIdentifyResult result = await ParseVisionResponse("not json at all");

        Assert.Null(result.BestGuessLabel);
        Assert.Empty(result.WebEntities);
    }

    [Fact]
    public async Task Missing_api_key_returns_empty_result()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleCloudVision:ApiKey"] = "",
            })
            .Build();

        var service = new GoogleCloudVisionService(
            new HttpClient(new FakeHandler("{}")),
            config,
            NullLogger<GoogleCloudVisionService>.Instance);

        VisionIdentifyResult result = await service.IdentifyAsync("AAAA", CancellationToken.None);

        Assert.Null(result.BestGuessLabel);
        Assert.False(service.IsConfigured);
    }

    [Fact]
    public async Task Search_query_filters_generic_labels()
    {
        // Only generic labels — should still produce null or very limited query
        string json = """
        {
          "responses": [{
            "labelAnnotations": [
              {"description": "Product", "score": 0.95},
              {"description": "Rectangle", "score": 0.90},
              {"description": "Font", "score": 0.85},
              {"description": "Material", "score": 0.80}
            ]
          }]
        }
        """;

        VisionIdentifyResult result = await ParseVisionResponse(json);

        // All labels are in the generic filter list — searchQuery should be null
        Assert.Null(result.SearchQuery);
    }

    // ═══════════════════════════════════════════════════════════════
    // Realistic Cloud Vision API JSON fixtures
    // ═══════════════════════════════════════════════════════════════

    private const string KidsEaselVisionJson = """
    {
      "responses": [{
        "webDetection": {
          "bestGuessLabels": [{"label": "kids double sided art easel with accessories"}],
          "webEntities": [
            {"entityId": "/m/0h8n", "description": "Easel", "score": 1.456},
            {"entityId": "/m/0jbk", "description": "Art", "score": 0.892},
            {"entityId": "/m/01x3z", "description": "Chalkboard", "score": 0.765},
            {"entityId": "/m/06msq", "description": "Whiteboard", "score": 0.654},
            {"entityId": "/m/0138t", "description": "Toy", "score": 0.543}
          ],
          "pagesWithMatchingImages": [
            {"url": "https://www.amazon.com/dp/B0DEXAMPLE", "pageTitle": "Kids Art Easel for Toddlers, Double Sided Magnetic Drawing Board - Amazon.com"},
            {"url": "https://www.ebay.com/itm/234567890", "pageTitle": "Children Double Sided Painting Easel Board with Chalk Markers | eBay"},
            {"url": "https://www.walmart.com/ip/12345", "pageTitle": "Kids Easel Double Sided Art Board - Walmart.com"}
          ],
          "visuallySimilarImages": [
            {"url": "https://images-na.ssl-images-amazon.com/images/I/example.jpg"}
          ]
        },
        "labelAnnotations": [
          {"description": "Toy", "score": 0.96},
          {"description": "Art", "score": 0.93},
          {"description": "Easel", "score": 0.91},
          {"description": "Product", "score": 0.89},
          {"description": "Painting", "score": 0.84},
          {"description": "Chalkboard", "score": 0.78},
          {"description": "Office supplies", "score": 0.72}
        ],
        "textAnnotations": [
          {"description": "Painting board\nAa Bb Cc\n1234567890\nABCD\nCOLOR CHALK"}
        ],
        "logoAnnotations": []
      }]
    }
    """;

    private const string PlainChairVisionJson = """
    {
      "responses": [{
        "webDetection": {
          "webEntities": [
            {"entityId": "/m/01mzpv", "score": 0.42}
          ],
          "pagesWithMatchingImages": [],
          "visuallySimilarImages": [
            {"url": "https://images.example.com/similar-chair.jpg"}
          ]
        },
        "labelAnnotations": [
          {"description": "Chair", "score": 0.97},
          {"description": "Furniture", "score": 0.95},
          {"description": "Office chair", "score": 0.88},
          {"description": "Comfort", "score": 0.72},
          {"description": "Product", "score": 0.68}
        ]
      }]
    }
    """;

    private const string SonyHeadphonesVisionJson = """
    {
      "responses": [{
        "webDetection": {
          "bestGuessLabels": [{"label": "Sony WH-1000XM5 wireless headphones"}],
          "webEntities": [
            {"entityId": "/m/06nhd", "description": "Sony", "score": 2.104},
            {"entityId": "/m/03x8m", "description": "Headphones", "score": 1.876},
            {"entityId": "/m/0h8n", "description": "WH-1000XM5", "score": 1.654},
            {"entityId": "/m/01x3z", "description": "Noise cancelling", "score": 0.987},
            {"entityId": "/m/06msq", "description": "Audio equipment", "score": 0.765}
          ],
          "pagesWithMatchingImages": [
            {"url": "https://www.amazon.com/Sony-WH-1000XM5/dp/B09XS7JWHH", "pageTitle": "Sony WH-1000XM5 The Best Wireless Noise Canceling Headphones - Amazon"},
            {"url": "https://www.bestbuy.com/site/sony-wh-1000xm5/6505727.p", "pageTitle": "Sony - WH-1000XM5 Wireless Noise-Cancelling Over-the-Ear Headphones - Best Buy"},
            {"url": "https://www.ebay.com/sch/i.html?_nkw=sony+wh-1000xm5", "pageTitle": "Sony WH-1000XM5 for sale | eBay"}
          ],
          "visuallySimilarImages": [
            {"url": "https://sony.scene7.com/is/image/sonyglobalsolutions/wh-1000xm5_Primary_image"}
          ]
        },
        "labelAnnotations": [
          {"description": "Headphones", "score": 0.98},
          {"description": "Audio equipment", "score": 0.95},
          {"description": "Gadget", "score": 0.88},
          {"description": "Electronic device", "score": 0.85}
        ],
        "logoAnnotations": [
          {"description": "Sony", "score": 0.94}
        ],
        "textAnnotations": [
          {"description": "SONY\nWH-1000XM5"}
        ]
      }]
    }
    """;

    private const string VintageClothingVisionJson = """
    {
      "responses": [{
        "webDetection": {
          "webEntities": [
            {"entityId": "/m/01yrx", "description": "Dress", "score": 0.654},
            {"entityId": "/m/01x3z", "description": "Vintage", "score": 0.543}
          ],
          "pagesWithMatchingImages": [],
          "visuallySimilarImages": [
            {"url": "https://images.example.com/similar-dress.jpg"}
          ]
        },
        "labelAnnotations": [
          {"description": "Dress", "score": 0.94},
          {"description": "Clothing", "score": 0.92},
          {"description": "Textile", "score": 0.88},
          {"description": "Fashion", "score": 0.85},
          {"description": "Vintage", "score": 0.78}
        ]
      }]
    }
    """;

    private const string BoardGameVisionJson = """
    {
      "responses": [{
        "webDetection": {
          "bestGuessLabels": [{"label": "Settlers of Catan board game"}],
          "webEntities": [
            {"entityId": "/m/012345", "description": "Catan", "score": 2.345},
            {"entityId": "/m/023456", "description": "Board game", "score": 1.876},
            {"entityId": "/m/034567", "description": "Settlers of Catan", "score": 1.543}
          ],
          "pagesWithMatchingImages": [
            {"url": "https://www.amazon.com/Catan-Board-Game/dp/B00U26V4VQ", "pageTitle": "CATAN Board Game (Base Game) | Amazon"},
            {"url": "https://www.target.com/p/catan-board-game", "pageTitle": "Catan Board Game : Target"}
          ]
        },
        "labelAnnotations": [
          {"description": "Board game", "score": 0.97},
          {"description": "Games", "score": 0.94},
          {"description": "Tabletop game", "score": 0.88}
        ],
        "textAnnotations": [
          {"description": "CATAN\nTrade Build Settle\nKlaus Teuber\n3-4 Players\nAges 10+"}
        ]
      }]
    }
    """;
}
