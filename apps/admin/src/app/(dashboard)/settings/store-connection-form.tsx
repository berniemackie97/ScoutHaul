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
      {pending ? "Saving…" : "Save"}
    </Button>
  );
}

export function StoreConnectionForm({
  initial,
}: {
  initial: { name: string; storeDomain: string | null; isLive: boolean; timeZoneId: string };
}) {
  const [state, formAction] = useActionState(updateStoreSettings, initialState);

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-4">
          <div>
            <CardTitle className="text-lg">Business &amp; storefront</CardTitle>
            <CardDescription>
              Your business name, the time zone your days are counted in, and the domain your public
              storefront answers on.
            </CardDescription>
          </div>
          {initial.isLive ? <Badge variant="success">Live</Badge> : <Badge variant="secondary">Not connected</Badge>}
        </div>
      </CardHeader>
      <CardContent>
        <form action={formAction} className="space-y-5">
          <div className="space-y-2">
            <Label htmlFor="name">Business name</Label>
            <Input id="name" name="name" defaultValue={initial.name} placeholder="Overhaul Trading Company" required />
          </div>

          <div className="space-y-2">
            <Label htmlFor="timeZoneId">Time zone</Label>
            <select
              id="timeZoneId"
              name="timeZoneId"
              defaultValue={initial.timeZoneId}
              className="border-input bg-background focus:ring-ring h-10 w-full rounded-lg border px-3 text-sm focus:outline-none focus:ring-2"
            >
              {zoneOptions(initial.timeZoneId).map((group) => (
                <optgroup key={group.label} label={group.label}>
                  {group.zones.map((zone) => (
                    <option key={zone.id} value={zone.id}>{zone.label}</option>
                  ))}
                </optgroup>
              ))}
            </select>
            <p className="text-xs text-muted-foreground">
              Reports count days in this zone, and marketplace exports that print local times without a zone are read in it.
            </p>
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
              className={`text-sm ${state.message ? (state.ok ? "text-success" : "text-destructive") : "text-muted-foreground"}`}
            >
              {state.message || "Changes take effect immediately."}
            </p>
            <SaveButton />
          </div>
        </form>
      </CardContent>
    </Card>
  );
}

const ZONE_GROUPS: Array<{ label: string; zones: Array<{ id: string; label: string }> }> = [
  {
    label: "United States",
    zones: [
      { id: "America/New_York", label: "Eastern — New York" },
      { id: "America/Chicago", label: "Central — Chicago" },
      { id: "America/Denver", label: "Mountain — Denver" },
      { id: "America/Phoenix", label: "Mountain, no DST — Phoenix" },
      { id: "America/Los_Angeles", label: "Pacific — Los Angeles" },
      { id: "America/Anchorage", label: "Alaska — Anchorage" },
      { id: "Pacific/Honolulu", label: "Hawaii — Honolulu" },
    ],
  },
  {
    label: "Canada",
    zones: [
      { id: "America/Halifax", label: "Atlantic — Halifax" },
      { id: "America/Toronto", label: "Eastern — Toronto" },
      { id: "America/Winnipeg", label: "Central — Winnipeg" },
      { id: "America/Edmonton", label: "Mountain — Edmonton" },
      { id: "America/Vancouver", label: "Pacific — Vancouver" },
    ],
  },
  {
    label: "Europe",
    zones: [
      { id: "Europe/London", label: "London" },
      { id: "Europe/Dublin", label: "Dublin" },
      { id: "Europe/Paris", label: "Paris" },
      { id: "Europe/Berlin", label: "Berlin" },
      { id: "Europe/Madrid", label: "Madrid" },
      { id: "Europe/Amsterdam", label: "Amsterdam" },
    ],
  },
  {
    label: "Asia Pacific",
    zones: [
      { id: "Australia/Sydney", label: "Sydney" },
      { id: "Australia/Melbourne", label: "Melbourne" },
      { id: "Australia/Perth", label: "Perth" },
      { id: "Pacific/Auckland", label: "Auckland" },
      { id: "Asia/Tokyo", label: "Tokyo" },
      { id: "Asia/Singapore", label: "Singapore" },
    ],
  },
  { label: "Other", zones: [{ id: "UTC", label: "UTC" }] },
];

/** A fixed list so server and browser render the same options; a saved zone outside it is kept selectable. */
function zoneOptions(current: string) {
  const known = ZONE_GROUPS.some((group) => group.zones.some((zone) => zone.id === current));
  return known ? ZONE_GROUPS : [{ label: "Current", zones: [{ id: current, label: current }] }, ...ZONE_GROUPS];
}
