"use client";

import { useActionState, useState } from "react";
import { useFormStatus } from "react-dom";
import Link from "next/link";
import type { components } from "@overhaul/api-client";
import { Plus, Trash2 } from "lucide-react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { saveRegistration, type TaxActionResult } from "../actions";

type Profile = components["schemas"]["SalesTaxProfileDto"];
type FrequencyRow = { effectiveFrom: string; frequency: string; alignment: string };

const initial: TaxActionResult = { ok: false, message: "" };
const select =
  "border-input bg-background focus:ring-ring h-10 w-full rounded-lg border px-3 text-sm focus:outline-none focus:ring-2";

function SaveButton() {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending}>
      {pending ? "Saving…" : "Save registration"}
    </Button>
  );
}

export function RegistrationForm({ profile }: { profile: Profile }) {
  const [state, action] = useActionState(saveRegistration, initial);
  const [rows, setRows] = useState<FrequencyRow[]>(
    profile.frequencies?.length
      ? profile.frequencies.map((row) => ({ effectiveFrom: row.effectiveFrom ?? "", frequency: row.frequency ?? "Quarterly", alignment: row.alignment ?? "Calendar" }))
      : [{ effectiveFrom: profile.registeredOn ?? "", frequency: "Quarterly", alignment: "Calendar" }],
  );
  const floridaZone = profile.timeZoneId === "America/New_York" || profile.timeZoneId === "America/Chicago";

  const update = (index: number, patch: Partial<FrequencyRow>) => setRows((current) => current.map((row, at) => (at === index ? { ...row, ...patch } : row)));

  return (
    <form action={action} className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Business</CardTitle>
          <CardDescription>As printed on the certificate. The county sets the surtax rate on the return.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 sm:grid-cols-2">
          <Field label="Legal name" name="legalName" defaultValue={profile.legalName} required className="sm:col-span-2" />
          <Field label="Doing business as" name="doingBusinessAs" defaultValue={profile.doingBusinessAs} />
          <Field label="Certificate number" name="certificateNumber" defaultValue={profile.certificateNumber} placeholder="78-8012345678-9" hint="13 digits, on the DR-11 and every return." />
          <Field label="Business partner number" name="businessPartnerNumber" defaultValue={profile.businessPartnerNumber} hint="Optional. Used in letters to DOR." />
          <div className="space-y-2">
            <Label htmlFor="county">County</Label>
            <select id="county" name="county" defaultValue={profile.county ?? ""} required className={select}>
              <option value="" disabled>Choose a county</option>
              {(profile.counties ?? []).map((county) => (
                <option key={county} value={county}>{county}</option>
              ))}
            </select>
          </div>
          <Field label="Street address" name="addressLine1" defaultValue={profile.addressLine1} className="sm:col-span-2" />
          <Field label="Address line 2" name="addressLine2" defaultValue={profile.addressLine2} />
          <Field label="City" name="city" defaultValue={profile.city} />
          <Field label="ZIP" name="postalCode" defaultValue={profile.postalCode} />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">Registration and frequency</CardTitle>
          <CardDescription>
            Every period from the registration date onward needs a return, even with no tax due. Add a row when DOR changes
            your frequency; it starts on the first day of a period.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Field label="Registration effective" name="registeredOn" type="date" defaultValue={profile.registeredOn} required />
            <Field label="Business closed" name="closedOn" type="date" defaultValue={profile.closedOn} hint="Only when closing: produces the final return, due within 15 days." />
          </div>

          <div className="space-y-2">
            <Label>Filing frequency</Label>
            {rows.map((row, index) => (
              <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_1fr_auto]">
                <Input type="date" name="frequencyFrom" aria-label="Starts" value={row.effectiveFrom} onChange={(event) => update(index, { effectiveFrom: event.target.value })} required />
                <select name="frequency" aria-label="Frequency" value={row.frequency} onChange={(event) => update(index, { frequency: event.target.value })} className={select}>
                  <option value="Monthly">Monthly</option>
                  <option value="Quarterly">Quarterly</option>
                  <option value="Semiannual">Semiannual</option>
                  <option value="Annual">Annual</option>
                </select>
                <select name="alignment" aria-label="Periods" value={row.alignment} onChange={(event) => update(index, { alignment: event.target.value })} className={select}>
                  <option value="Calendar">Calendar periods</option>
                  <option value="Alternate" disabled={row.frequency !== "Quarterly" && row.frequency !== "Semiannual"}>
                    Alternate (ending Feb/May/Aug/Nov)
                  </option>
                </select>
                <Button type="button" variant="ghost" size="icon" aria-label="Remove" disabled={rows.length === 1} onClick={() => setRows((current) => current.filter((_, at) => at !== index))}>
                  <Trash2 />
                </Button>
              </div>
            ))}
            <Button type="button" variant="outline" size="sm" onClick={() => setRows((current) => [...current, { effectiveFrom: "", frequency: "Annual", alignment: "Calendar" }])}>
              <Plus /> Frequency change
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">How you file</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <Check name="filesElectronically" defaultChecked={profile.filesElectronically ?? true} label="File and pay through eServices">
            Electronic filing on time earns the collection allowance (2.5% of the first $1,200, up to $30).
          </Check>
          <Check name="donateCollectionAllowance" defaultChecked={profile.donateCollectionAllowance ?? false} label="Donate the collection allowance to education">
            Leaves Line 11 blank; DOR moves the allowance to the Educational Enhancement Trust Fund.
          </Check>
          <Check name="deliveryChargesOptional" defaultChecked={profile.deliveryChargesOptional ?? true} label="Shipping is separately stated and buyers can pick up instead">
            Then shipping is not taxable (Rule 12A-1.045, F.A.C.). Turn off if every order must ship.
          </Check>
          <div className="space-y-2">
            <Label htmlFor="notes">Notes</Label>
            <Textarea id="notes" name="notes" defaultValue={profile.notes ?? ""} rows={2} />
          </div>
          <p className="text-muted-foreground text-xs">
            Days are counted in <strong>{profile.timeZoneId}</strong>.{" "}
            {floridaZone ? null : (
              <>
                That is not a Florida zone; change it in <Link href="/settings" className="underline">Settings</Link> so late-evening sales land on the right day.
              </>
            )}
          </p>
        </CardContent>
      </Card>

      <div className="flex items-center gap-4">
        <SaveButton />
        {state.message ? (
          <p role="status" className={`text-sm ${state.ok ? "text-success" : "text-destructive"}`}>
            {state.message}
          </p>
        ) : null}
      </div>
    </form>
  );
}

function Field({
  label,
  name,
  defaultValue,
  hint,
  className,
  ...props
}: { label: string; name: string; defaultValue?: string | null; hint?: string; className?: string } & Omit<React.ComponentProps<typeof Input>, "defaultValue" | "name">) {
  return (
    <div className={`space-y-2 ${className ?? ""}`}>
      <Label htmlFor={name}>{label}</Label>
      <Input id={name} name={name} defaultValue={defaultValue ?? ""} {...props} />
      {hint ? <p className="text-muted-foreground text-xs">{hint}</p> : null}
    </div>
  );
}

function Check({ name, label, defaultChecked, children }: { name: string; label: string; defaultChecked: boolean; children: React.ReactNode }) {
  return (
    <label className="flex items-start gap-3 text-sm">
      <input type="checkbox" name={name} defaultChecked={defaultChecked} className="mt-1 h-4 w-4" />
      <span>
        <span className="font-medium">{label}</span>
        <span className="text-muted-foreground block text-xs">{children}</span>
      </span>
    </label>
  );
}
