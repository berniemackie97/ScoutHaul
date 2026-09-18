import { describe, expect, it } from "vitest";
import { countsSentence, coverage, fileSize, importable, issueCounts, recordsLabel, runStatus } from "./import-model";

describe("runStatus", () => {
  it("names every status in the operator's words", () => {
    expect(runStatus("Succeeded")).toEqual({ label: "Imported", tone: "success" });
    expect(runStatus("CompletedWithIssues").tone).toBe("warning");
    expect(runStatus("Failed").tone).toBe("danger");
  });

  it("does not hide a status it does not know", () => {
    expect(runStatus("Paused").label).toBe("Paused");
  });
});

describe("countsSentence", () => {
  it("lists only what happened", () => {
    expect(countsSentence({ created: 3, updated: 0, unchanged: 40, rejected: 1 })).toBe("3 new · 40 unchanged · 1 rejected");
  });

  it("says when nothing was imported rather than showing an empty line", () => {
    expect(countsSentence({ created: 0, updated: 0, unchanged: 0, rejected: 0 })).toBe("Nothing to import");
  });
});

describe("coverage", () => {
  it("shows a period within one year without repeating the year", () => {
    expect(coverage("2026-07-01T07:00:00Z", "2026-07-31T23:00:00Z", false)).toBe("Jul 1 – Jul 31, 2026");
  });

  it("shows both years when the period crosses one", () => {
    expect(coverage("2025-12-20T00:00:00Z", "2026-01-05T00:00:00Z", false)).toBe("Dec 20, 2025 – Jan 5, 2026");
  });

  it("describes a snapshot by its date", () => {
    expect(coverage(null, "2026-07-09T07:00:00Z", true)).toBe("as of Jul 9, 2026");
  });

  it("shows a single day once", () => {
    expect(coverage("2026-07-03T20:46:00Z", "2026-07-03T21:00:00Z", false)).toBe("Jul 3, 2026");
  });

  it("has nothing to say when the file does not say", () => {
    expect(coverage(null, null, false)).toBeNull();
  });
});

describe("recordsLabel", () => {
  it.each([
    ["depop.sales", 58, "58 orders"],
    ["ebay.all-orders", 1, "1 order"],
    ["ebay.active-listings", 213, "213 listings"],
    ["poshmark.inventory", 45, "45 listings"],
    ["ebay.listing-sales", 12, "12 sales"],
    ["ebay.listing-quality", 2, "2 quality rows"],
  ])("counts %s in its own terms", (formatKey, records, label) => {
    expect(recordsLabel({ formatKey, records })).toBe(label);
  });
});

describe("fileSize", () => {
  it("reads the way people read file sizes", () => {
    expect(fileSize(512)).toBe("512 B");
    expect(fileSize(3 * 1024 + 200)).toBe("3.2 KB");
    expect(fileSize(2.5 * 1024 * 1024)).toBe("2.5 MB");
  });
});

describe("importable", () => {
  it("keeps only files that can actually be imported", () => {
    const files = [
      { fileName: "a.csv", canImport: true },
      { fileName: "notes.csv", canImport: false },
    ];
    expect(importable(files).map((file) => file.fileName)).toEqual(["a.csv"]);
  });
});

describe("issueCounts", () => {
  it("separates rows that were refused from notes", () => {
    expect(
      issueCounts([
        { severity: "Error", message: "bad date" },
        { severity: "Warning", message: "bundle" },
        { severity: "Warning", message: "refund" },
      ]),
    ).toEqual({ errors: 1, warnings: 2 });
  });
});
