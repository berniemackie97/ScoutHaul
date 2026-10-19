namespace HaulScout.Application.Appraisal;

public sealed record AppraisalSnapshotDto(
    Guid Id,
    DateTimeOffset AppraisedAt,
    decimal? List,
    PriceBasis Basis,
    int Confidence,
    ConfidenceLevel Level,
    int SaleComps,
    int AskingComps);

/// <summary>What an item was judged to be worth, each time it was appraised.</summary>
public interface IAppraisalHistoryStore
{
    Task<bool> ItemExistsAsync(Guid organizationId, Guid inventoryItemId, CancellationToken cancellationToken);

    Task RecordAsync(
        Guid organizationId,
        Guid inventoryItemId,
        Guid? productIdentityId,
        PriceResult price,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AppraisalSnapshotDto>> ListForItemAsync(
        Guid organizationId,
        Guid inventoryItemId,
        int take,
        CancellationToken cancellationToken);
}
