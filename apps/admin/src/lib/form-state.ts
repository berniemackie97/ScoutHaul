export type FormValues = Record<string, string>;
export type FieldErrors = Record<string, string>;

export type FailedFormState = {
  values?: FormValues;
  fieldErrors?: FieldErrors;
  formKey?: string;
};

export function valuesFromFormData(formData: FormData): FormValues {
  const values: FormValues = {};
  for (const [key, value] of formData.entries()) {
    if (typeof value === "string") {
      values[key] = value;
    }
  }
  return values;
}

export function failedFormState(message: string, formData: FormData, fieldErrors: FieldErrors = {}): FailedFormState & {
  ok: false;
  message: string;
} {
  return {
    ok: false,
    message,
    values: valuesFromFormData(formData),
    fieldErrors,
    formKey: `${Date.now()}`,
  };
}
