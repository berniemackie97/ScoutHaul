import { ABSENT } from "@overhaul/ui";

/**
 * What the API sends when it refuses a request.
 *
 * RFC 7807 ProblemDetails, with a machine `code` in the extensions. Every endpoint sends this shape
 * now; before it did, the API answered with bare strings, ad-hoc objects and empty bodies, which is
 * why nineteen action files each grew their own copy of the extractor below.
 */
export type ApiProblem = {
  title?: string;
  detail?: string;
  status?: number;
  /** Stable and machine-readable. Branch on this, never on the message. */
  code?: string;
  /** Field-level failures, when the refusal was per-field. */
  errors?: Record<string, string[]>;
};

function asProblem(error: unknown): ApiProblem | null {
  return error && typeof error === "object" ? (error as ApiProblem) : null;
}

/**
 * The sentence to show the operator.
 *
 * A field-level error is the most specific thing available, then the detail, then the title. The
 * fallback exists because an unreachable server produces no body at all.
 */
export function errorMessage(error: unknown, fallback: string): string {
  if (typeof error === "string" && error.trim()) return error;

  const problem = asProblem(error);
  if (!problem) return fallback;

  const fieldError = Object.values(problem.errors ?? {}).flat().find((entry) => entry?.trim());
  if (fieldError) return fieldError;

  for (const value of [problem.detail, problem.title, (problem as { message?: string }).message]) {
    if (typeof value === "string" && value.trim()) return value;
  }
  return fallback;
}

/** The machine reason, when the API gave one. Null for a transport failure, which has no code. */
export function errorCode(error: unknown): string | null {
  const code = asProblem(error)?.code;
  return typeof code === "string" && code.trim() ? code : null;
}

/** Whether a refusal was for a specific, known reason — for branching on one case in particular. */
export function isErrorCode(error: unknown, code: string): boolean {
  return errorCode(error) === code;
}

/** Field-level failures, keyed by field, for putting a message under the input that caused it. */
export function fieldErrors(error: unknown): Record<string, string[]> {
  return asProblem(error)?.errors ?? {};
}

export { ABSENT };
