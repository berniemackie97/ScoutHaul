"use client";

import { useActionState } from "react";
import { Loader2, RotateCcw } from "lucide-react";
import { Button } from "@overhaul/ui";
import { rerunImport, type RerunState } from "../actions";

/** Re-reads the kept file — after a parser fix, or once a refund's sale has been recorded. */
export function RerunButton({ runId }: { runId: string }) {
  const [state, action, pending] = useActionState<RerunState>(rerunImport.bind(null, runId), { error: null });
  return (
    <form action={action} className="flex flex-col items-end gap-1">
      <Button type="submit" variant="outline" disabled={pending}>
        {pending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> : <RotateCcw className="mr-2 h-4 w-4" aria-hidden />}
        Run again
      </Button>
      {state.error ? (
        <p role="alert" className="text-destructive text-xs">
          {state.error}
        </p>
      ) : null}
    </form>
  );
}
