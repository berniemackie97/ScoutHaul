using Microsoft.AspNetCore.Mvc;
using ScoutHaul.Api.Authorization;
using ScoutHaul.Application.Contracts;
using ScoutHaul.Application.Services;

namespace ScoutHaul.Api.Controllers;

[ApiController]
[Route("api/vision")]
[RequireOrganizationPermission(ScoutHaulPermissions.ManageInventory)]
public sealed class VisionController(
    IVisionIdentificationService visionService,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    /// <summary>
    /// Identify a product from a photo using Gemini multimodal. Returns a specific product name,
    /// brand, model, category, condition, a conservative used-resale valuation, and comp price points.
    /// </summary>
    [HttpPost("identify")]
    [RequestSizeLimit(12_000_000)] // 12MB max for base64 image
    public async Task<ActionResult<VisionIdentifyResult>> Identify(
        [FromBody] VisionIdentifyRequest request,
        CancellationToken cancellationToken)
    {
        if (!visionService.IsConfigured)
        {
            return StatusCode(503, new { title = "Vision service not configured", detail = "Gemini API key (Gemini__ApiKey) is not set." });
        }

        if (string.IsNullOrWhiteSpace(request.ImageBase64) && string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            return BadRequest(new { title = "Image required", detail = "Provide imageBase64 or imageUrl." });
        }

        string imageBase64 = request.ImageBase64 ?? "";

        // If URL provided instead of base64, download and convert via IHttpClientFactory
        if (string.IsNullOrWhiteSpace(imageBase64) && !string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            try
            {
                using HttpClient client = httpClientFactory.CreateClient();
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
        int commaIndex = imageBase64.IndexOf(',');
        if (commaIndex >= 0)
        {
            imageBase64 = imageBase64[(commaIndex + 1)..];
        }

        VisionIdentifyResult result = await visionService.IdentifyAsync(imageBase64, cancellationToken);
        return Ok(result);
    }
}
