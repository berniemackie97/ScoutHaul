"use client";

import { useActionState, useId } from "react";
import { useFormStatus } from "react-dom";
import { UploadCloud } from "lucide-react";
import {
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Label,
  Textarea,
} from "@overhaul/ui";
import type { ImportResult } from "./actions";

const initial: ImportResult = { ok: false, message: "" };

function SubmitButton({ label }: { label: string }) {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending}>
      <UploadCloud className="mr-2 h-4 w-4" />
      {pending ? "Importing…" : label}
    </Button>
  );
}

export function CsvUploader({
  title,
  description,
  action,
  buttonLabel = "Import CSV",
}: {
  title: string;
  description: string;
  action: (previous: ImportResult, formData: FormData) => Promise<ImportResult>;
  buttonLabel?: string;
}) {
  const [state, formAction] = useActionState(action, initial);
  const fileId = useId();
  const csvId = useId();
  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">{title}</CardTitle>
        <CardDescription>{description}</CardDescription>
      </CardHeader>
      <CardContent>
        <form action={formAction} className="space-y-3">
          <div className="grid gap-2">
            <Label htmlFor={fileId}>CSV file</Label>
            <input
              id={fileId}
              name="file"
              type="file"
              accept=".csv,text/csv"
              className="file:bg-muted file:text-foreground text-sm file:mr-3 file:rounded-md file:border-0 file:px-3 file:py-1.5 file:text-sm"
            />
          </div>
          <details className="rounded-lg border p-3">
            <summary className="text-muted-foreground cursor-pointer text-xs font-medium">
              Paste CSV instead
            </summary>
            <div className="mt-3 grid gap-2">
              <Label htmlFor={csvId}>CSV contents</Label>
              <Textarea id={csvId} name="csv" rows={8} className="font-mono text-xs" />
            </div>
          </details>
          <div className="flex items-center justify-between gap-3">
            {state.message && (
              <p className={`text-sm ${state.ok ? "text-emerald-700" : "text-destructive"}`}>
                {state.message}
              </p>
            )}
            <SubmitButton label={buttonLabel} />
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
