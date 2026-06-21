"use client";

import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import { ExternalLink, Globe } from "lucide-react";
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Input,
  Label,
} from "@overhaul/ui";
import { updateStoreSettings, type SettingsActionResult } from "./actions";

const initialState: SettingsActionResult = { ok: false, message: "" };

function SaveButton() {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending}>
      {pending ? "Saving…" : "Save store settings"}
    </Button>
  );
}

export function StoreConnectionForm({
  initial,
}: {
  initial: { name: string; storeDomain: string | null; isLive: boolean };
}) {
  const [state, formAction] = useActionState(updateStoreSettings, initialState);

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-4">
          <div>
            <CardTitle className="text-lg">Business &amp; storefront</CardTitle>
            <CardDescription>
              Connect a public storefront to this organization. The domain below is how the store
              resolves which organization&apos;s inventory and photos to serve.
            </CardDescription>
          </div>
          {initial.isLive ? <Badge variant="success">Live</Badge> : <Badge variant="secondary">Not connected</Badge>}
        </div>
      </CardHeader>
      <CardContent>
        <form action={formAction} className="space-y-5">
          <div className="space-y-2">
            <Label htmlFor="name">Legal / store name</Label>
            <Input id="name" name="name" defaultValue={initial.name} placeholder="Overhaul Trading Company" required />
          </div>

          <div className="space-y-2">
            <Label htmlFor="storeDomain">Storefront domain</Label>
            <Input
              id="storeDomain"
              name="storeDomain"
              defaultValue={initial.storeDomain ?? ""}
              placeholder="overhaultrading.com"
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
            />
            <p className="text-xs text-muted-foreground">
              Bare domain — no <code>https://</code>, no path. Leave blank to disconnect the storefront.
            </p>
            {initial.isLive && initial.storeDomain && (
              <a
                href={`https://${initial.storeDomain}`}
                target="_blank"
                rel="noreferrer"
                className="inline-flex items-center gap-1.5 text-sm text-brand-600 hover:underline"
              >
                <Globe className="h-3.5 w-3.5" /> {initial.storeDomain}
                <ExternalLink className="h-3 w-3" />
              </a>
            )}
          </div>

          <div className="flex items-center justify-between gap-4 border-t pt-4">
            <p
              aria-live="polite"
              className={`text-sm ${state.message ? (state.ok ? "text-green-700" : "text-destructive") : "text-muted-foreground"}`}
            >
              {state.message || "Changes take effect immediately on the public storefront."}
            </p>
            <SaveButton />
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
