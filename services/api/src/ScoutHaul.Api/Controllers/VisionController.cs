using Microsoft.AspNetCore.Mvc;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/vision")]
[RequireOrganizationPermission(ScoutHaulPermissions.ManageInventory)]
public sealed class VisionController(
    IVisionIdentificationService visionService) : ControllerBase
{
    /// <summary>
    /// Identify a product from a photo using Google Cloud Vision Web Detection + Label Detection.
    /// Returns best guess product name, web entities, matching product pages, and object labels.
    /// </summary>
    [HttpPost("identify")]
    [RequestSizeLimit(12_000_000)] // 12MB max for base64 image
    public async Task<ActionResult<VisionIdentifyResult>> Identify(
        [FromBody] VisionIdentifyRequest request,
        CancellationToken cancellationToken)
    {
        if (!visionService.IsConfigured)
        {
            return StatusCode(503, new { title = "Vision service not configured", detail = "Google Cloud Vision API key is not set." });
        }

        if (string.IsNullOrWhiteSpace(request.ImageBase64) && string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            return BadRequest(new { title = "Image required", detail = "Provide imageBase64 or imageUrl." });
        }

        string imageBase64 = request.ImageBase64 ?? "";

        // If URL provided instead of base64, download and convert
        if (string.IsNullOrWhiteSpace(imageBase64) && !string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            try
            {
                using HttpClient client = new();
                client.Timeout = TimeSpan.FromSeconds(10);
                byte[] imageBytes = await client.GetByteArrayAsync(request.ImageUrl, cancellationToken);
                imageBase64 = Convert.ToBase64String(imageBytes);
            }
            catch
            {
                return BadRequest(new { title = "Image download failed", detail = "Could not download image from the provided URL." });
            }
        }

        // Strip data URI prefix if present (e.g., "data:image/jpeg;base64,...")
        if (imageBase64.Contains(','))
        {
            imageBase64 = imageBase64[(imageBase64.IndexOf(',') + 1)..];
        }

        VisionIdentifyResult result = await visionService.IdentifyAsync(imageBase64, cancellationToken);
        return Ok(result);
    }
}
