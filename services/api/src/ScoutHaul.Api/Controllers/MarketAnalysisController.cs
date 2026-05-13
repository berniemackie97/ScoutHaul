using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Api.Services;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;
using ScoutHaul.Domain;
using ScoutHaul.Domain.Entities;
using ScoutHaul.Infrastructure.Persistence;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/market-analysis")]
[RequireOrganizationPermission(ScoutHaulPermissions.ViewReports)]
public sealed class MarketAnalysisController(
    ScoutHaulDbContext db,
    ICurrentUserContext currentUser,
    IConfidenceScorer confidenceScorer,
    IDealAnalyzer dealAnalyzer) : ControllerBase
{
    [HttpPost("calculate")]
    public async Task<ActionResult<MarketAnalysisResponse>> Calculate(MarketAnalysisRequest request, CancellationToken cancellationToken)
    {
        ProductIdentity? product = null;
        if (request.ProductIdentityId.HasValue)
        {
            product = await db.ProductIdentities.SingleOrDefaultAsync(
                productIdentity => productIdentity.Id == request.ProductIdentityId.Value && productIdentity.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

            if (product is null)
            {
                return NotFound("Product identity was not found.");
            }
        }

        InventoryItem? item = null;
        if (request.InventoryItemId.HasValue)
        {
            item = await db.InventoryItems.SingleOrDefaultAsync(
                inventoryItem => inventoryItem.Id == request.InventoryItemId.Value && inventoryItem.OrganizationId == currentUser.OrganizationId,
                cancellationToken);

            if (item is null)
            {
                return NotFound("Inventory item was not found.");
            }

            if (product is not null && item.ProductIdentityId.HasValue && item.ProductIdentityId != product.Id)
            {
                return BadRequest("Inventory item does not belong to the supplied product identity.");
            }
        }

        var observations = request.ProductIdentityId.HasValue || request.InventoryItemId.HasValue
            ? await LoadRelevantObservations(request, item, cancellationToken)
            : new List<MarketObservation>();
        MarketValuationResult valuation = MarketValuation.Estimate(
            request.ExpectedSalePrice,
            item?.EstimatedSalePrice,
            request.PricingBasis,
            observations);
        var confidence = confidenceScorer.Score(observations, valuation.PricingBasis, request.Condition ?? item?.Condition, request.RiskFlags);

        var analysisInput = new DealAnalysisInput(
            valuation.EstimatedSalePrice,
            request.SellerAskingPrice ?? item?.SellerAskingPrice,
            request.PurchasePrice ?? item?.PurchasePrice,
            request.Platform,
            request.PlatformFeePercent,
            request.PaymentFeePercent,
            request.FixedPaymentFee,
            request.EstimatedShippingCostPaidBySeller,
            request.SuppliesCost,
            request.RiskReserve,
            request.DesiredMinimumProfit,
            request.TargetMarginPercent,
            confidence.Score,
            valuation.PricingBasis);

        var recommendation = dealAnalyzer.Analyze(analysisInput);
        var combinedWarnings = confidence.Warnings.Concat(recommendation.Warnings).Distinct().ToList();

        if (valuation.ActiveListingCount > 0 && valuation.SoldCompCount == 0 && request.ExpectedSalePrice is null && item?.EstimatedSalePrice is null)
        {
            combinedWarnings.Add("Only active listings were found. Active listing prices are not sold prices and were not used for valuation.");
        }

        if (valuation.PricingBasis == PricingBasisType.CachedRecent && valuation.SoldCompCount == 0)
        {
            combinedWarnings.Add("No sold comps were found. Provider estimates were used for valuation.");
        }

        var analysis = new MarketAnalysis
        {
            OrganizationId = currentUser.OrganizationId,
            ProductIdentityId = request.ProductIdentityId ?? item?.ProductIdentityId,
            InventoryItemId = request.InventoryItemId,
            EstimatedSalePrice = valuation.EstimatedSalePrice,
            PricingBasis = valuation.PricingBasis,
            ConfidenceScore = confidence.Score,
            SoldCompCount = valuation.SoldCompCount,
            ActiveListingCount = valuation.ActiveListingCount,
            ReasonsJson = JsonSerializer.Serialize(confidence.Reasons),
            WarningsJson = JsonSerializer.Serialize(combinedWarnings)
        };

        db.MarketAnalyses.Add(analysis);
        await db.SaveChangesAsync(cancellationToken);

        var buyRecommendation = new BuyRecommendation
        {
            OrganizationId = currentUser.OrganizationId,
            MarketAnalysisId = analysis.Id,
            ProductIdentityId = analysis.ProductIdentityId,
            InventoryItemId = analysis.InventoryItemId,
            PricingBasis = valuation.PricingBasis,
            Decision = recommendation.Decision,
            RecommendedMaxBuyPrice = recommendation.RecommendedMaxBuyPrice,
            ExpectedNetProfit = recommendation.ExpectedNetProfit,
            RoiPercent = recommendation.RoiPercent,
            MarginPercent = recommendation.MarginPercent,
            BreakEvenPrice = recommendation.BreakEvenPrice,
            WarningsJson = JsonSerializer.Serialize(recommendation.Warnings)
        };

        db.BuyRecommendations.Add(buyRecommendation);
        await db.SaveChangesAsync(cancellationToken);

        return new MarketAnalysisResponse(
            analysis.Id,
            buyRecommendation.Id,
            analysis.ProductIdentityId,
            analysis.InventoryItemId,
            analysis.EstimatedSalePrice,
            analysis.PricingBasis,
            analysis.SoldCompCount,
            analysis.ActiveListingCount,
            new ConfidenceScoreResult(confidence.Score, confidence.Reasons, combinedWarnings),
            recommendation with { Warnings = combinedWarnings });
    }

    [HttpPost("recommendation")]
    public Task<ActionResult<MarketAnalysisResponse>> CalculateRecommendation(MarketAnalysisRequest request, CancellationToken cancellationToken) =>
        Calculate(request, cancellationToken);

    private async Task<List<MarketObservation>> LoadRelevantObservations(MarketAnalysisRequest request, InventoryItem? item, CancellationToken cancellationToken)
    {
        var query = db.MarketObservations.Where(observation =>
            observation.OrganizationId == currentUser.OrganizationId && !observation.IsArchived);

        var productIdentityId = request.ProductIdentityId ?? item?.ProductIdentityId;
        if (request.InventoryItemId.HasValue && productIdentityId.HasValue)
        {
            query = query.Where(observation =>
                observation.InventoryItemId == request.InventoryItemId.Value
                || observation.ProductIdentityId == productIdentityId.Value);
        }
        else if (request.InventoryItemId.HasValue)
        {
            query = query.Where(observation => observation.InventoryItemId == request.InventoryItemId.Value);
        }
        else if (productIdentityId.HasValue)
        {
            query = query.Where(observation => observation.ProductIdentityId == productIdentityId.Value);
        }

        return await query.OrderByDescending(observation => observation.ObservedAt).ToListAsync(cancellationToken);
    }

}
