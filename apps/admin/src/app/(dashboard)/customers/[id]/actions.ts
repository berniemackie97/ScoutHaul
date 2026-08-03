"use server";

import { revalidatePath } from "next/cache";
import { getApiClient } from "@/lib/api";

export type CustomerActionResult = { ok: boolean; message: string };

function errorMessage(error: unknown, fallback: string): string {
  if (typeof error === "string" && error.trim()) return error;
  if (error && typeof error === "object") {
    for (const key of ["detail", "title", "message"] as const) {
      const value = (error as Record<string, unknown>)[key];
      if (typeof value === "string" && value.trim()) return value;
    }
  }
  return fallback;
}

/**
 * Corrects a customer's details. Customers arrive from checkout and from marketplace imports spelled
 * however the platform spelled them, so this is the only way to fix a wrong email or name.
 */
export async function updateCustomer(
  id: string,
  _previous: CustomerActionResult,
  formData: FormData,
): Promise<CustomerActionResult> {
  const text = (name: string) => {
    const value = formData.get(name);
    return typeof value === "string" && value.trim() ? value.trim() : null;
  };

  const api = await getApiClient();
  const result = await api.PUT("/api/customers/{id}", {
    params: { path: { id } },
    body: {
      displayName: text("displayName"),
      email: text("email"),
      phone: text("phone"),
      acceptsMarketing: formData.get("acceptsMarketing") === "on",
    },
  });

  if (!result.data) {
    // The API explains collisions ("another customer already uses that email") and bad addresses;
    // surface that rather than a generic failure, because the operator can act on it.
    return { ok: false, message: errorMessage(result.error, "The customer could not be updated.") };
  }

  revalidatePath("/customers");
  revalidatePath(`/customers/${id}`);
  return { ok: true, message: "Customer updated." };
}
