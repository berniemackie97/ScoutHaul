import { describe, expect, it } from "vitest";
import type { ItemProfile } from "../item-profiles";
import { conditionChoices } from "./condition-scale";

const options = [
  { value: "Factory sealed", label: "Factory sealed", hint: "Unopened" },
  { value: "New with tags", label: "New with tags", hint: "Unworn, tags attached" },
  { value: "Good", label: "Good", hint: "Visible wear" },
  { value: "For parts", label: "For parts / not working", hint: "Not working" },
  { value: "Store graded A", label: "Store graded A" },
];
const clothing = { key: "apparel", label: "Clothing", conditions: ["New with tags", "Good"] } as ItemProfile;
const general = { key: "general", label: "General", conditions: [] } as ItemProfile;

describe("condition scales", () => {
  it("offers a kind its own scale in order, plus the organization's own conditions", () => {
    expect(conditionChoices(clothing, options, "").map((choice) => choice.value)).toEqual(["New with tags", "Good", "Store graded A"]);
  });

  it("offers everything before a category is chosen", () => {
    expect(conditionChoices(general, options, "")).toHaveLength(5);
  });

  it("keeps a stored condition that is off the scale, marked, so saving never drops it", () => {
    const choices = conditionChoices(clothing, options, "for parts");
    expect(choices.at(-1)).toEqual({ value: "For parts", label: "For parts / not working", hint: "Not on the Clothing scale" });
  });

  it("keeps a stored value the reference list no longer has", () => {
    expect(conditionChoices(clothing, options, "Mint").at(-1)?.value).toBe("Mint");
  });
});
