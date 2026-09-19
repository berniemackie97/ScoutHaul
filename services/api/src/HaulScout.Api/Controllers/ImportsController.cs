using Microsoft.AspNetCore.Mvc;
using HaulScout.Api.Authorization;
using HaulScout.Api.Services;
using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Sync;

namespace HaulScout.Api.Controllers;

/// <summary>
/// The one place marketplace exports come in, and the history of every import.
///
/// Gated on membership rather than one permission: a single drop can hold an orders export and a
/// listings export, which need different permissions. <see cref="ReportImportService"/> checks each
/// file against its own kind and refuses only the ones the caller cannot import.
/// </summary>
[ApiController]
[Route("api/imports")]
[RequireOrganizationMember]
public sealed class ImportsController(ReportImportService imports) : HaulScoutControllerBase
{
    /// <summary>Room for the maximum number of files at the maximum size, plus multipart overhead.</summary>
    private const long MaxRequestBytes = (long)ReportImportService.MaxFiles * ReportImportService.MaxFileBytes + 1024 * 1024;

    /// <summary>What each dropped file is and what it would do — nothing is written.</summary>
    [HttpPost("detect")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<IReadOnlyList<ReportDetectionDto>>> Detect(
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        if (Refusal(files) is ObjectResult refused)
        {
            return refused;
        }

        return Ok(await imports.DetectAsync(await ReadAsync(files, cancellationToken), cancellationToken));
    }

    /// <summary>Imports each file independently and records each as a sync run.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ActionResult<ImportResultDto>> Import(
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        if (Refusal(files) is ObjectResult refused)
        {
            return refused;
        }

        return await imports.ImportAsync(await ReadAsync(files, cancellationToken), cancellationToken);
    }

    /// <summary>What imports have left for a person to match.</summary>
    [HttpGet("queues")]
    public async Task<ActionResult<ImportQueuesDto>> Queues(CancellationToken cancellationToken) =>
        await imports.QueuesAsync(cancellationToken);

    /// <summary>Every import and scan, newest first; scans that changed nothing only when <paramref name="quiet"/> is set.</summary>
    [HttpGet("runs")]
    public async Task<ActionResult<SyncRunListDto>> Runs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] bool quiet = false,
        CancellationToken cancellationToken = default) =>
        await imports.ListAsync(page, pageSize, quiet, cancellationToken);

    [HttpGet("runs/{id:guid}")]
    public async Task<ActionResult<SyncRunDetailDto>> Run(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await imports.GetAsync(id, cancellationToken));

    /// <summary>Reads a past import's exact file again.</summary>
    [HttpPost("runs/{id:guid}/rerun")]
    public async Task<ActionResult<SyncRunDto>> Rerun(Guid id, CancellationToken cancellationToken) =>
        ToActionResult(await imports.RerunAsync(id, cancellationToken));

    /// <summary>The exact file an import read.</summary>
    [HttpGet("runs/{id:guid}/file")]
    public async Task<IActionResult> File(Guid id, CancellationToken cancellationToken)
    {
        AppResult<ReportFile> result = await imports.DownloadAsync(id, cancellationToken);
        if (result.Status != AppResultStatus.Ok || result.Value is null)
        {
            return Missing(result.Message ?? "The file for that import is not kept.");
        }

        string contentType = result.Value.LooksLikeWorkbook
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "text/csv; charset=utf-8";
        return File(result.Value.Content, contentType, result.Value.FileName);
    }

    private ObjectResult? Refusal(List<IFormFile> files) =>
        files.Count == 0 ? Invalid("Choose at least one export to import.")
        : files.Count > ReportImportService.MaxFiles ? Invalid($"Import at most {ReportImportService.MaxFiles} files at a time.")
        : null;

    /// <summary>
    /// Reads each file, but never more of one than the size limit plus a byte: enough for the importer
    /// to refuse an oversized file by name without the server holding all of it.
    /// </summary>
    private static async Task<IReadOnlyList<ReportFile>> ReadAsync(List<IFormFile> files, CancellationToken cancellationToken)
    {
        List<ReportFile> read = [];
        foreach (IFormFile file in files)
        {
            int cap = (int)Math.Min(file.Length, ReportImportService.MaxFileBytes + 1L);
            byte[] buffer = new byte[cap];
            await using Stream stream = file.OpenReadStream();
            await stream.ReadExactlyAsync(buffer, cancellationToken);
            read.Add(new ReportFile(Path.GetFileName(file.FileName), buffer));
        }

        return read;
    }
}
