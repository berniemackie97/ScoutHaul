using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Services;

/// <summary>
/// What to actually check at each kind of stop — the sourcing playbook, encoded. Pure knowledge with no persistence,
/// so it lives with the other catalogs rather than in the web layer. Applying a template only adds what is missing,
/// which makes re-applying it safe after a stop's source type changes.
/// </summary>
public static class SourcingStopChecklistCatalog
{
    public static void ApplyTemplate(SourcingTripStop stop)
    {
        string[] template = GetTemplate(stop.SourceType);
        HashSet<string> existing = stop.ChecklistItems.Select(item => item.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int nextOrder = stop.ChecklistItems.Count == 0 ? 1 : stop.ChecklistItems.Max(item => item.SortOrder) + 1;

        foreach (string text in template.Where(item => !existing.Contains(item)))
        {
            stop.ChecklistItems.Add(new SourcingStopChecklistItem
            {
                OrganizationId = stop.OrganizationId,
                Text = text,
                SortOrder = nextOrder++,
                IsRequired = true
            });
        }
    }

    public static string[] GetTemplate(SourcingStopSourceType sourceType) =>
        sourceType switch
        {
            SourcingStopSourceType.GarageSale => [
                "Ask if they have video games, electronics, tools, or baby items not outside",
                "Check shoes",
                "Check toys and LEGO",
                "Check baby monitors and baby gear",
                "Check small appliances",
                "Check tools",
                "Scan barcodes on boxed items",
                "Bundle-negotiate before leaving",
                "Add purchases before leaving"
            ],
            SourcingStopSourceType.EstateSale => [
                "Check garage/tools first",
                "Check electronics",
                "Check kitchen brands and small appliances",
                "Check baby/kids items",
                "Check books/media",
                "Look for boxed items/manuals",
                "Photograph model numbers",
                "Record high-value items passed on",
                "Consider return on discount day"
            ],
            SourcingStopSourceType.ThriftStore or SourcingStopSourceType.Goodwill => [
                "Check shoes",
                "Check electronics case",
                "Check small appliances",
                "Check toys",
                "Check bags",
                "Check sporting goods",
                "Check books/media",
                "Check baby/kids section",
                "Scan promising barcodes",
                "Record spend before checkout"
            ],
            SourcingStopSourceType.FacebookPickup => [
                "Confirm seller/item",
                "Inspect item condition",
                "Test power/function if applicable",
                "Confirm all accessories included",
                "Compare to listing photos",
                "Negotiate if condition differs",
                "Record purchase price",
                "Mark pickup complete"
            ],
            SourcingStopSourceType.Liquidation or SourcingStopSourceType.Auction => [
                "Check manifest if available",
                "Inspect box/item condition",
                "Estimate sellable percentage",
                "Check for missing parts",
                "Avoid restricted/unsafe categories",
                "Record lot cost and item count",
                "Photograph lot before sorting"
            ],
            _ => [
                "Confirm location and seller details",
                "Inspect condition before buying",
                "Check comps before committing",
                "Record purchase price",
                "Add purchases before leaving"
            ]
        };
}
