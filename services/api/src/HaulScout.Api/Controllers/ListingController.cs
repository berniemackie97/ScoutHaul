using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;
using HaulScout.Application.Services;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/listing")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class ListingController(IListingGenerationService listingService) : HaulScoutControllerBase
{
    /// <summary>
    /// Generate a ready-to-post marketplace listing — an optimized title, an honest description, and a
    /// competitive suggested list price backed by real sold/active comps — from an item's details.
    /// </summary>
    [HttpPost("generate")]
    public async Task<ActionResult<GeneratedListingDto>> Generate(GenerateListingRequest request, CancellationToken cancellationToken)
    {
        if (!listingService.IsConfigured)
        {
            return StatusCode(503, new { title = "AI not configured", detail = "Gemini API key (Gemini__ApiKey) is not set." });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Invalid("Provide at least the item title/name to generate a listing.", "title_required");
        }

        GeneratedListingDto? result = await listingService.GenerateAsync(request, cancellationToken);
        return result is null
            ? StatusCode(502, new { title = "Generation failed", detail = "The AI listing generator did not return a result. Try again in a moment." })
            : Ok(result);
    }
}
