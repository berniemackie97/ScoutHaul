namespace HaulScout.Application.Sync;

/// <summary>The kinds of sync issue, each with the title and fix a person sees when the agent sends none.</summary>
public static class SyncIssueCodes
{
    public const string Rejected = "REJECTED";
    public const string FieldMissing = "FIELD_MISSING";
    public const string PhotoFetch = "PHOTO_FETCH";
    public const string SiteChanged = "SITE_CHANGED";
    public const string Network = "NETWORK";
    public const string Challenge = "CHALLENGE";
    public const string CancelledAfterSale = "CANCELLED_AFTER_SALE";
    public const string Other = "OTHER";

    public const int DetailLimit = 4000;
    public const int SnapshotLimit = 16000;

    public static (string Title, string? Fix) Describe(string? code) => (code ?? Other) switch
    {
        Rejected => ("Record refused", "Open the record, correct what the message names, then retry."),
        FieldMissing => ("Detail missing from a record", "Retry reads the record's own page, which usually carries it."),
        PhotoFetch => ("Photo not saved", "Retry later; the photo link usually still works."),
        SiteChanged => ("Page could not be read", "The marketplace changed its pages. Update the extension; the raw page is kept."),
        Network => ("Could not reach the marketplace", "Retry; it backs off on its own."),
        Challenge => ("Security check", "Open the marketplace in this browser, complete the check, then resume."),
        CancelledAfterSale => ("Cancelled after it was recorded", "Correct the sale in HaulScout; sales are never undone automatically."),
        _ => ("Sync issue", null),
    };
}
