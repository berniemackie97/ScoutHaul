using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HaulScout.Api.Authorization;
using HaulScout.Application.Appraisal;
using HaulScout.Application.Security;
using HaulScout.Application.Services;

namespace HaulScout.Api.Controllers;

[ApiController]
[Route("api/appraisals")]
[RequireOrganizationPermission(HaulScoutPermissions.ManageInventory)]
[EnableRateLimiting(RateLimitPolicy)]
public sealed class AppraisalsController(AppraisalService appraisals) : HaulScoutControllerBase
{
    // Each call can spend shared model quota, so a runaway client is held to a human pace.
    public const string RateLimitPolicy = "appraisal";

    /// <summary>What the item is, from a photo, a barcode, typed details, or a mix.</summary>
    [HttpPost("identify")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(AppraisalService.MaxPhotoBytes + 1_000_000)]
    public async Task<ActionResult<IdentificationDto>> Identify([FromForm] IdentifyForm form, CancellationToken cancellationToken)
    {
        AppraisalPhoto? photo = null;
        if (form.Photo is { Length: > 0 } file)
        {
            if (file.Length > AppraisalService.MaxPhotoBytes)
            {
                return Invalid($"Photos must be under {AppraisalService.MaxPhotoBytes / 1_000_000} MB.", "photo_too_large");
            }

            using MemoryStream buffer = new();
            await file.CopyToAsync(buffer, cancellationToken);
            byte[] bytes = buffer.ToArray();
            string? mediaType = PhotoSignature.MediaTypeOf(bytes);
            if (mediaType is null)
            {
                return Invalid("Only JPEG, PNG and WebP photos can be identified.", "photo_unsupported");
            }

            photo = new AppraisalPhoto(bytes, mediaType);
        }

        try
        {
            return ToActionResult(await appraisals.IdentifyAsync(new IdentifyInput(photo, form.Barcode, form.Details), cancellationToken));
        }
        catch (VisionUnavailableException exception)
        {
            int status = exception.Code switch
            {
                "quota_exhausted" or "rate_limited" => StatusCodes.Status429TooManyRequests,
                "upstream_unavailable" or "vision_not_configured" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status502BadGateway,
            };
            return Refused(status, "Identification unavailable", exception.Message, exception.Code);
        }
    }

    /// <summary>Comps from every source, a price, and how far to trust it.</summary>
    [HttpPost]
    public async Task<ActionResult<AppraisalDto>> Appraise(AppraiseRequest request, CancellationToken cancellationToken) =>
        ToActionResult(await appraisals.AppraiseAsync(request, cancellationToken));
}

public sealed class IdentifyForm
{
    public IFormFile? Photo { get; set; }
    public string? Barcode { get; set; }
    public string? Details { get; set; }
}
