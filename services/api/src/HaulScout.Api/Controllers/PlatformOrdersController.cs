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
    PlatformOrderImportWorkflowService platformOrderImports,
    PlatformOrderAutoMatchService autoMatch) : HaulScoutControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PlatformOrderDto>>> List(
        [FromQuery] PlatformOrderStatus? status,
        CancellationToken cancellationToken) =>
        Ok(await platformOrders.ListAsync(status, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlatformOrderDto>> Get(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await platformOrders.GetAsync(id, cancellationToken));

    /// <summary>What settling the waiting backlog would do — nothing is written.</summary>
    [HttpGet("auto-match/preview")]
    public async Task<ActionResult<AutoMatchPreviewDto>> PreviewAutoMatch(CancellationToken cancellationToken) =>
        await autoMatch.PreviewBacklogAsync(cancellationToken);

    /// <summary>
    /// Settles every waiting order that leaves nothing to decide. The plan is rebuilt here rather than
    /// taken from the caller, so a preview the operator has been looking at for a while cannot write
    /// something that is no longer true.
    /// </summary>
    [HttpPost("auto-match")]
    public async Task<ActionResult<AutoMatchAppliedDto>> ApplyAutoMatch(CancellationToken cancellationToken) =>
        new AutoMatchAppliedDto(await autoMatch.ApplyBacklogAsync(cancellationToken));

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

    /// <summary>
    /// Removes an imported order outright — a test order, the wrong file, a row that should never
    /// have been brought in. Only while it is unreconciled: once it has been matched, sales exist
    /// downstream, and deleting the import row would strand them. Ignoring keeps the record and its
    /// history; deleting is for rows that should not exist at all.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<PlatformOrderDeletedDto>> Delete(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await platformOrders.DeleteImportedAsync(id, cancellationToken));

    [HttpPost("{id:guid}/ignore")]
    public async Task<ActionResult<PlatformOrderDto>> Ignore(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await platformOrders.IgnoreAsync(id, cancellationToken));

}
