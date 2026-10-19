namespace HaulScout.Application.Appraisal;

/// <summary>Searches a person can open to check a price by eye, sold results first.</summary>
public static class CompSearchLinks
{
    public static IReadOnlyList<CompSearchLinkDto> For(string query)
    {
        string q = Uri.EscapeDataString(query.Trim());
        return
        [
            new("eBay sold", CompKind.Sold, $"https://www.ebay.com/sch/i.html?_nkw={q}&LH_Sold=1&LH_Complete=1&_sop=13"),
            new("eBay listings", CompKind.Asking, $"https://www.ebay.com/sch/i.html?_nkw={q}&_sop=15"),
            new("Poshmark", CompKind.Asking, $"https://poshmark.com/search?query={q}"),
            new("Mercari", CompKind.Asking, $"https://www.mercari.com/search/?keyword={q}"),
            new("Depop", CompKind.Asking, $"https://www.depop.com/search/?q={q}"),
            new("Facebook Marketplace", CompKind.Asking, $"https://www.facebook.com/marketplace/search/?query={q}"),
        ];
    }
}
