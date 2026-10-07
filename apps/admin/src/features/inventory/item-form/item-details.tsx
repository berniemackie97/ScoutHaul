"use client";

import { useEffect, useState } from "react";
import type { components } from "@overhaul/api-client";
import type { FieldErrors, FormValues } from "@/lib/form-state";
import { readListInput } from "@/lib/list-input";
import {
  attributeInputName,
  fieldSpec,
  isListAttribute,
  pickAttributes,
  readAttributes,
  type AttributeValues,
  type ItemAttribute,
  type ItemField,
  type ItemProfile,
} from "../item-profiles";
import { matchOption, matchOptions, MultiOptionField, OptionField, SelectField, TextField } from "./controls";
import { AUTHENTICITY, COMPLETENESS, FUNCTIONAL, PACKAGING } from "./options";

type ReferenceData = components["schemas"]["InventoryReferenceDataDto"];

/** The stored values the details start from; a failed save's `values` take precedence. */
export type ItemDetailValues = {
  brand?: string | null;
  model?: string | null;
  colors?: string[] | null;
  sizeSystem?: string | null;
  sizeValue?: string | null;
  functionalStatus?: string | null;
  completenessStatus?: string | null;
  authenticityStatus?: string | null;
  packagingStatus?: string | null;
  manufacturerPartNumber?: string | null;
  serialNumber?: string | null;
  manufactureYear?: number | null;
  countryOfOrigin?: string | null;
  attributesJson?: string | null;
};

/**
 * Everything about an item that depends on what kind of item it is. The category picks a profile, and the profile
 * decides which of these fields exist and what they are called — the same table the API enforces on save.
 */
export function ItemDetails({
  profile,
  category,
  reference,
  initial,
  values,
  errors = {},
  formKey,
  onEdit,
}: {
  profile: ItemProfile;
  category: string;
  reference: ReferenceData;
  initial: ItemDetailValues;
  values?: FormValues;
  errors?: FieldErrors;
  formKey?: string;
  /** Called when a searchable field changes; those post through hidden inputs that raise no form event. */
  onEdit?: () => void;
}) {
  const start = (name: keyof ItemDetailValues) => values?.[name] ?? (initial[name] as string | null | undefined) ?? "";
  const [brand, setBrand] = useState(() => start("brand"));
  const [model, setModel] = useState(() => start("model"));
  const [colors, setColors] = useState<string[]>(() => (values?.colors !== undefined ? (readListInput(values.colors) ?? []) : (initial.colors ?? [])));
  const [sizeSystem, setSizeSystem] = useState(() => start("sizeSystem"));
  const [sizeValue, setSizeValue] = useState(() => start("sizeValue"));

  // A failed save hands back what was typed under a new form key; take it.
  useEffect(() => {
    if (!values) return;
    setBrand(values.brand ?? "");
    setModel(values.model ?? "");
    setColors(readListInput(values.colors) ?? []);
    setSizeSystem(values.sizeSystem ?? "");
    setSizeValue(values.sizeValue ?? "");
  }, [formKey, values]);

  const edit = <T,>(set: (value: T) => void) => (value: T) => {
    set(value);
    onEdit?.();
  };

  const spec = (field: ItemField) => fieldSpec(profile, field);
  const brands = (reference.brands ?? []).filter((option) => !option.parentValue || option.parentValue === category);
  const canonicalBrand = matchOption(brand, reference.brands ?? []);
  const models = (reference.models ?? []).filter((option) => option.parentValue === canonicalBrand);
  const colorOptions = (reference.colors ?? []).filter((option) => option.value && !option.isLegacy).map((option) => option.value!);
  const sizeSystems = (reference.sizeSystems ?? []).filter((option) => profile.sizeSystems?.includes(option.value ?? ""));
  const sizes = (reference.sizes ?? []).filter((option) => option.parentValue === sizeSystem);
  const attributes: AttributeValues = values ? pickAttributes(Object.entries(values)) : readAttributes(initial.attributesJson);
  const statuses = [
    { field: "FunctionalStatus", name: "functionalStatus", choices: FUNCTIONAL },
    { field: "CompletenessStatus", name: "completenessStatus", choices: COMPLETENESS },
    { field: "AuthenticityStatus", name: "authenticityStatus", choices: AUTHENTICITY },
    { field: "PackagingStatus", name: "packagingStatus", choices: PACKAGING },
  ] as const;
  const shownStatuses = statuses.filter((status) => spec(status.field));

  return (
    <div className="space-y-6">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <OptionField
          label={profile.brandLabel ?? "Brand"}
          name="brand"
          value={matchOption(brand, reference.brands ?? [], true)}
          options={brands}
          onChange={edit((value: string) => {
            setBrand(value);
            setModel("");
          })}
          error={errors.brand}
          custom
          placeholder="Search or type a brand…"
        />
        {spec("Model") && (
          <OptionField
            label={spec("Model")!.label ?? "Model"}
            name="model"
            value={matchOption(model, models, true)}
            options={models}
            onChange={edit(setModel)}
            error={errors.model}
            custom
            placeholder="Search or type…"
          />
        )}
        {spec("Color") && (
          <MultiOptionField label="Colors" name="colors" values={matchOptions(colors, colorOptions)} options={colorOptions} max={COLOR_LIMIT} onChange={edit(setColors)} error={errors.colors} />
        )}
        {spec("Size") && (
          <>
            <OptionField
              label="Size system"
              name="sizeSystem"
              value={matchOption(sizeSystem, sizeSystems)}
              options={sizeSystems}
              onChange={edit((value: string) => {
                setSizeSystem(value);
                setSizeValue("");
              })}
              error={errors.sizeSystem}
            />
            <OptionField
              label="Size"
              name="sizeValue"
              value={matchOption(sizeValue, sizes)}
              options={sizes}
              onChange={edit(setSizeValue)}
              disabled={!sizeSystem}
              error={errors.sizeValue}
              placeholder={sizeSystem ? "Search sizes…" : "Choose a size system first"}
            />
          </>
        )}
        {(profile.attributes ?? []).map((attribute) => (
          <AttributeField key={`${profile.key}:${attribute.key}:${formKey ?? "initial"}`} attribute={attribute} initial={attributes[attribute.key!]} onEdit={onEdit} />
        ))}
        {spec("ManufactureYear") && (
          <TextField
            label={spec("ManufactureYear")!.label ?? "Year made"}
            name="manufactureYear"
            type="number"
            min={1800}
            max={2200}
            step="1"
            defaultValue={values?.manufactureYear ?? initial.manufactureYear}
            error={errors.manufactureYear}
            formKey={formKey}
          />
        )}
        {spec("ManufacturerPartNumber") && (
          <TextField
            label={spec("ManufacturerPartNumber")!.label ?? "Part number"}
            name="manufacturerPartNumber"
            defaultValue={values?.manufacturerPartNumber ?? initial.manufacturerPartNumber}
            error={errors.manufacturerPartNumber}
            formKey={formKey}
          />
        )}
        {spec("SerialNumber") && (
          <TextField
            label={spec("SerialNumber")!.label ?? "Serial number"}
            name="serialNumber"
            defaultValue={values?.serialNumber ?? initial.serialNumber}
            error={errors.serialNumber}
            formKey={formKey}
          />
        )}
        {spec("CountryOfOrigin") && (
          <CountryField initial={values?.countryOfOrigin ?? initial.countryOfOrigin ?? ""} reference={reference} error={errors.countryOfOrigin} onEdit={onEdit} />
        )}
      </div>

      {shownStatuses.length > 0 && (
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {shownStatuses.map((status) => (
            <SelectField
              key={status.name}
              label={spec(status.field)!.label ?? status.name}
              name={status.name}
              choices={status.choices}
              defaultValue={values?.[status.name] ?? initial[status.name]}
              error={errors[status.name]}
              formKey={formKey}
            />
          ))}
        </div>
      )}

      {profile.key === "general" && (
        <p className="text-muted-foreground text-sm">Choose a category to see the details that kind of item has.</p>
      )}
    </div>
  );
}

/** The API allows two colors; past that an item is Multicolor or Patterned. */
const COLOR_LIMIT = 2;

/** One profile detail: free text, a searchable pick from its list, or several picks for a list such as materials. */
function AttributeField({ attribute, initial, onEdit }: { attribute: ItemAttribute; initial: string | string[] | undefined; onEdit?: () => void }) {
  const label = attribute.label ?? attribute.key!;
  const options = attribute.options ?? [];
  const [values, setValues] = useState(() => matchOptions(typeof initial === "string" ? [initial] : initial, options));
  const [value, setValue] = useState(() => (typeof initial === "string" ? initial : (initial?.[0] ?? "")));
  const changed = <T,>(set: (next: T) => void) => (next: T) => {
    set(next);
    onEdit?.();
  };

  if (isListAttribute(attribute)) {
    return <MultiOptionField label={label} name={attributeInputName(attribute)} values={values} options={options} max={attribute.maxValues ?? 1} onChange={changed(setValues)} />;
  }

  if (options.length > 0) {
    const choices = options.map((option) => ({ value: option, label: option }));
    return <OptionField label={label} name={attributeInputName(attribute)} value={matchOption(value, choices)} options={choices} onChange={changed(setValue)} />;
  }

  return <TextField label={label} name={attributeInputName(attribute)} defaultValue={value} />;
}

function CountryField({ initial, reference, error, onEdit }: { initial: string; reference: ReferenceData; error?: string; onEdit?: () => void }) {
  const [country, setCountry] = useState(initial);
  return (
    <OptionField
      label="Country of origin"
      name="countryOfOrigin"
      value={matchOption(country, reference.countries ?? [])}
      options={reference.countries ?? []}
      onChange={(value) => {
        setCountry(value);
        onEdit?.();
      }}
      error={error}
    />
  );
}
