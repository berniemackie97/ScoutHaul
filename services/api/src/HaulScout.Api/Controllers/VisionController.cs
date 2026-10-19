using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Application.Contracts;
using HaulScout.Application.Inventory;
using HaulScout.Application.Security;
using HaulScout.Application.Services;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/vision")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
public sealed class VisionController(
    IVisionIdentificationService visionService,
    IRemoteImageFetcher remoteImageFetcher) : HaulScoutControllerBase
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
            return Unavailable("Gemini API key (Gemini__ApiKey) is not set.", "vision_not_configured");
        }

        if (string.IsNullOrWhiteSpace(request.ImageBase64)
            && string.IsNullOrWhiteSpace(request.ImageUrl)
            && string.IsNullOrWhiteSpace(request.Context))
        {
            return Invalid("Provide imageBase64, imageUrl, or typed item details in context.", "input_required");
        }

        if (request.Context?.Length > 4_000)
        {
            return Invalid("Keep item details under 4,000 characters.", "context_too_long");
        }

        string? imageBase64 = string.IsNullOrWhiteSpace(request.ImageBase64) ? null : request.ImageBase64;

        // A URL instead of base64 means we fetch it, and the URL comes from the request — an SSRF
        // surface. IRemoteImageFetcher is the same guard the photo-import path already uses: http/https
        // only, every resolved address checked so a public hostname pointing at loopback or a cloud
        // metadata endpoint is refused, image content types only, and a byte cap held while streaming.
        // This used to be a bare GetByteArrayAsync, which had none of that.
        if (string.IsNullOrWhiteSpace(imageBase64) && !string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            try
            {
                PhotoUpload fetched = await remoteImageFetcher.FetchAsync(request.ImageUrl, cancellationToken);
                await using Stream content = fetched.Content;
                using MemoryStream buffer = new();
                await content.CopyToAsync(buffer, cancellationToken);
                imageBase64 = Convert.ToBase64String(buffer.ToArray());
            }
            catch (RemoteImageFetchException ex)
            {
                return Invalid(ex.Message, "image_download_failed");
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
            VisionIdentifyResult result = await visionService.IdentifyAsync(imageBase64, "image/jpeg", request.Context, cancellationToken);
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
            return Refused(status, "AI vision unavailable", ex.Message, ex.Code);
        }
    }
}
