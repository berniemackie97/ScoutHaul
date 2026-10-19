using System.Text;
using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Tax;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/sales-tax")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageFinance)]
public sealed class SalesTaxController(SalesTaxWorkflowService salesTax) : HaulScoutControllerBase
{
    [HttpGet("profile")]
    public Task<SalesTaxProfileDto> Profile(CancellationToken cancellationToken) => salesTax.GetProfileAsync(cancellationToken);

    [HttpPut("profile")]
    public async Task<ActionResult<SalesTaxProfileDto>> SaveProfile(SaveSalesTaxProfileRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.SaveProfileAsync(request, cancellationToken));

    [HttpGet("overview")]
    public Task<SalesTaxOverviewDto> Overview(CancellationToken cancellationToken) => salesTax.GetOverviewAsync(cancellationToken);

    [HttpGet("periods/{key}")]
    public async Task<ActionResult<SalesTaxWorksheetDto>> Worksheet(string key, [FromQuery] DateOnly? filedOn, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.GetWorksheetAsync(key, filedOn, cancellationToken));

    [HttpGet("periods/{key}/schedule.csv")]
    public async Task<IActionResult> ScheduleCsv(string key, CancellationToken cancellationToken)
    {
        AppResult<SalesTaxWorksheetDto> result = await salesTax.GetWorksheetAsync(key, null, cancellationToken);
        if (result.Status != AppResultStatus.Ok || result.Value is null) return Missing(result.Message ?? "No such reporting period.");

        byte[] csv = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(SalesTaxCsv.Build(result.Value))).ToArray();
        return File(csv, "text/csv; charset=utf-8", $"fl-dr15-{result.Value.Period.Start:yyyy-MM-dd}-to-{result.Value.Period.End:yyyy-MM-dd}.csv");
    }

    [HttpPost("periods/{key}/filings")]
    public async Task<ActionResult<SalesTaxWorksheetDto>> File(string key, FileSalesTaxReturnRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.FileAsync(key, request, cancellationToken));

    [HttpPost("filings/{id:guid}/void")]
    public async Task<ActionResult> Void(Guid id, VoidSalesTaxReturnRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.VoidAsync(id, request, cancellationToken));

    [HttpPost("use-entries")]
    public async Task<ActionResult<SalesTaxUseEntryDto>> AddUseEntry(SaveSalesTaxUseEntryRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.SaveUseEntryAsync(null, request, cancellationToken));

    [HttpPut("use-entries/{id:guid}")]
    public async Task<ActionResult<SalesTaxUseEntryDto>> UpdateUseEntry(Guid id, SaveSalesTaxUseEntryRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.SaveUseEntryAsync(id, request, cancellationToken));

    [HttpDelete("use-entries/{id:guid}")]
    public async Task<ActionResult> DeleteUseEntry(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.DeleteUseEntryAsync(id, cancellationToken));

    [HttpPost("adjustments")]
    public async Task<ActionResult<SalesTaxAdjustmentDto>> AddAdjustment(SaveSalesTaxAdjustmentRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.AddAdjustmentAsync(request, cancellationToken));

    [HttpDelete("adjustments/{id:guid}")]
    public async Task<ActionResult> DeleteAdjustment(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.DeleteAdjustmentAsync(id, cancellationToken));

    [HttpPut("sales/{saleId:guid}/decision")]
    public async Task<ActionResult> SaveDecision(Guid saleId, SaveSalesTaxSaleDecisionRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.SaveSaleDecisionAsync(saleId, request, cancellationToken));

    [HttpDelete("sales/{saleId:guid}/decision")]
    public async Task<ActionResult> DeleteDecision(Guid saleId, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.DeleteSaleDecisionAsync(saleId, cancellationToken));

    [HttpPut("sales/{saleId:guid}/collector")]
    public async Task<ActionResult> SetCollector(Guid saleId, SetSaleTaxCollectorRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await salesTax.SetSaleCollectorAsync(saleId, request, cancellationToken));
}
