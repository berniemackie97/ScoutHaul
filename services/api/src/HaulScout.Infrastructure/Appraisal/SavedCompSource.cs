using Microsoft.EntityFrameworkCore;
using HaulScout.Application.Appraisal;
using HaulScout.Domain;
using HaulScout.Infrastructure.Persistence;

namespace HaulScout.Infrastructure.Appraisal;

/// <summary>Comps people recorded against this product in the last year.</summary>
internal sealed class SavedCompSource(HaulScoutDbContext db, TimeProvider clock) : ICompSource
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(365);

    public string Name => "Saved comps";
    public bool IsConfigured => true;
    public TimeSpan Deadline => TimeSpan.FromSeconds(5);

    public async Task<CompSourceResult> FindAsync(CompQuery query, CancellationToken cancellationToken)
    {
        if (query.ProductIdentityId is not { } productIdentityId)
        {
            return CompSourceResult.Empty;
        }

        DateTimeOffset since = clock.GetUtcNow() - Window;
        var saved = await db.MarketObservations
            .AsNoTracking()
            .Where(observation => observation.OrganizationId == query.OrganizationId
                && observation.ProductIdentityId == productIdentityId
                && !observation.IsArchived
                && observation.ObservedAt >= since
                && !observation.Source.StartsWith("AI ")
                && (observation.ObservationType == MarketObservationType.Sold
                    || observation.ObservationType == MarketObservationType.ManualSold
                    || observation.ObservationType == MarketObservationType.ActiveListing
                    || observation.ObservationType == MarketObservationType.ManualActive))
            .OrderByDescending(observation => observation.SoldAt ?? observation.ObservedAt)
            .Take(50)
            .Select(observation => new
            {
                observation.ObservationType,
                observation.Source,
                observation.Title,
                observation.Price,
                observation.ShippingPrice,
                observation.Condition,
                observation.SourceUrl,
                observation.SoldAt,
                observation.ObservedAt,
            })
            .ToListAsync(cancellationToken);

        return new CompSourceResult(saved
            .Select(observation =>
            {
                bool sold = observation.ObservationType is MarketObservationType.Sold or MarketObservationType.ManualSold;
                return new Comp(
                    sold ? CompKind.Sold : CompKind.Asking,
                    observation.Source,
                    observation.Title,
                    observation.Price,
                    observation.ShippingPrice,
                    observation.Condition,
                    observation.SourceUrl,
                    sold ? observation.SoldAt ?? observation.ObservedAt : observation.ObservedAt,
                    Verified: true);
            })
            .ToArray());
    }
}
