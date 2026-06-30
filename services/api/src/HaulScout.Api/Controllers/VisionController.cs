using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Services;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/vision")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class VisionController(
    IVisionIdentificationService visionService,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    /// <summary>
    /// Identify a product from a photo, typed facts, or both. Returns a specific product name,
    /// brand, model, category, condition, a conservative used-resale valuation, and comp price points.
    /// </summary>
    [HttpPost("identify")]
    // 28MB cap (under Kestrel's 30MB default). The client now downscales to ~300KB before upload,
    // but full-res photos from older app builds base64-encode to 10-25MB on high-MP phones — at the
    // old 12MB limit those silently 413'd and surfaced as "not identified" only for detailed/large
    // images, which looked like random inconsistency. Generous headroom removes size as a failure mode.
    [RequestSizeLimit(28_000_000)]
    public async Task<ActionResult<VisionIdentifyResult>> Identify(
        [FromBody] VisionIdentifyRequest request,
        CancellationToken cancellationToken)
    {
        if (!visionService.IsConfigured)
        {
            return StatusCode(503, new { title = "Vision service not configured", detail = "Gemini API key (Gemini__ApiKey) is not set." });
        }

        if (string.IsNullOrWhiteSpace(request.ImageBase64)
            && string.IsNullOrWhiteSpace(request.ImageUrl)
            && string.IsNullOrWhiteSpace(request.Context))
        {
            return BadRequest(new { title = "Input required", detail = "Provide imageBase64, imageUrl, or typed item details in context." });
        }

        if (request.Context?.Length > 4_000)
        {
            return BadRequest(new { title = "Typed details too long", detail = "Keep item details under 4,000 characters." });
        }

        string? imageBase64 = string.IsNullOrWhiteSpace(request.ImageBase64) ? null : request.ImageBase64;

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
        if (!string.IsNullOrWhiteSpace(imageBase64))
        {
            int commaIndex = imageBase64.IndexOf(',');
            if (commaIndex >= 0)
            {
                imageBase64 = imageBase64[(commaIndex + 1)..];
            }
        }

        try
        {
            VisionIdentifyResult result = await visionService.IdentifyAsync(imageBase64, request.Context, cancellationToken);
            return Ok(result);
        }
        catch (VisionUnavailableException ex)
        {
            // Surface the real cause so the client can show "daily limit reached", "busy", etc. —
            // not a misleading "no results found". `code` is the stable machine key.
            int status = ex.Code switch
            {
                "quota_exhausted" or "rate_limited" => StatusCodes.Status429TooManyRequests,
                "upstream_unavailable" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status502BadGateway,
            };
            return StatusCode(status, new { code = ex.Code, title = "AI vision unavailable", detail = ex.Message });
        }
    }
}
