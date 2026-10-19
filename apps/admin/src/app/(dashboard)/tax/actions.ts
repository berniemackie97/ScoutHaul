"use server";

import { revalidatePath } from "next/cache";
import type { components } from "@overhaul/api-client";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";

type Schemas = components["schemas"];

export type TaxActionResult = { ok: boolean; message: string };

const text = (form: FormData, name: string) => {
  const value = form.get(name);
  return typeof value === "string" && value.trim().length > 0 ? value.trim() : null;
};

const number = (form: FormData, name: string) => {
  const raw = text(form, name);
  if (raw === null) return null;
  const value = Number(raw.replace(/[$,\s]/g, ""));
  return Number.isFinite(value) ? value : Number.NaN;
};

/** Endpoints that answer with no body type their error as never; read it from the response instead. */
const refused = (result: { response: Response; error?: unknown }) => (result.response.ok ? null : (result.error ?? {}));

const checked = (form: FormData, name: string) => form.get(name) === "on" || form.get(name) === "true";

function refresh(periodKey?: string | null) {
  revalidatePath("/tax");
  if (periodKey) revalidatePath(`/tax/${periodKey}`);
}

export async function saveRegistration(_previous: TaxActionResult, form: FormData): Promise<TaxActionResult> {
  const registeredOn = text(form, "registeredOn");
  if (!registeredOn) return { ok: false, message: "Enter the date the registration took effect." };

  const starts = form.getAll("frequencyFrom").map(String);
  const kinds = form.getAll("frequency").map(String);
  const alignments = form.getAll("alignment").map(String);
  const frequencies: Schemas["SalesTaxFrequencyDto"][] = starts
    .map((from, index) => ({
      effectiveFrom: from,
      frequency: kinds[index] as Schemas["SalesTaxFrequency"],
      alignment: (alignments[index] || "Calendar") as Schemas["SalesTaxPeriodAlignment"],
    }))
    .filter((row) => row.effectiveFrom && row.frequency);

  const body: Schemas["SaveSalesTaxProfileRequest"] = {
    certificateNumber: text(form, "certificateNumber"),
    businessPartnerNumber: text(form, "businessPartnerNumber"),
    legalName: text(form, "legalName") ?? "",
    doingBusinessAs: text(form, "doingBusinessAs"),
    addressLine1: text(form, "addressLine1"),
    addressLine2: text(form, "addressLine2"),
    city: text(form, "city"),
    postalCode: text(form, "postalCode"),
    county: text(form, "county") ?? "",
    registeredOn,
    closedOn: text(form, "closedOn"),
    filesElectronically: checked(form, "filesElectronically"),
    donateCollectionAllowance: checked(form, "donateCollectionAllowance"),
    deliveryChargesOptional: checked(form, "deliveryChargesOptional"),
    notes: text(form, "notes"),
    frequencies,
  };

  const api = await getApiClient();
  const result = await api.PUT("/api/sales-tax/profile", { body });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The registration could not be saved.") };

  refresh();
  revalidatePath("/tax/settings");
  return { ok: true, message: "Saved. Every period from the registration date is now tracked." };
}

export async function recordFiling(_previous: TaxActionResult, form: FormData): Promise<TaxActionResult> {
  const periodKey = text(form, "periodKey");
  const filedOn = text(form, "filedOn");
  const amountPaid = number(form, "amountPaid");
  if (!periodKey || !filedOn) return { ok: false, message: "Enter the date the return was filed." };
  if (amountPaid === null || Number.isNaN(amountPaid)) return { ok: false, message: "Enter the amount paid (0.00 for a zero return)." };

  const body: Schemas["FileSalesTaxReturnRequest"] = {
    filedOn,
    paidOn: text(form, "paidOn"),
    method: (text(form, "method") ?? "Electronic") as Schemas["SalesTaxFilingMethod"],
    confirmationNumber: text(form, "confirmationNumber"),
    amountPaid,
    donateCollectionAllowance: checked(form, "donateCollectionAllowance"),
    amended: checked(form, "amended"),
    acknowledgedWarnings: checked(form, "acknowledge") ? Number(text(form, "warningCount") ?? 0) : 0,
    expectedFingerprint: text(form, "fingerprint") ?? "",
    notes: text(form, "notes"),
  };

  const api = await getApiClient();
  const result = await api.POST("/api/sales-tax/periods/{key}/filings", { params: { path: { key: periodKey } }, body });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The filing could not be recorded.") };

  refresh(periodKey);
  return { ok: true, message: body.amended ? "Amended return recorded. It replaces the earlier filing." : "Filing recorded with its snapshot." };
}

export async function voidFiling(_previous: TaxActionResult, form: FormData): Promise<TaxActionResult> {
  const id = text(form, "returnId");
  const reason = text(form, "reason");
  if (!id) return { ok: false, message: "Nothing to void." };
  if (!reason) return { ok: false, message: "Say why this record is being voided." };

  const api = await getApiClient();
  const result = await api.POST("/api/sales-tax/filings/{id}/void", { params: { path: { id } }, body: { reason } });
  if (refused(result)) return { ok: false, message: errorMessage(refused(result), "The filing record could not be voided.") };

  refresh(text(form, "periodKey"));
  return { ok: true, message: "Voided. The period shows as unfiled again." };
}

export async function saveUseEntry(_previous: TaxActionResult, form: FormData): Promise<TaxActionResult> {
  const amount = number(form, "amount");
  const occurredOn = text(form, "occurredOn");
  const description = text(form, "description");
  if (!occurredOn || !description) return { ok: false, message: "Enter the date and what was bought." };
  if (amount === null || Number.isNaN(amount) || amount <= 0) return { ok: false, message: "Enter the purchase amount." };
  const otherState = number(form, "otherStateTaxPaid");

  const api = await getApiClient();
  const result = await api.POST("/api/sales-tax/use-entries", {
    body: {
      occurredOn,
      description,
      amount,
      county: text(form, "county"),
      otherStateTaxPaid: otherState === null || Number.isNaN(otherState) ? 0 : otherState,
      expenseId: null,
      inventoryItemId: null,
      notes: text(form, "notes"),
    },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The use tax entry could not be saved.") };

  refresh(text(form, "periodKey"));
  return { ok: true, message: "Added to Line B." };
}

export async function deleteUseEntry(_previous: TaxActionResult, form: FormData): Promise<TaxActionResult> {
  const id = text(form, "id");
  if (!id) return { ok: false, message: "Nothing to remove." };
  const api = await getApiClient();
  const result = await api.DELETE("/api/sales-tax/use-entries/{id}", { params: { path: { id } } });
  if (refused(result)) return { ok: false, message: errorMessage(refused(result), "The entry could not be removed.") };
  refresh(text(form, "periodKey"));
  return { ok: true, message: "Removed." };
}

export async function addAdjustment(_previous: TaxActionResult, form: FormData): Promise<TaxActionResult> {
  const periodKey = text(form, "periodKey");
  const amount = number(form, "amount");
  const description = text(form, "description");
  if (!periodKey || !description) return { ok: false, message: "Describe the adjustment and its source." };
  if (amount === null || Number.isNaN(amount) || amount <= 0) return { ok: false, message: "Enter the amount." };

  const api = await getApiClient();
  const result = await api.POST("/api/sales-tax/adjustments", {
    body: { periodKey, kind: (text(form, "kind") ?? "LawfulDeduction") as Schemas["SalesTaxAdjustmentKind"], amount, description },
  });
  if (!result.data) return { ok: false, message: errorMessage(result.error, "The adjustment could not be saved.") };

  refresh(periodKey);
  return { ok: true, message: "Adjustment added." };
}

export async function deleteAdjustment(_previous: TaxActionResult, form: FormData): Promise<TaxActionResult> {
  const id = text(form, "id");
  if (!id) return { ok: false, message: "Nothing to remove." };
  const api = await getApiClient();
  const result = await api.DELETE("/api/sales-tax/adjustments/{id}", { params: { path: { id } } });
  if (refused(result)) return { ok: false, message: errorMessage(refused(result), "The adjustment could not be removed.") };
  refresh(text(form, "periodKey"));
  return { ok: true, message: "Removed." };
}

export async function saveSaleDecision(_previous: TaxActionResult, form: FormData): Promise<TaxActionResult> {
  const saleId = text(form, "saleId");
  if (!saleId) return { ok: false, message: "No sale selected." };
  const destination = (text(form, "destination") ?? "FloridaCounty") as Schemas["Dr15Destination"];

  const api = await getApiClient();
  const result = await api.PUT("/api/sales-tax/sales/{saleId}/decision", {
    params: { path: { saleId } },
    body: {
      destination,
      county: destination === "FloridaCounty" ? text(form, "county") : null,
      exemption: (text(form, "exemption") ?? "None") as Schemas["Dr15Exemption"],
      exemptionCertificate: text(form, "exemptionCertificate"),
      notes: text(form, "notes"),
    },
  });
  if (refused(result)) return { ok: false, message: errorMessage(refused(result), "The decision could not be saved.") };

  refresh(text(form, "periodKey"));
  return { ok: true, message: "Decision saved." };
}

export async function clearSaleDecision(_previous: TaxActionResult, form: FormData): Promise<TaxActionResult> {
  const saleId = text(form, "saleId");
  if (!saleId) return { ok: false, message: "No sale selected." };
  const api = await getApiClient();
  const result = await api.DELETE("/api/sales-tax/sales/{saleId}/decision", { params: { path: { saleId } } });
  if (refused(result)) return { ok: false, message: errorMessage(refused(result), "The decision could not be cleared.") };
  refresh(text(form, "periodKey"));
  return { ok: true, message: "Back to the order's own address." };
}

export async function setSaleCollector(_previous: TaxActionResult, form: FormData): Promise<TaxActionResult> {
  const saleId = text(form, "saleId");
  const reason = text(form, "reason");
  if (!saleId) return { ok: false, message: "No sale selected." };
  if (!reason) return { ok: false, message: "Say who actually collected the tax." };

  const api = await getApiClient();
  const result = await api.PUT("/api/sales-tax/sales/{saleId}/collector", {
    params: { path: { saleId } },
    body: { marketplaceCollected: checked(form, "marketplaceCollected"), reason },
  });
  if (refused(result)) return { ok: false, message: errorMessage(refused(result), "The sale could not be updated.") };

  refresh(text(form, "periodKey"));
  return { ok: true, message: "Updated." };
}
