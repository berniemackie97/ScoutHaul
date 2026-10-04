import type { InventoryItem, InventoryStatus } from "./inventory-operations";
import { fieldSpec, type ItemField, type ItemProfile } from "./item-profiles";

export type InventoryReadinessFacts = {
  title?: string | null;
  description?: string | null;
  category?: string | null;
  condition?: string | null;
  functionalStatus?: string | null;
  completenessStatus?: string | null;
  authenticityStatus?: string | null;
  packagingStatus?: string | null;
  testingNotes?: string | null;
  estimatedSalePrice?: string | number | null;
  photoCount?: number | null;
  componentCount?: number | null;
  isBundle?: boolean | null;
  /** The item's kind decides which condition details it must have; without one, only the common checks apply. */
  profile?: ItemProfile | null;
};

export type ReadinessRequirement = {
  id: string;
  label: string;
  met: boolean;
  severity: "blocker" | "warning";
};

export const PRE_SALE_STAGE_OPTIONS: Array<{ value: InventoryStatus; label: string; description: string }> = [
  { value: "Draft", label: "Draft", description: "Captured but not operationally ready." },
  { value: "Researching", label: "Researching", description: "Needs identity, comps, or listing research." },
  { value: "Watchlist", label: "Watchlist", description: "Tracked but not yet purchased/committed." },
  { value: "Purchased", label: "Purchased", description: "Owned/acquired and waiting for intake work." },
  { value: "NeedsCleaning", label: "Needs cleaning", description: "Physical cleanup is required before listing." },
  { value: "NeedsTesting", label: "Needs testing", description: "Function must be tested or disclosed." },
  { value: "NeedsRepair", label: "Needs repair", description: "Repair or parts-only decision is required." },
  { value: "NeedsPhotos", label: "Needs photos", description: "Photography is required before listing." },
  { value: "ReadyToList", label: "Ready to list", description: "Facts, condition, valuation, and prep are ready." },
  { value: "Listed", label: "Listed", description: "Published on at least one sales channel." },
  { value: "Donated", label: "Donated", description: "Removed from active resale inventory." },
  { value: "Disposed", label: "Disposed", description: "Removed from active inventory as waste/loss." },
];

export const CREATE_STAGE_OPTIONS = PRE_SALE_STAGE_OPTIONS.filter((option) =>
  !["Listed", "Donated", "Disposed"].includes(option.value)
);

function present(value: unknown) {
  return typeof value === "string" ? value.trim().length > 0 : value !== null && value !== undefined;
}

function positiveMoney(value: string | number | null | undefined) {
  if (value === null || value === undefined || value === "") return false;
  const parsed = typeof value === "number" ? value : Number(value);
  return Number.isFinite(parsed) && parsed > 0;
}

function known(value: string | null | undefined, unknownValues: string[]) {
  return Boolean(value && !unknownValues.includes(value));
}

/** "blocker" or "warning" when the item's kind asks for this field before listing; null when it does not. */
function severityFor(facts: InventoryReadinessFacts, field: ItemField, readyIntent: boolean): ReadinessRequirement["severity"] | null {
  const readiness = facts.profile ? fieldSpec(facts.profile, field)?.readiness : undefined;
  if (readiness === "Required") return readyIntent ? "blocker" : "warning";
  if (readiness === "Recommended") return "warning";
  return null;
}

export function readinessRequirements(
  targetStatus: InventoryStatus,
  facts: InventoryReadinessFacts,
): ReadinessRequirement[] {
  const requirements: ReadinessRequirement[] = [];
  const readyIntent = targetStatus === "ReadyToList" || targetStatus === "Listed";
  const photoRequired = readyIntent || targetStatus === "NeedsPhotos";

  requirements.push({ id: "title", label: "Title recorded", met: present(facts.title), severity: "blocker" });

  if (readyIntent) {
    requirements.push({ id: "category", label: "Category selected", met: present(facts.category), severity: "blocker" });
    if (facts.isBundle) {
      requirements.push({ id: "description", label: "Bundle contents and condition spread described", met: present(facts.description), severity: "blocker" });
    } else {
      requirements.push({ id: "condition", label: "Cosmetic condition selected", met: present(facts.condition), severity: "blocker" });
    }
    requirements.push({ id: "estimatedSalePrice", label: "Expected sale price recorded", met: positiveMoney(facts.estimatedSalePrice), severity: "blocker" });
  }

  if (facts.isBundle || targetStatus === "ReadyToList") {
    requirements.push({
      id: "components",
      label: facts.isBundle ? "At least two bundle components selected" : "Not a bundle-only requirement",
      met: facts.isBundle ? (facts.componentCount ?? 0) >= 2 : true,
      severity: "blocker",
    });
  }

  const functional = severityFor(facts, "FunctionalStatus", readyIntent);
  if (functional) {
    requirements.push({
      id: "functionalStatus",
      label: "Working condition recorded",
      met: known(facts.functionalStatus, ["Unknown", "Untested"]),
      severity: functional,
    });
    requirements.push({
      id: "testingNotes",
      label: "Testing notes or disclosure recorded",
      met: present(facts.testingNotes) || known(facts.functionalStatus, ["Unknown", "Untested", "FullyFunctional"]),
      severity: "warning",
    });
  }

  const checks: Array<{ field: ItemField; id: string; label: string; value: string | null | undefined; unknown: string[] }> = [
    { field: "CompletenessStatus", id: "completenessStatus", label: "Completeness recorded", value: facts.completenessStatus, unknown: ["Unknown"] },
    { field: "PackagingStatus", id: "packagingStatus", label: "Packaging recorded", value: facts.packagingStatus, unknown: ["NotApplicable"] },
    { field: "AuthenticityStatus", id: "authenticityStatus", label: "Authenticity assessed or disclosed", value: facts.authenticityStatus, unknown: ["NotAssessed"] },
  ];
  for (const check of checks) {
    const severity = severityFor(facts, check.field, readyIntent);
    if (severity) requirements.push({ id: check.id, label: check.label, met: known(check.value, check.unknown), severity });
  }

  if (photoRequired && facts.photoCount !== null && facts.photoCount !== undefined) {
    requirements.push({
      id: "photos",
      label: "Photos attached",
      met: facts.photoCount > 0,
      severity: readyIntent ? "blocker" : "warning",
    });
  }

  return requirements.filter((requirement) => requirement.id !== "components" || facts.isBundle);
}

export function readinessBlockers(targetStatus: InventoryStatus, facts: InventoryReadinessFacts) {
  return readinessRequirements(targetStatus, facts).filter((requirement) => requirement.severity === "blocker" && !requirement.met);
}

export function factsFromInventoryItem(item: InventoryItem, profile?: ItemProfile | null): InventoryReadinessFacts {
  return {
    profile,
    title: item.title,
    description: item.description,
    category: item.category,
    condition: item.condition,
    functionalStatus: item.functionalStatus,
    completenessStatus: item.completenessStatus,
    authenticityStatus: item.authenticityStatus,
    packagingStatus: item.packagingStatus,
    testingNotes: item.testingNotes,
    estimatedSalePrice: item.estimatedSalePrice,
    photoCount: item.photos?.length ?? 0,
    componentCount: item.bundleComponentCount ?? item.bundleComponents?.length ?? 0,
    isBundle: item.isBundle,
  };
}
