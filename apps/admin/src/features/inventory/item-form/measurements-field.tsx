"use client";

import { useEffect, useState } from "react";
import { Input, Label } from "@overhaul/ui";
import { ChoiceGroup } from "@/components/choice-group";
import { readMeasurements, writeMeasurements, type ItemMeasurement, type MeasurementUnit } from "./measurements";

/** Flat-lay measurements for the kinds of item buyers filter by them. Posts one JSON object under `measurementsJson`. */
export function MeasurementsField({ measurements, initial, formKey, onEdit }: {
  measurements: readonly ItemMeasurement[];
  initial: string | null | undefined;
  formKey?: string;
  onEdit?: () => void;
}) {
  const [state, setState] = useState(() => readMeasurements(initial));
  useEffect(() => setState(readMeasurements(initial)), [formKey, initial]);
  const edit = (next: typeof state) => {
    setState(next);
    onEdit?.();
  };

  return (
    <div className="space-y-4">
      <input type="hidden" name="measurementsJson" value={writeMeasurements(measurements, state)} />
      <div className="flex flex-wrap items-center gap-3">
        <span className="text-sm font-medium">Unit</span>
        <ChoiceGroup
          label="Measurement unit"
          name="measurementUnit"
          variant="segmented"
          choices={[{ value: "in", label: "in" }, { value: "cm", label: "cm" }]}
          value={state.unit}
          onChange={(unit) => edit({ ...state, unit: unit as MeasurementUnit })}
        />
      </div>
      <div className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        {measurements.map((measurement) => {
          const key = measurement.key ?? "";
          const id = `measure-${key}`;
          return (
            <div key={key} className="space-y-2">
              <Label htmlFor={id}>{measurement.label}</Label>
              <div className="flex items-center overflow-hidden rounded-md border focus-within:ring-2 focus-within:ring-ring">
                <Input
                  id={id}
                  type="number"
                  inputMode="decimal"
                  min={0}
                  step="0.25"
                  className="border-0 shadow-none tabular-nums focus-visible:ring-0"
                  value={state.values[key] ?? ""}
                  onChange={(event) => edit({ ...state, values: { ...state.values, [key]: event.target.value } })}
                />
                <span className="bg-muted text-muted-foreground border-l px-2.5 py-2 text-xs">{state.unit}</span>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
