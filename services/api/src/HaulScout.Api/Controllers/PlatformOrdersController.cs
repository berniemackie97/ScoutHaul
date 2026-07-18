using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Domain;

namespace HaulScout.Api.Controllers;

/// <summary>
/// HTTP adapter for marketplace order imports. Import parsing remains platform-specific; reconciliation is owned by
/// <see cref="PlatformOrderWorkflowService"/> so the controller does not carry persistence or domain mutation rules.
/// </summary>
[ApiController]
[Route("api/platform-orders")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
public sealed class PlatformOrdersController(
    PlatformOrderWorkflowService platformOrders,
    MarketplaceRefundImportService marketplaceRefundImportService,
    PlatformOrderImportWorkflowService platformOrderImports) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlatformOrderDto>>> List(
        [FromQuery] PlatformOrderStatus? status,
        CancellationToken cancellationToken) =>
        Ok(await platformOrders.ListAsync(status, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlatformOrderDto>> Get(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await platformOrders.GetAsync(id, cancellationToken));

    /// <summary>Idempotent batch upsert of imported orders. Re-ingesting a reconciled order refreshes the landing
    /// row for re-reconciliation while preserving line mappings.</summary>
    [HttpPost("ingest")]
    public async Task<ActionResult<IngestPlatformOrdersResultDto>> Ingest(IngestPlatformOrdersRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await platformOrderImports.IngestAsync(request, cancellationToken));

    /// <summary>Import eBay seller CSVs. The all-orders report creates/updates order rows; the listing-sales report
    /// enriches them with exact eBay fees, shipping labels, and net sales.</summary>
    [HttpPost("import/ebay-csv")]
    public async Task<ActionResult<IngestPlatformOrdersResultDto>> ImportEbayCsv(ImportPlatformOrdersCsvRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await platformOrderImports.ImportEbayCsvAsync(request, cancellationToken));

    /// <summary>Import refund/fee-credit evidence from marketplace CSVs onto already reconciled sales.</summary>
    [HttpPost("import/refunds-csv")]
    public async Task<ActionResult<ImportMarketplaceRefundsResultDto>> ImportMarketplaceRefundsCsv(
        ImportMarketplaceRefundsCsvRequest request,
        CancellationToken cancellationToken)
    {
        (ImportMarketplaceRefundsResultDto? result, string? error) = await marketplaceRefundImportService.ImportCsvAsync(request, cancellationToken);
        return error is not null ? BadRequest(error) : result!;
    }

    /// <summary>Turn an imported order into ScoutHaul sales using the platform's exact money figures.</summary>
    [HttpPost("{id:guid}/reconcile")]
    public async Task<ActionResult<PlatformOrderReconcileResultDto>> Reconcile(Guid id, ReconcilePlatformOrderRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await platformOrders.ReconcileAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/ignore")]
    public async Task<ActionResult<PlatformOrderDto>> Ignore(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await platformOrders.IgnoreAsync(id, cancellationToken));

    private ActionResult<T> ToActionResult<T>(AppResult<T> result) =>
        result.Status switch
        {
            AppResultStatus.Ok when result.Value is not null => result.Value,
            AppResultStatus.NotFound => result.Message is null ? NotFound() : NotFound(result.Message),
            AppResultStatus.Conflict => Conflict(result.Message),
            AppResultStatus.Validation => BadRequest(result.Message),
            _ => Problem("Unexpected platform-order workflow result.")
        };
}
