"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useId, useRef, useState, type DragEvent } from "react";
import { AlertTriangle, CheckCircle2, FileSpreadsheet, Info, Loader2, Upload, X, XCircle } from "lucide-react";
import { Badge, Button, cn } from "@overhaul/ui";
import {
  ACCEPTED_FILES,
  SUPPORTED_EXPORTS,
  countsSentence,
  coverage,
  fileSize,
  importable,
  issueCounts,
  recordsLabel,
  runStatus,
  type ReportDetection,
  type SyncRun,
  type Tone,
} from "@/features/imports/import-model";

type Stage =
  | { name: "idle" }
  | { name: "detecting"; files: File[] }
  | { name: "review"; files: File[]; detections: ReportDetection[] }
  | { name: "importing"; files: File[]; detections: ReportDetection[] }
  | { name: "done"; outcomes: Outcome[] };

type Outcome = { fileName: string; run: SyncRun | null; problem: string | null };

const BADGE: Record<Tone, "success" | "warning" | "destructive" | "info" | "secondary"> = {
  success: "success",
  warning: "warning",
  danger: "destructive",
  info: "info",
  neutral: "secondary",
};

/**
 * Drop any marketplace exports; see what each one is; import.
 *
 * Replaces four upload cards the operator had to choose between. Every file is recognised by its
 * contents and described — what it is, how many records, what period — before anything changes, and
 * a file that cannot be imported says why instead of failing half-way through.
 */
export function ImportDropZone() {
  const router = useRouter();
  const inputId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [stage, setStage] = useState<Stage>({ name: "idle" });
  const [dragging, setDragging] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function detect(files: File[]) {
    if (files.length === 0) return;
    setError(null);
    setStage({ name: "detecting", files });
    try {
      const detections = await post<ReportDetection[]>("/api/imports/detect", files);
      setStage({ name: "review", files, detections });
    } catch (problem) {
      setError(messageOf(problem));
      setStage({ name: "idle" });
    }
  }

  async function runImport() {
    if (stage.name !== "review") return;
    const chosen = stage.files.filter((_, index) => stage.detections[index]?.canImport);
    if (chosen.length === 0) return;

    setError(null);
    setStage({ ...stage, name: "importing" });
    try {
      const result = await post<{ files?: { fileName?: string; run?: SyncRun | null; problem?: string | null }[] }>(
        "/api/imports",
        chosen,
      );
      setStage({
        name: "done",
        outcomes: (result.files ?? []).map((file) => ({
          fileName: file.fileName ?? "file",
          run: file.run ?? null,
          problem: file.problem ?? null,
        })),
      });
      // The history and the match queues below are server-rendered; bring them up to date.
      router.refresh();
    } catch (problem) {
      setError(messageOf(problem));
      setStage({ ...stage, name: "review" });
    }
  }

  function remove(index: number) {
    if (stage.name !== "review") return;
    const files = stage.files.filter((_, position) => position !== index);
    const detections = stage.detections.filter((_, position) => position !== index);
    setStage(files.length === 0 ? { name: "idle" } : { name: "review", files, detections });
  }

  function onDrop(event: DragEvent) {
    event.preventDefault();
    setDragging(false);
    void detect(Array.from(event.dataTransfer.files));
  }

  const busy = stage.name === "detecting" || stage.name === "importing";

  return (
    <section aria-labelledby={`${inputId}-heading`} className="space-y-4">
      <h2 id={`${inputId}-heading`} className="sr-only">
        Import files
      </h2>

      {error ? (
        <p role="alert" className="border-destructive-border bg-destructive-surface text-destructive flex items-start gap-2 rounded-lg border p-3 text-sm">
          <XCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden /> {error}
        </p>
      ) : null}

      {stage.name === "idle" || stage.name === "detecting" ? (
        <div
          onDragOver={(event) => {
            event.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={onDrop}
          className={cn(
            "bg-card flex flex-col items-center justify-center gap-3 rounded-xl border-2 border-dashed px-6 py-10 text-center transition-colors",
            dragging ? "border-brand-500 bg-brand-50/60 dark:bg-brand-950/40" : "border-border",
          )}
        >
          {stage.name === "detecting" ? (
            <p role="status" className="text-muted-foreground flex items-center gap-2 text-sm">
              <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Reading {stage.files.length} file
              {stage.files.length === 1 ? "" : "s"}…
            </p>
          ) : (
            <>
              <div className="bg-brand-50 text-brand-700 dark:bg-brand-900 dark:text-brand-100 flex h-12 w-12 items-center justify-center rounded-full">
                <Upload className="h-5 w-5" aria-hidden />
              </div>
              <div>
                <p className="text-sm font-medium">Drop marketplace exports here</p>
                <p className="text-muted-foreground mt-1 text-xs">
                  Several at once is fine — each is recognised from its contents. Nothing is imported until you confirm.
                </p>
              </div>
              <Button type="button" variant="outline" onClick={() => inputRef.current?.click()}>
                Choose files
              </Button>
              <input
                ref={inputRef}
                id={inputId}
                type="file"
                multiple
                accept={ACCEPTED_FILES}
                className="sr-only"
                onChange={(event) => {
                  void detect(Array.from(event.target.files ?? []));
                  event.target.value = "";
                }}
              />
              <details className="text-muted-foreground mt-1 text-xs">
                <summary className="hover:text-foreground cursor-pointer">Which exports can I use?</summary>
                <ul className="mt-2 space-y-1 text-left">
                  {SUPPORTED_EXPORTS.map((entry) => (
                    <li key={`${entry.platform}-${entry.name}`}>
                      <span className="text-foreground font-medium">
                        {entry.platform} {entry.name}
                      </span>{" "}
                      — {entry.where}
                    </li>
                  ))}
                </ul>
              </details>
            </>
          )}
        </div>
      ) : null}

      {stage.name === "review" || stage.name === "importing" ? (
        <div className="space-y-3">
          <ul className="space-y-3">
            {stage.detections.map((detection, index) => (
              <DetectedFile
                key={`${detection.fileName}-${index}`}
                detection={detection}
                onRemove={stage.name === "review" ? () => remove(index) : undefined}
              />
            ))}
          </ul>
          <div className="flex flex-wrap items-center justify-end gap-3">
            <Button type="button" variant="ghost" disabled={busy} onClick={() => setStage({ name: "idle" })}>
              Cancel
            </Button>
            <Button type="button" disabled={busy || importable(stage.detections).length === 0} onClick={() => void runImport()}>
              {stage.name === "importing" ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> Importing…
                </>
              ) : (
                `Import ${importable(stage.detections).length} file${importable(stage.detections).length === 1 ? "" : "s"}`
              )}
            </Button>
          </div>
        </div>
      ) : null}

      {stage.name === "done" ? (
        <div className="space-y-3">
          <ul className="space-y-3" aria-live="polite">
            {stage.outcomes.map((outcome, index) => (
              <ImportedFile key={`${outcome.fileName}-${index}`} outcome={outcome} />
            ))}
          </ul>
          <div className="flex justify-end">
            <Button type="button" variant="outline" onClick={() => setStage({ name: "idle" })}>
              Import more files
            </Button>
          </div>
        </div>
      ) : null}
    </section>
  );
}

function DetectedFile({ detection, onRemove }: { detection: ReportDetection; onRemove?: () => void }) {
  const period = coverage(detection.coversFrom, detection.coversTo, detection.completeSnapshot ?? false);
  const { errors, warnings } = issueCounts(detection.issues);
  const previewIssues = (detection.issues ?? []).slice(0, 3);

  return (
    <li className={cn("bg-card rounded-xl border p-4", !detection.canImport && "border-destructive/40")}>
      <div className="flex items-start gap-3">
        <FileSpreadsheet className="text-muted-foreground mt-0.5 h-5 w-5 shrink-0" aria-hidden />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <p className="truncate text-sm font-medium">{detection.label ?? "Unrecognised file"}</p>
            {detection.canImport ? (
              <Badge variant="secondary">{recordsLabel(detection)}</Badge>
            ) : (
              <Badge variant="destructive">Can&rsquo;t import</Badge>
            )}
          </div>
          <p className="text-muted-foreground mt-0.5 truncate text-xs">
            {detection.fileName} · {fileSize(detection.sizeBytes)}
            {period ? ` · ${period}` : ""}
          </p>

          {detection.problem ? <p className="text-destructive mt-2 text-sm">{detection.problem}</p> : null}

          {detection.previouslyImportedAt ? (
            <p className="text-muted-foreground mt-2 flex items-start gap-1.5 text-xs">
              <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden />
              This exact file was imported on {new Date(detection.previouslyImportedAt).toLocaleDateString()}. Importing it
              again only changes what differs.
            </p>
          ) : null}

          {errors + warnings > 0 ? (
            <div className="mt-2 space-y-1 text-xs">
              <p className="text-muted-foreground">
                {errors > 0 ? `${errors} row${errors === 1 ? "" : "s"} will be skipped` : null}
                {errors > 0 && warnings > 0 ? " · " : null}
                {warnings > 0 ? `${warnings} note${warnings === 1 ? "" : "s"}` : null}
              </p>
              <ul className="space-y-0.5">
                {previewIssues.map((issue, index) => (
                  <li key={index} className={cn("flex items-start gap-1.5", issue.severity === "Error" ? "text-destructive" : "text-warning")}>
                    <AlertTriangle className="mt-0.5 h-3 w-3 shrink-0" aria-hidden />
                    <span>
                      {issue.rowNumber ? `Row ${issue.rowNumber}: ` : ""}
                      {issue.message}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
        {onRemove ? (
          <button
            type="button"
            onClick={onRemove}
            aria-label={`Remove ${detection.fileName}`}
            className="text-muted-foreground hover:text-foreground rounded p-1"
          >
            <X className="h-4 w-4" />
          </button>
        ) : null}
      </div>
    </li>
  );
}

function ImportedFile({ outcome }: { outcome: Outcome }) {
  const status = runStatus(outcome.run?.status);
  const Icon = outcome.run && outcome.run.status !== "Failed" ? CheckCircle2 : XCircle;

  return (
    <li className="bg-card flex items-start gap-3 rounded-xl border p-4">
      <Icon
        className={cn("mt-0.5 h-5 w-5 shrink-0", status.tone === "danger" || !outcome.run ? "text-destructive" : "text-success")}
        aria-hidden
      />
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <p className="truncate text-sm font-medium">{outcome.run?.formatLabel ?? outcome.fileName}</p>
          {outcome.run ? <Badge variant={BADGE[status.tone]}>{status.label}</Badge> : null}
        </div>
        <p className="text-muted-foreground mt-0.5 text-sm">
          {outcome.run ? (outcome.run.summary ?? countsSentence(outcome.run)) : outcome.problem}
        </p>
      </div>
      {outcome.run?.id ? (
        <Link href={`/imports/runs/${outcome.run.id}`} className="text-brand-600 shrink-0 text-xs font-medium hover:underline">
          Details
        </Link>
      ) : null}
    </li>
  );
}

async function post<T>(url: string, files: File[]): Promise<T> {
  const form = new FormData();
  for (const file of files) form.append("files", file, file.name);

  const response = await fetch(url, { method: "POST", body: form });
  const body: unknown = await response.json().catch(() => null);
  if (!response.ok) throw new Error(detailOf(body) ?? `The server answered ${response.status}.`);
  return body as T;
}

function detailOf(body: unknown): string | null {
  if (body && typeof body === "object") {
    if ("detail" in body && typeof body.detail === "string") return body.detail;
    if ("title" in body && typeof body.title === "string") return body.title;
  }
  return null;
}

function messageOf(problem: unknown): string {
  return problem instanceof Error ? problem.message : "Something went wrong. Try again.";
}
