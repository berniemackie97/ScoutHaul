import { describe, expect, it } from "vitest";
import { writeListInput } from "@/lib/list-input";
import { attributeInputName, attributesFromForm, pickAttributes, readAttributes, type ItemAttribute } from "./item-profiles";

const material: ItemAttribute = { key: "material", label: "Material", options: ["Cotton", "Silk"], maxValues: 3 };
const department: ItemAttribute = { key: "department", label: "Department", options: ["Women", "Men"], maxValues: 1 };

describe("item profile details", () => {
  it("posts a list detail as one list field and a single pick as plain text", () => {
    expect(attributeInputName(material)).toBe("attrs.material");
    expect(attributeInputName(department)).toBe("attr.department");
  });

  it("reads stored text and lists, ignoring anything else", () => {
    expect(readAttributes('{"material":["Silk","Cotton"],"department":"Women","bad":3}')).toEqual({ material: ["Silk", "Cotton"], department: "Women" });
    expect(readAttributes("not json")).toEqual({});
  });

  it("turns a posted form back into the JSON the API stores, dropping empty answers", () => {
    const form = new FormData();
    form.set("attrs.material", writeListInput(["Silk", "Cotton"]));
    form.set("attrs.lining", writeListInput([]));
    form.set("attr.department", " Women ");
    form.set("attr.grade", "   ");
    form.set("title", "Blouse");

    expect(attributesFromForm(form)).toBe('{"material":["Silk","Cotton"],"department":"Women"}');
    expect(pickAttributes([["attrs.material", "not a list"]])).toEqual({});
  });
});
