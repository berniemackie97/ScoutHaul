import { describe, expect, it } from "vitest";
import { changeTone, describeChange } from "./trading";

describe("describeChange", () => {
  it("shows a rounded percentage with a direction", () => {
    expect(describeChange({ current: 112, previous: 100, percentChange: 12.4 })).toBe("+12%");
    expect(describeChange({ current: 92, previous: 100, percentChange: -8 })).toBe("−8%");
  });

  it("calls something from nothing new", () => {
    expect(describeChange({ current: 50, previous: 0, percentChange: null })).toBe("new");
  });

  it("does not call nothing-then-nothing new", () => {
    expect(describeChange({ current: 0, previous: 0, percentChange: null })).toBe("—");
  });

  it("says so when nothing moved", () => {
    expect(describeChange({ current: 100, previous: 100, percentChange: 0.2 })).toBe("no change");
  });
});

describe("changeTone", () => {
  it("treats more sales as good news and more expenses as bad", () => {
    expect(changeTone({ current: 120, previous: 100, percentChange: 20 })).toBe("up");
    expect(changeTone({ current: 120, previous: 100, percentChange: 20 }, false)).toBe("down");
  });

  it("calls a sub-cent difference flat", () => {
    expect(changeTone({ current: 100.001, previous: 100, percentChange: 0 })).toBe("flat");
  });
});
