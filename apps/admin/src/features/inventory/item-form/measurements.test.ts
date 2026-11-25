import { describe, expect, it } from "vitest";
import { readMeasurements, writeMeasurements } from "./measurements";

const shirt = [
  { key: "pitToPit", label: "Pit to pit" },
  { key: "length", label: "Length" },
];

describe("measurements", () => {
  it("reads stored numbers and the unit, ignoring junk", () => {
    expect(readMeasurements('{"unit":"cm","pitToPit":50,"bad":{}}')).toEqual({ unit: "cm", values: { pitToPit: "50" } });
    expect(readMeasurements("nope")).toEqual({ unit: "in", values: {} });
  });

  it("writes only the profile's positive numbers", () => {
    expect(writeMeasurements(shirt, { unit: "in", values: { pitToPit: "19.5", length: "-1", sleeve: "8" } })).toBe('{"unit":"in","pitToPit":19.5}');
  });

  it("writes an empty object when nothing is measured, so clearing reaches the API", () => {
    expect(writeMeasurements(shirt, { unit: "cm", values: { pitToPit: " " } })).toBe("{}");
  });
});
