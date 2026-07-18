using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;

namespace HaulScout.Application.PlatformOrders;

/// <summary>
/// Application entry point for marketplace order imports. Controllers hand it request DTOs; it owns validation,
/// channel-account authorization, parser selection, and dispatch to the exact import use case.
/// </summary>
public sealed class PlatformOrderImportWorkflowService(
    IPlatformOrderImportStore store,
    ICurrentUserContext currentUser,
    PlatformOrderIngestionService ingestion,
    PlatformOrderFeeEnrichmentService feeEnrichment)
{
    public async Task<AppResult<IngestPlatformOrdersResultDto>> IngestAsync(
        IngestPlatformOrdersRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Orders is null || request.Orders.Count == 0)
        {
            return AppResult<IngestPlatformOrdersResultDto>.Validation("No orders were supplied.");
        }

        AppResult<Guid?> channelAccount = await ResolveChannelAccountAsync(request.ChannelAccountId, cancellationToken);
        if (channelAccount.Status != AppResultStatus.Ok)
        {
            return channelAccount.Status switch
            {
                AppResultStatus.NotFound => AppResult<IngestPlatformOrdersResultDto>.NotFound(channelAccount.Message),
                AppResultStatus.Conflict => AppResult<IngestPlatformOrdersResultDto>.Conflict(channelAccount.Message ?? "Channel account conflict."),
                AppResultStatus.Validation => AppResult<IngestPlatformOrdersResultDto>.Validation(channelAccount.Message ?? "Channel account is invalid."),
                _ => AppResult<IngestPlatformOrdersResultDto>.Validation("Channel account is invalid.")
            };
        }

        (IngestPlatformOrdersResultDto? result, string? error) = await ingestion.IngestAsync(
            request.Platform,
            request.Orders,
            channelAccount.Value,
            cancellationToken);
        return error is not null
            ? AppResult<IngestPlatformOrdersResultDto>.Validation(error)
            : AppResult<IngestPlatformOrdersResultDto>.Ok(result!);
    }

    public async Task<AppResult<IngestPlatformOrdersResultDto>> ImportEbayCsvAsync(
        ImportPlatformOrdersCsvRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Csv))
        {
            return AppResult<IngestPlatformOrdersResultDto>.Validation("Paste or upload the eBay report CSV.");
        }

        EbayReportParser.ParseResult parsed = EbayReportParser.ParseReport(request.Csv);
        if (parsed.Kind == EbayReportParser.EbayReportKind.AllOrders)
        {
            if (parsed.Orders.Count == 0)
            {
                return AppResult<IngestPlatformOrdersResultDto>.Validation(parsed.Warning ?? "No order rows were found in the report.");
            }

            (IngestPlatformOrdersResultDto? result, string? error) = await ingestion.IngestAsync(
                string.IsNullOrWhiteSpace(request.Platform) ? "eBay" : request.Platform,
                parsed.Orders,
                null,
                cancellationToken);
            return error is not null
                ? AppResult<IngestPlatformOrdersResultDto>.Validation(error)
                : AppResult<IngestPlatformOrdersResultDto>.Ok(result!);
        }

        if (parsed.Kind == EbayReportParser.EbayReportKind.ListingSales)
        {
            if (parsed.ListingSales.Count == 0)
            {
                return AppResult<IngestPlatformOrdersResultDto>.Validation(parsed.Warning ?? "No listing-sales rows were found in the report.");
            }

            return AppResult<IngestPlatformOrdersResultDto>.Ok(
                await feeEnrichment.EnrichEbayListingSalesAsync(parsed.ListingSales, cancellationToken));
        }

        return AppResult<IngestPlatformOrdersResultDto>.Validation(parsed.Warning ?? "Unsupported eBay CSV.");
    }

    private async Task<AppResult<Guid?>> ResolveChannelAccountAsync(Guid? channelAccountId, CancellationToken cancellationToken)
    {
        if (!channelAccountId.HasValue)
        {
            return AppResult<Guid?>.Ok(null);
        }

        bool exists = await store.ChannelAccountExistsAsync(currentUser.OrganizationId, channelAccountId.Value, cancellationToken);
        return exists
            ? AppResult<Guid?>.Ok(channelAccountId.Value)
            : AppResult<Guid?>.NotFound("Channel account was not found.");
    }
}
