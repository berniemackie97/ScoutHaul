"use client";

import { useActionState, useState, type ReactNode } from "react";
import { useFormStatus } from "react-dom";
import { Check, Copy, Printer } from "lucide-react";
import { Button, Input, Label, Textarea } from "@overhaul/ui";
import type { components } from "@overhaul/api-client";
import {
  addAdjustment,
  clearSaleDecision,
  deleteAdjustment,
  deleteUseEntry,
  recordFiling,
  saveSaleDecision,
  saveUseEntry,
  setSaleCollector,
  voidFiling,
  type TaxActionResult,
} from "../actions";

type Action = (previous: TaxActionResult, form: FormData) => Promise<TaxActionResult>;
const initial: TaxActionResult = { ok: false, message: "" };
const select = "border-input bg-background focus:ring-ring h-10 w-full rounded-lg border px-3 text-sm focus:outline-none focus:ring-2";

export function CopyValue({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      onClick={async () => {
        await navigator.clipboard.writeText(value);
        setCopied(true);
        setTimeout(() => setCopied(false), 1200);
      }}
      className="text-muted-foreground hover:text-foreground rounded p-1 print:hidden"
      aria-label={`Copy ${label}`}
      title={`Copy ${value}`}
    >
      {copied ? <Check className="h-3.5 w-3.5 text-green-600" aria-hidden /> : <Copy className="h-3.5 w-3.5" aria-hidden />}
    </button>
  );
}

export function PrintButton() {
  return (
    <Button type="button" variant="outline" size="sm" onClick={() => window.print()}>
      <Printer /> Print / PDF
    </Button>
  );
}

function Submit({ children, variant, confirm }: { children: ReactNode; variant?: "default" | "outline" | "destructive" | "ghost"; confirm?: string }) {
  const { pending } = useFormStatus();
  return (
    <Button
      type="submit"
      size="sm"
      variant={variant}
      disabled={pending}
      onClick={(event) => {
        if (confirm && !window.confirm(confirm)) event.preventDefault();
      }}
    >
      {pending ? "Working…" : children}
    </Button>
  );
}

function ActionForm({ action, children, className, hidden }: { action: Action; children: ReactNode; className?: string; hidden: Record<string, string> }) {
  const [state, formAction] = useActionState(action, initial);
  return (
    <form action={formAction} className={className}>
      {Object.entries(hidden).map(([name, value]) => (
        <input key={name} type="hidden" name={name} value={value} />
      ))}
      {children}
      {state.message ? (
        <p role="status" className={`mt-2 text-xs ${state.ok ? "text-green-700 dark:text-green-400" : "text-destructive"}`}>
          {state.message}
        </p>
      ) : null}
    </form>
  );
}

export function FilingForm({
  periodKey,
  fingerprint,
  warningCount,
  amountDue,
  amended,
  defaultFiledOn,
  filesElectronically,
  telefileEligible,
}: {
  periodKey: string;
  fingerprint: string;
  warningCount: number;
  amountDue: number;
  amended: boolean;
  defaultFiledOn: string;
  filesElectronically: boolean;
  telefileEligible: boolean;
}) {
  return (
    <ActionForm action={recordFiling} hidden={{ periodKey, fingerprint, warningCount: String(warningCount), amended: String(amended) }} className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="filedOn">Filed on</Label>
          <Input id="filedOn" name="filedOn" type="date" defaultValue={defaultFiledOn} required />
        </div>
        <div className="space-y-2">
          <Label htmlFor="paidOn">Paid on</Label>
          <Input id="paidOn" name="paidOn" type="date" />
          <p className="text-muted-foreground text-xs">Leave blank if paid the same day, or nothing was due.</p>
        </div>
        <div className="space-y-2">
          <Label htmlFor="method">How</Label>
          <select id="method" name="method" defaultValue={filesElectronically ? "Electronic" : "Paper"} className={select}>
            <option value="Electronic">eServices (electronic)</option>
            <option value="Telefile" disabled={!telefileEligible}>Telefile 800-550-6713 (no tax due)</option>
            <option value="Paper">Paper (mailed)</option>
          </select>
        </div>
        <div className="space-y-2">
          <Label htmlFor="confirmationNumber">Confirmation number</Label>
          <Input id="confirmationNumber" name="confirmationNumber" placeholder="From eServices or telefile" />
        </div>
        <div className="space-y-2">
          <Label htmlFor="amountPaid">Amount paid</Label>
          <Input id="amountPaid" name="amountPaid" inputMode="decimal" defaultValue={amountDue.toFixed(2)} required />
        </div>
        <label className="flex items-start gap-3 pt-7 text-sm">
          <input type="checkbox" name="donateCollectionAllowance" className="mt-1 h-4 w-4" />
          <span>Donated the collection allowance</span>
        </label>
      </div>
      {warningCount > 0 ? (
        <label className="flex items-start gap-3 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-700 dark:bg-amber-950/40 dark:text-amber-100">
          <input type="checkbox" name="acknowledge" required className="mt-1 h-4 w-4" />
          <span>I reviewed the {warningCount} warning{warningCount === 1 ? "" : "s"} above and the return is right as it stands.</span>
        </label>
      ) : null}
      <div className="space-y-2">
        <Label htmlFor="filingNotes">Notes</Label>
        <Textarea id="filingNotes" name="notes" rows={2} />
      </div>
      <Submit>{amended ? "Record amended return" : "Record filing"}</Submit>
    </ActionForm>
  );
}

export function VoidForm({ periodKey, returnId }: { periodKey: string; returnId: string }) {
  return (
    <ActionForm action={voidFiling} hidden={{ periodKey, returnId }} className="flex flex-wrap items-end gap-2">
      <Input name="reason" placeholder="Why void this record?" className="h-9 max-w-xs" required />
      <Submit variant="outline" confirm="Void this filing record? The period will show as unfiled.">Void record</Submit>
    </ActionForm>
  );
}

export function UseTaxForm({ periodKey, counties, defaultDate }: { periodKey: string; counties: string[]; defaultDate: string }) {
  return (
    <ActionForm action={saveUseEntry} hidden={{ periodKey }} className="grid gap-3 sm:grid-cols-[150px_1fr_130px_160px_auto] sm:items-end">
      <div className="space-y-1">
        <Label htmlFor="useDate">Date</Label>
        <Input id="useDate" name="occurredOn" type="date" defaultValue={defaultDate} required />
      </div>
      <div className="space-y-1">
        <Label htmlFor="useDescription">What was bought and used</Label>
        <Input id="useDescription" name="description" placeholder="Label printer, bought online, no FL tax" required />
      </div>
      <div className="space-y-1">
        <Label htmlFor="useAmount">Amount</Label>
        <Input id="useAmount" name="amount" inputMode="decimal" required />
      </div>
      <div className="space-y-1">
        <Label htmlFor="useCounty">Used in</Label>
        <select id="useCounty" name="county" defaultValue="" className={select}>
          <option value="">Business county</option>
          {counties.map((county) => (
            <option key={county} value={county}>{county}</option>
          ))}
        </select>
      </div>
      <Submit>Add to Line B</Submit>
      <details className="sm:col-span-5">
        <summary className="text-muted-foreground cursor-pointer text-xs">Paid tax to another state on it?</summary>
        <div className="mt-2 max-w-xs space-y-1">
          <Label htmlFor="otherStateTaxPaid">Tax paid to the other state</Label>
          <Input id="otherStateTaxPaid" name="otherStateTaxPaid" inputMode="decimal" placeholder="0.00" />
          <p className="text-muted-foreground text-xs">Credited on Line 6 (DR-15N, Line B).</p>
        </div>
      </details>
    </ActionForm>
  );
}

export function RemoveButton({ kind, id, periodKey }: { kind: "use" | "adjustment"; id: string; periodKey: string }) {
  return (
    <ActionForm action={kind === "use" ? deleteUseEntry : deleteAdjustment} hidden={{ id, periodKey }}>
      <Submit variant="ghost" confirm="Remove this entry?">Remove</Submit>
    </ActionForm>
  );
}

export function AdjustmentForm({ periodKey }: { periodKey: string }) {
  return (
    <ActionForm action={addAdjustment} hidden={{ periodKey }} className="grid gap-3 sm:grid-cols-[220px_1fr_130px_auto] sm:items-end">
      <div className="space-y-1">
        <Label htmlFor="adjustmentKind">Line</Label>
        <select id="adjustmentKind" name="kind" className={select}>
          <option value="LawfulDeduction">6 · Lawful deduction</option>
          <option value="CreditOrEstimatedTaxPaid">8 · Est. tax paid / DOR credit memo</option>
          <option value="EstimatedTaxDue">9 · Est. tax due this month</option>
        </select>
      </div>
      <div className="space-y-1">
        <Label htmlFor="adjustmentDescription">Source</Label>
        <Input id="adjustmentDescription" name="description" placeholder="Credit memo number, statute, reason" required />
      </div>
      <div className="space-y-1">
        <Label htmlFor="adjustmentAmount">Amount</Label>
        <Input id="adjustmentAmount" name="amount" inputMode="decimal" required />
      </div>
      <Submit variant="outline">Add</Submit>
    </ActionForm>
  );
}

type Sale = components["schemas"]["SalesTaxReportedSaleDto"];

export function SaleDecisionForm({ sale, periodKey, counties }: { sale: Sale; periodKey: string; counties: string[] }) {
  const [destination, setDestination] = useState(sale.destination === "OutsideFlorida" ? "OutsideFlorida" : "FloridaCounty");
  const [exemption, setExemption] = useState(sale.exemption ?? "None");
  return (
    <div className="space-y-3">
      <ActionForm action={saveSaleDecision} hidden={{ periodKey, saleId: sale.saleId ?? "" }} className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-1">
          <Label>Delivered</Label>
          <select name="destination" value={destination} onChange={(event) => setDestination(event.target.value)} className={select}>
            <option value="FloridaCounty">In Florida</option>
            <option value="OutsideFlorida">Outside Florida (exempt)</option>
          </select>
        </div>
        {destination === "FloridaCounty" ? (
          <div className="space-y-1">
            <Label>County</Label>
            <select name="county" defaultValue={sale.county ?? ""} required className={select}>
              <option value="" disabled>Choose the delivery county</option>
              {counties.map((county) => (
                <option key={county} value={county}>{county}</option>
              ))}
            </select>
          </div>
        ) : <div />}
        <div className="space-y-1">
          <Label>Exemption</Label>
          <select name="exemption" value={exemption} onChange={(event) => setExemption(event.target.value as typeof exemption)} className={select}>
            <option value="None">None (taxable)</option>
            <option value="Resale">Sale for resale (buyer&apos;s resale certificate)</option>
            <option value="ExemptOrganization">Exempt organization (DR-14)</option>
            <option value="Other">Other (explain in notes)</option>
          </select>
        </div>
        {exemption === "Resale" || exemption === "ExemptOrganization" ? (
          <div className="space-y-1">
            <Label>Certificate number</Label>
            <Input name="exemptionCertificate" required />
          </div>
        ) : <div />}
        <div className="space-y-1 sm:col-span-2">
          <Label>Notes</Label>
          <Input name="notes" placeholder="How you know: tracking, pickup, buyer's address" />
        </div>
        <div className="sm:col-span-2">
          <Submit>Save decision</Submit>
        </div>
      </ActionForm>
      {sale.hasDecision ? (
        <ActionForm action={clearSaleDecision} hidden={{ periodKey, saleId: sale.saleId ?? "" }}>
          <Submit variant="ghost">Clear decision</Submit>
        </ActionForm>
      ) : null}
    </div>
  );
}

export function CollectorForm({ periodKey, saleId, marketplaceCollected, label }: { periodKey: string; saleId: string; marketplaceCollected: boolean; label: string }) {
  return (
    <ActionForm action={setSaleCollector} hidden={{ periodKey, saleId, marketplaceCollected: String(marketplaceCollected) }} className="mt-2 flex flex-wrap items-center gap-2">
      <Input name="reason" placeholder="Who actually collected the tax, and how you know" className="h-9 max-w-sm" required />
      <Submit variant="outline">{label}</Submit>
    </ActionForm>
  );
}
