"use client";

import Link from "next/link";
import { AlertTriangle, RefreshCw } from "lucide-react";
import { Button, Card, CardContent } from "@overhaul/ui";

type ErrorPanelProps = {
  title: string;
  description: string;
  /** Next's boundary reset — re-renders the segment without a full page load. */
  onRetry?: () => void;
  /** Next replaces the message in production and gives this instead; it identifies the log entry. */
  digest?: string;
  homeHref?: string;
};

/**
 * What a failed page shows. Shared so a boundary is a few lines rather than a reason to skip adding
 * one — the app had none at all, and a single throw took the whole console down with it.
 */
export function ErrorPanel({ title, description, onRetry, digest, homeHref = "/" }: ErrorPanelProps) {
  return (
    <Card className="mx-auto mt-10 max-w-lg">
      <CardContent className="flex flex-col items-start gap-4 pt-6">
        <div className="bg-destructive/10 text-destructive flex h-10 w-10 items-center justify-center rounded-full">
          <AlertTriangle className="h-5 w-5" aria-hidden />
        </div>
        <div className="space-y-1">
          <h2 className="text-lg font-semibold">{title}</h2>
          <p className="text-muted-foreground text-sm">{description}</p>
        </div>
        {digest ? (
          <p className="text-muted-foreground font-mono text-xs">
            Reference: <span className="select-all">{digest}</span>
          </p>
        ) : null}
        <div className="flex flex-wrap gap-2">
          {onRetry ? (
            <Button onClick={onRetry}>
              <RefreshCw className="mr-2 h-4 w-4" aria-hidden />
              Try again
            </Button>
          ) : null}
          <Button variant="outline" asChild>
            <Link href={homeHref}>Back to dashboard</Link>
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
