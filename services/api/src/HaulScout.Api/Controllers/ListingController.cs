using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Appraisal;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Security;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/listing")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class ListingController(ListingGenerationService listings) : HaulScoutControllerBase
{
    /// <summary>Title and description for an item, priced by its appraisal.</summary>
    [HttpPost("generate")]
    public async Task<ActionResult<GeneratedListingDto>> Generate(GenerateListingRequest request, CancellationToken cancellationToken)
    {
        if (!listings.IsConfigured)
        {
            return Unavailable("Gemini API key (Gemini__ApiKey) is not set.", "writer_not_configured");
        }

        AppResult<GeneratedListingDto> result = await listings.GenerateAsync(request, cancellationToken);
        return result.Code == "generation_failed"
            ? Refused(StatusCodes.Status502BadGateway, "Generation failed", result.Message!, result.Code)
            : ToActionResult(result);
    }
}
