import type { components } from "@overhaul/api-client";

type Schemas = components["schemas"];
type Choices<T extends string> = ReadonlyArray<readonly [T, string]>;

// One list per enum, shared by the item editor and the item creator.

export const OWNERSHIP: Choices<Schemas["InventoryOwnershipType"]> = [
  ["Owned", "Owned"],
  ["Consigned", "Consigned"],
  ["CustomerRepair", "Customer's item, in for repair"],
  ["VendorManaged", "Vendor-managed"],
];

export const FUNCTIONAL: Choices<Schemas["InventoryFunctionalStatus"]> = [
  ["Unknown", "Not recorded"],
  ["Untested", "Untested"],
  ["FullyFunctional", "Fully working"],
  ["FunctionalWithIssues", "Works, with issues"],
  ["Intermittent", "Works intermittently"],
  ["NonFunctional", "Not working"],
  ["PartsOnly", "For parts only"],
];

export const COMPLETENESS: Choices<Schemas["InventoryCompletenessStatus"]> = [
  ["Unknown", "Not recorded"],
  ["Complete", "Complete"],
  ["MissingAccessories", "Missing accessories"],
  ["MissingParts", "Missing parts"],
  ["Incomplete", "Incomplete set"],
];

export const AUTHENTICITY: Choices<Schemas["InventoryAuthenticityStatus"]> = [
  ["NotAssessed", "Not assessed"],
  ["Authentic", "Authenticated"],
  ["LikelyAuthentic", "Likely authentic"],
  ["NeedsVerification", "Needs verification"],
  ["Replica", "Replica (disclosed)"],
  ["Counterfeit", "Counterfeit — do not list"],
];

export const PACKAGING: Choices<Schemas["InventoryPackagingStatus"]> = [
  ["NotApplicable", "Not recorded"],
  ["FactorySealed", "Factory sealed"],
  ["OriginalExcellent", "Original packaging, excellent"],
  ["OriginalWorn", "Original packaging, worn"],
  ["OriginalDamaged", "Original packaging, damaged"],
  ["ReplacementPackaging", "Replacement packaging"],
  ["LooseNoPackaging", "Loose, no packaging"],
];

export const DIMENSION_UNITS: Choices<Schemas["DimensionUnit"]> = [
  ["Inches", "in"],
  ["Centimeters", "cm"],
];

export const WEIGHT_UNITS: Choices<Schemas["WeightUnit"]> = [
  ["Ounces", "oz"],
  ["Pounds", "lb"],
  ["Grams", "g"],
  ["Kilograms", "kg"],
];
