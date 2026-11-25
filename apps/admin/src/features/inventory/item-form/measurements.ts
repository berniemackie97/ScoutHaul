import type { components } from "@overhaul/api-client";

export type ItemMeasurement = components["schemas"]["ItemMeasurementDto"];
export type MeasurementUnit = "in" | "cm";
export type MeasurementValues = { unit: MeasurementUnit; values: Record<string, string> };

/** The stored measurements as text per key; anything unreadable reads as none, in inches. */
export function readMeasurements(json: string | null | undefined): MeasurementValues {
  const empty: MeasurementValues = { unit: "in", values: {} };
  if (!json) return empty;
  try {
    const parsed: unknown = JSON.parse(json);
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return empty;
    const values: Record<string, string> = {};
    let unit: MeasurementUnit = "in";
    for (const [key, value] of Object.entries(parsed)) {
      if (key === "unit") unit = value === "cm" ? "cm" : "in";
      else if (typeof value === "number" || typeof value === "string") values[key] = String(value);
    }
    return { unit, values };
  } catch {
    return empty;
  }
}

/** The JSON the API stores: the profile's measurements that are positive numbers, with the unit. Always an object, so an emptied set clears. */
export function writeMeasurements(measurements: readonly ItemMeasurement[], state: MeasurementValues): string {
  const out: Record<string, string | number> = { unit: state.unit };
  for (const measurement of measurements) {
    const raw = state.values[measurement.key ?? ""]?.trim();
    const number = raw ? Number(raw) : NaN;
    if (measurement.key && Number.isFinite(number) && number > 0) out[measurement.key] = number;
  }
  return Object.keys(out).length > 1 ? JSON.stringify(out) : "{}";
}
