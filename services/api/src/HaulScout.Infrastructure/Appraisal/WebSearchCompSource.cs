using HaulScout.Application.Appraisal;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Infrastructure.Appraisal;

/// <summary>
/// Listings found by a grounded web search. The prices are read off pages by a model, so every comp
/// is marked unverified and carries the page it came from.
/// </summary>
internal sealed class WebSearchCompSource(IResaleEstimator estimator) : ICompSource
{
    public string Name => "Web search";
    public bool IsConfigured => estimator.IsConfigured;
    public TimeSpan Deadline => TimeSpan.FromSeconds(45);

    public async Task<CompSourceResult> FindAsync(CompQuery query, CancellationToken cancellationToken)
    {
        ResaleSearchOutcome outcome = await estimator.SearchAsync(
            Describe(query.Item),
            ResaleSearchHintBuilder.FromQuery(query.SearchText),
            cancellationToken);
        if (outcome.Failure is not null)
        {
            throw new CompSourceUnavailableException(outcome.Failure);
        }

        if (outcome.Estimate is not { } estimate)
        {
            return CompSourceResult.Empty;
        }

        Comp[] comps = (estimate.Comps ?? [])
            .Select(comp => new Comp(
                comp.Status == "active" ? CompKind.Asking : CompKind.Sold,
                SourceName(comp.SourceUrl),
                comp.Title ?? comp.Note ?? query.Item.Title,
                comp.Price,
                Shipping: null,
                comp.Condition,
                comp.SourceUrl,
                comp.Date,
                Verified: false))
            .ToArray();

        return new CompSourceResult(
            comps,
            new ModelEstimate(
                estimate.Low,
                estimate.High,
                estimate.Rationale,
                estimate.ValuationConfidence ?? "low",
                string.Equals(estimate.PriceRangeType, "wide", StringComparison.OrdinalIgnoreCase)),
            (estimate.Sources ?? []).Select(source => new WebSourceDto(source.Url, source.Title)).ToArray());
    }

    private static string Describe(ItemFacts item)
    {
        List<string> lines = [item.Title];
        void Add(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) lines.Add($"{label}: {value}");
        }

        Add("Brand", item.Brand);
        Add("Model", item.Model);
        Add("Category", item.Category);
        Add("Color", item.Color);
        Add("Condition", item.Condition);
        if (item.Identifiers.Count > 0)
        {
            lines.Add($"Identifiers: {string.Join(", ", item.Identifiers)}");
        }

        return string.Join('\n', lines);
    }

    private static string SourceName(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return "Web";
        }

        string host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return host switch
        {
            _ when host.EndsWith("ebay.com", StringComparison.OrdinalIgnoreCase) => "eBay",
            _ when host.EndsWith("poshmark.com", StringComparison.OrdinalIgnoreCase) => "Poshmark",
            _ when host.EndsWith("mercari.com", StringComparison.OrdinalIgnoreCase) => "Mercari",
            _ when host.EndsWith("depop.com", StringComparison.OrdinalIgnoreCase) => "Depop",
            _ when host.EndsWith("facebook.com", StringComparison.OrdinalIgnoreCase) => "Facebook",
            _ when host.EndsWith("stockx.com", StringComparison.OrdinalIgnoreCase) => "StockX",
            _ => host,
        };
    }
}
