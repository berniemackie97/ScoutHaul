using HaulScout.Application.Contracts;
using HaulScout.Domain.Entities;

namespace HaulScout.Application.Sync;

/// <summary>A run as the history shows it — one mapping for file drops and scans alike.</summary>
public static class SyncRunDtos
{
    public static SyncRunDto From(SyncRun run, string? fileName, int issueCount, string? agentName = null) => new(
        run.Id,
        run.FormatKey,
        run.Platform,
        Label(run),
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
        // Only a kept file can be read again; a scan is repeated by scanning.
        CanRerun: run.ArtifactId is not null && run.Status != SyncRunStatus.Running,
        run.Loop?.ToString(),
        run.Trigger?.ToString(),
        agentName ?? run.SyncAgent?.Name);

    /// <summary>"eBay All Orders report" for a file; "eBay sales scan" for an agent run.</summary>
    public static string? Label(SyncRun run) => run.Source == SyncSource.Agent && run.Loop is SyncLoopKind loop
        ? $"{run.Platform} {(loop == SyncLoopKind.Sales ? "sales" : "listings")} scan"
        : ReportFormats.Find(run.FormatKey)?.Label;
}
