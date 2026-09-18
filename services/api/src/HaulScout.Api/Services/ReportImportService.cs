using HaulScout.Application.Common;
using HaulScout.Application.Contracts;
using HaulScout.Application.Imports;
using HaulScout.Application.Organizations;
using HaulScout.Application.PlatformOrders;
using HaulScout.Application.Security;
using HaulScout.Application.Sync;
using HaulScout.Domain;
using HaulScout.Domain.Entities;

namespace HaulScout.Api.Services;

/// <summary>
/// Marketplace exports in, sync runs out: one drop zone for every supported file.
///
/// The importer works out what each file is from its contents, says what it found before anything
/// changes, then imports each file independently — one bad file never stops the others — and
/// records every attempt as a <see cref="SyncRun"/> with its counts, its problem rows and the exact
/// file, so an import can be looked back on, understood, and re-run after a fix.
///
/// It lives in the API layer because listing ingest still does; it adds no persistence of its own
/// beyond the run store.
/// </summary>
public sealed class ReportImportService(
    ISyncRunStore runs,
    ICurrentUserContext currentUser,
    IOrganizationAuthorizer access,
    IOrganizationStore organizations,
    PlatformOrderIngestionService orderIngestion,
    PlatformOrderFeeEnrichmentService feeEnrichment,
    MarketplaceRefundImportService refunds,
    PlatformListingImportService listings,
    TimeProvider clock,
    ILogger<ReportImportService> logger)
{
    /// <summary>Marketplace exports are kilobytes to a few megabytes; anything larger is not one.</summary>
    public const int MaxFileBytes = 10 * 1024 * 1024;

    public const int MaxFiles = 10;

    public async Task<IReadOnlyList<ReportDetectionDto>> DetectAsync(IReadOnlyList<ReportFile> files, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        TimeZoneInfo zone = await ZoneAsync(organizationId, cancellationToken);
        List<ReportDetectionDto> detections = [];

        foreach (ReportFile file in files)
        {
            if (Refusal(file) is string tooBig)
            {
                detections.Add(Unimportable(file, null, tooBig));
                continue;
            }

            ReportFormat? format = ReportFormats.Detect(file);
            if (format is null)
            {
                detections.Add(Unimportable(file, null,
                    "Not a marketplace export this system can read. Supported: eBay All Orders, Listings Sales, Active Listings and Listing Quality; Depop sales; Poshmark inventory."));
                continue;
            }

            if (!await access.HasPermission(organizationId, format.RequiredPermission, cancellationToken))
            {
                detections.Add(Unimportable(file, format, $"Your role cannot import {Describe(format.Area)}."));
                continue;
            }

            ReadReport report = ReportReader.Read(format, file, zone);
            detections.Add(new ReportDetectionDto(
                file.FileName,
                file.Content.LongLength,
                format.Key,
                format.Platform,
                format.Label,
                report.Records,
                report.CoversFrom,
                report.CoversTo,
                format.CompleteSnapshot,
                CanImport: report.Records > 0,
                Problem: report.Records > 0 ? null : "The file has no rows that can be imported.",
                report.Issues.Select(ToDto).ToList(),
                await runs.LastImportedAtAsync(organizationId, file.Sha256, cancellationToken)));
        }

        return detections;
    }

    public async Task<ImportResultDto> ImportAsync(IReadOnlyList<ReportFile> files, CancellationToken cancellationToken)
    {
        List<ImportFileOutcomeDto> outcomes = [];
        foreach (ReportFile file in files)
        {
            outcomes.Add(await ImportOneAsync(file, rerunOf: null, cancellationToken));
        }

        return new ImportResultDto(outcomes);
    }

    /// <summary>Reads a past run's exact file again — after a parser fix, or once a refund's sale is recorded.</summary>
    public async Task<AppResult<SyncRunDto>> RerunAsync(Guid runId, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        SyncRun? original = await runs.FindRunAsync(organizationId, runId, cancellationToken);
        if (original is null)
        {
            return AppResult<SyncRunDto>.NotFound("That import was not found.");
        }

        if (original.ArtifactId is not Guid artifactId
            || await runs.FindArtifactByIdAsync(organizationId, artifactId, cancellationToken) is not SyncArtifact artifact)
        {
            return AppResult<SyncRunDto>.Validation("The file for this import is no longer kept, so it cannot be re-run.");
        }

        ReportFile file = new(artifact.FileName, ReportFile.Decompress(artifact.CompressedContent));
        ImportFileOutcomeDto outcome = await ImportOneAsync(file, rerunOf: original.Id, cancellationToken);
        return outcome.Run is null
            ? AppResult<SyncRunDto>.Validation(outcome.Problem ?? "The file could not be imported again.")
            : AppResult<SyncRunDto>.Ok(outcome.Run);
    }

    public async Task<SyncRunListDto> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        (IReadOnlyList<SyncRunRow> rows, int total) = await runs.ListRunsAsync(
            currentUser.OrganizationId, (page - 1) * pageSize, pageSize, cancellationToken);

        return new SyncRunListDto(
            rows.Select(row => ToDto(row.Run, row.FileName, row.IssueCount)).ToList(),
            total,
            page,
            pageSize,
            Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
    }

    public async Task<AppResult<SyncRunDetailDto>> GetAsync(Guid runId, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        SyncRun? run = await runs.FindRunAsync(organizationId, runId, cancellationToken);
        if (run is null)
        {
            return AppResult<SyncRunDetailDto>.NotFound("That import was not found.");
        }

        IReadOnlyList<SyncRunIssue> issues = await runs.ListIssuesAsync(organizationId, runId, cancellationToken);
        SyncArtifact? artifact = run.ArtifactId is Guid artifactId
            ? await runs.FindArtifactByIdAsync(organizationId, artifactId, cancellationToken)
            : null;

        return AppResult<SyncRunDetailDto>.Ok(new SyncRunDetailDto(
            ToDto(run, artifact?.FileName, issues.Count),
            issues.Select(ToDto).ToList()));
    }

    /// <summary>The exact file a run read, for "what did this export actually say?".</summary>
    public async Task<AppResult<ReportFile>> DownloadAsync(Guid runId, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        SyncRun? run = await runs.FindRunAsync(organizationId, runId, cancellationToken);
        if (run?.ArtifactId is not Guid artifactId
            || await runs.FindArtifactByIdAsync(organizationId, artifactId, cancellationToken) is not SyncArtifact artifact)
        {
            return AppResult<ReportFile>.NotFound("The file for that import is not kept.");
        }

        if (ReportFormats.Find(run.FormatKey) is ReportFormat format
            && !await access.HasPermission(organizationId, format.RequiredPermission, cancellationToken))
        {
            return AppResult<ReportFile>.NotFound("The file for that import is not kept.");
        }

        return AppResult<ReportFile>.Ok(new ReportFile(artifact.FileName, ReportFile.Decompress(artifact.CompressedContent)));
    }

    private async Task<ImportFileOutcomeDto> ImportOneAsync(ReportFile file, Guid? rerunOf, CancellationToken cancellationToken)
    {
        Guid organizationId = currentUser.OrganizationId;
        if (Refusal(file) is string tooBig)
        {
            return new ImportFileOutcomeDto(file.FileName, null, tooBig);
        }

        ReportFormat? format = ReportFormats.Detect(file);
        if (format is not null && !await access.HasPermission(organizationId, format.RequiredPermission, cancellationToken))
        {
            return new ImportFileOutcomeDto(file.FileName, null, $"Your role cannot import {Describe(format.Area)}.");
        }

        // The same bytes are stored once, however many times they are imported.
        SyncArtifact? artifact = await runs.FindArtifactAsync(organizationId, file.Sha256, cancellationToken);
        if (artifact is null)
        {
            artifact = NewArtifact(organizationId, file);
            runs.Add(artifact);
        }

        SyncRun run = new()
        {
            OrganizationId = organizationId,
            Source = SyncSource.File,
            FormatKey = format?.Key ?? ReportFormats.UnrecognisedKey,
            Platform = format?.Platform ?? "Unknown",
            StartedAt = clock.GetUtcNow(),
            TriggeredByUserId = currentUser.UserId,
            ArtifactId = artifact.Id,
            RerunOfId = rerunOf,
            IsCompleteSnapshot = format?.CompleteSnapshot ?? false,
        };

        if (format is null)
        {
            // Unrecognised files are kept in the history too — with the file — so the attempt can be
            // looked at rather than vanishing.
            run.Status = SyncRunStatus.Failed;
            run.Summary = "Not a marketplace export this system can read.";
            run.FinishedAt = clock.GetUtcNow();
            runs.Add(run);
            await runs.SaveChangesAsync(cancellationToken);
            return new ImportFileOutcomeDto(file.FileName, ToDto(run, artifact.FileName, 0), run.Summary);
        }

        run.ChannelAccountId = await runs.SoleAccountAsync(organizationId, format.Platform, cancellationToken);
        runs.Add(run);
        // Saved as running before any work starts, so an import that dies part-way still leaves a trace.
        await runs.SaveChangesAsync(cancellationToken);

        try
        {
            ReadReport report = ReportReader.Read(format, file, await ZoneAsync(organizationId, cancellationToken));
            run.CoversFrom = report.CoversFrom;
            run.CoversTo = report.CoversTo;

            List<ReportRowIssue> issues = [.. report.Issues];
            Outcome outcome = report.Records == 0
                ? Outcome.Fail("The file has no rows that can be imported.")
                : await ApplyAsync(format, file, report, run.ChannelAccountId, issues, cancellationToken);

            run.Created = outcome.Created;
            run.Updated = outcome.Updated;
            run.Unchanged = outcome.Unchanged;
            run.Rejected = issues.Count(issue => issue.Rejected);
            run.Summary = outcome.Summary;
            run.FinishedAt = clock.GetUtcNow();
            run.Status = outcome.Failed
                ? SyncRunStatus.Failed
                : issues.Count > 0 ? SyncRunStatus.CompletedWithIssues : SyncRunStatus.Succeeded;

            runs.AddIssues(issues.Select(issue => new SyncRunIssue
            {
                OrganizationId = organizationId,
                SyncRunId = run.Id,
                Severity = issue.Rejected ? SyncIssueSeverity.Error : SyncIssueSeverity.Warning,
                RowNumber = issue.RowNumber > 0 ? issue.RowNumber : null,
                ExternalReference = Clip(issue.Reference, 200),
                Message = Clip(issue.Message, 1000)!,
            }));
            await runs.SaveChangesAsync(cancellationToken);
            return new ImportFileOutcomeDto(file.FileName, ToDto(run, artifact.FileName, issues.Count), outcome.Failed ? outcome.Summary : null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The detail goes to the log, keyed by the run; the operator gets a sentence and the reference.
            logger.LogError(exception, "Import {RunId} of {FormatKey} failed", run.Id, run.FormatKey);
            string summary = $"The import stopped part-way; nothing after the failure was saved. Reference {run.Id}.";
            await runs.FailAsync(organizationId, run.Id, summary, clock.GetUtcNow(), cancellationToken);
            run.Status = SyncRunStatus.Failed;
            run.Summary = summary;
            return new ImportFileOutcomeDto(file.FileName, ToDto(run, artifact.FileName, 0), summary);
        }
    }

    private sealed record Outcome(int Created, int Updated, int Unchanged, string Summary, bool Failed = false)
    {
        public static Outcome Fail(string message) => new(0, 0, 0, message, Failed: true);
    }

    private async Task<Outcome> ApplyAsync(
        ReportFormat format,
        ReportFile file,
        ReadReport report,
        Guid? accountId,
        List<ReportRowIssue> issues,
        CancellationToken cancellationToken)
    {
        if (format == ReportFormats.EbayAllOrders || format == ReportFormats.DepopSales)
        {
            (IngestPlatformOrdersResultDto? result, string? error) =
                await orderIngestion.IngestAsync(format.Platform, report.Orders, accountId, cancellationToken);
            if (error is not null)
            {
                return Outcome.Fail(error);
            }

            string summary = Counted(result!.Created, result.Updated, result.Unchanged, "order");
            if (result.AutoReconciled > 0)
            {
                summary += $" {result.AutoReconciled} recorded as sales automatically.";
            }

            if (result.Skipped > 0)
            {
                summary += $" {result.Skipped} you marked Not my inventory were left alone.";
            }

            return new Outcome(result.Created, result.Updated, result.Unchanged,
                summary + await ApplyRefundsAsync(format.Platform, file, issues, cancellationToken));
        }

        if (format == ReportFormats.EbayListingSales)
        {
            IngestPlatformOrdersResultDto result = await feeEnrichment.EnrichEbayListingSalesAsync(report.ListingSales, cancellationToken);
            return new Outcome(result.Created, result.Updated, result.Unchanged,
                $"Fees and net sales refreshed on {result.Updated} order{Plural(result.Updated)}."
                + await ApplyRefundsAsync(format.Platform, file, issues, cancellationToken));
        }

        if (format == ReportFormats.EbayActiveListings || format == ReportFormats.PoshmarkInventory)
        {
            AppResult<IngestPlatformListingsResultDto> result = await listings.IngestAsync(
                new IngestPlatformListingsRequest(format.Platform, report.Listings, accountId), cancellationToken);
            if (result.Status != AppResultStatus.Ok || result.Value is null)
            {
                return Outcome.Fail(result.Message ?? "The listings could not be imported.");
            }

            return new Outcome(result.Value.Created, result.Value.Updated, result.Value.Unchanged,
                Counted(result.Value.Created, result.Value.Updated, result.Value.Unchanged, "listing"));
        }

        if (format == ReportFormats.EbayListingQuality)
        {
            AppResult<ImportPlatformListingQualityResultDto> result =
                await listings.ImportQualityWorkbookAsync(file.Content, format.Platform, cancellationToken);
            if (result.Status != AppResultStatus.Ok || result.Value is null)
            {
                return Outcome.Fail(result.Message ?? "The workbook could not be imported.");
            }

            issues.AddRange(result.Value.Warnings.Select(warning => new ReportRowIssue(0, null, warning, false)));
            return new Outcome(result.Value.Imported, result.Value.Updated, 0,
                $"{result.Value.Imported + result.Value.Updated} quality snapshot{Plural(result.Value.Imported + result.Value.Updated)}; " +
                $"{result.Value.MatchedListings} matched to known listings.");
        }

        return Outcome.Fail($"No import is defined for {format.Label}.");
    }

    /// <summary>
    /// Applies the refunds a sales export carries. Only runs when the file holds any: a sales file
    /// without refunds is not a refund import that failed.
    /// </summary>
    private async Task<string> ApplyRefundsAsync(string platform, ReportFile file, List<ReportRowIssue> issues, CancellationToken cancellationToken)
    {
        string csv = file.Text();
        if (MarketplaceRefundReportParser.Parse(platform, csv).Events.Count == 0)
        {
            return string.Empty;
        }

        (ImportMarketplaceRefundsResultDto? result, string? error) =
            await refunds.ImportCsvAsync(new ImportMarketplaceRefundsCsvRequest(platform, csv), cancellationToken);
        if (error is not null)
        {
            issues.Add(new ReportRowIssue(0, null, $"Refunds were not applied: {error}", false));
            return string.Empty;
        }

        issues.AddRange(result!.Warnings.Select(warning => new ReportRowIssue(0, null, warning, false)));
        return result.Imported > 0 ? $" {result.Imported} refund{Plural(result.Imported)} applied." : string.Empty;
    }

    private async Task<TimeZoneInfo> ZoneAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        string id = (await organizations.FindAsync(organizationId, cancellationToken))?.TimeZoneId ?? Organization.DefaultTimeZoneId;
        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out TimeZoneInfo? zone)
            ? zone
            : TimeZoneInfo.FindSystemTimeZoneById(Organization.DefaultTimeZoneId);
    }

    private static string? Refusal(ReportFile file) =>
        file.Content.Length == 0 ? "The file is empty."
        : file.Content.Length > MaxFileBytes ? $"The file is over {MaxFileBytes / (1024 * 1024)} MB, which is larger than any marketplace export."
        : null;

    private static SyncArtifact NewArtifact(Guid organizationId, ReportFile file)
    {
        SyncArtifact artifact = new()
        {
            OrganizationId = organizationId,
            FileName = Clip(Path.GetFileName(file.FileName), 260) ?? "export",
            ContentType = file.LooksLikeWorkbook
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "text/csv",
            SizeBytes = file.Content.LongLength,
            Sha256 = file.Sha256,
            CompressedContent = file.Compressed(),
        };
        return artifact;
    }

    private static ReportDetectionDto Unimportable(ReportFile file, ReportFormat? format, string problem) => new(
        file.FileName, file.Content.LongLength, format?.Key, format?.Platform, format?.Label, 0, null, null,
        format?.CompleteSnapshot ?? false, CanImport: false, problem, [], null);

    private static string Describe(ReportArea area) => area switch
    {
        ReportArea.Orders => "sales and orders",
        ReportArea.Listings => "listings",
        _ => "listing-quality reports",
    };

    private static string Counted(int created, int updated, int unchanged, string noun) =>
        $"{created} new, {updated} updated, {unchanged} unchanged {noun}{Plural(created + updated + unchanged)}.";

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private static string? Clip(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..(max - 1)] + "…";

    private static SyncRunIssueDto ToDto(ReportRowIssue issue) =>
        new(issue.Rejected ? "Error" : "Warning", issue.RowNumber > 0 ? issue.RowNumber : null, issue.Reference, issue.Message);

    private static SyncRunIssueDto ToDto(SyncRunIssue issue) =>
        new(issue.Severity.ToString(), issue.RowNumber, issue.ExternalReference, issue.Message);

    private static SyncRunDto ToDto(SyncRun run, string? fileName, int issueCount) => new(
        run.Id,
        run.FormatKey,
        run.Platform,
        ReportFormats.Find(run.FormatKey)?.Label,
        run.Source.ToString(),
        run.Status.ToString(),
        run.StartedAt,
        run.FinishedAt,
        run.Created,
        run.Updated,
        run.Unchanged,
        run.Rejected,
        run.CoversFrom,
        run.CoversTo,
        run.IsCompleteSnapshot,
        run.Summary,
        fileName,
        issueCount,
        run.RerunOfId,
        CanRerun: run.ArtifactId is not null && run.Status != SyncRunStatus.Running);
}
