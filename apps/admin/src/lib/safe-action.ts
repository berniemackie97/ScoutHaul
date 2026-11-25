export type ActionOutcome = { ok: boolean; message: string };

/**
 * Runs a server action from a click and always comes back with an outcome. A dropped connection or an API that cannot
 * be reached makes the action throw; caught here, it reads as a failed action beside the button instead of replacing
 * the whole page with an error screen.
 */
export async function safely<T extends ActionOutcome>(action: () => Promise<T>, failure = "That did not go through. Check the connection and try again."): Promise<T | ActionOutcome> {
  try {
    return await action();
  } catch {
    return { ok: false, message: failure };
  }
}
