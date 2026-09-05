namespace HaulScout.Domain;

/// <summary>
/// Size limits for the copy that goes on a listing, shared by the database schema and the write
/// path so the two cannot drift apart. A request over these limits is rejected with a 400 rather
/// than silently truncated — losing the tail of a description the operator wrote is worse than
/// making them shorten it.
/// </summary>
public static class ListingLimits
{
    /// <summary>
    /// Marketplaces cap titles far lower than this (eBay 80, Poshmark 50). We store what the
    /// operator or the generator wrote and let the channel truncate for its own rules.
    /// </summary>
    public const int TitleMaxLength = 300;

    public const int DescriptionMaxLength = 4000;
}
