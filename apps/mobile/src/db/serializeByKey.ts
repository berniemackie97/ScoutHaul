/**
 * Runs work for the same key one at a time, in the order it was asked for.
 *
 * A read-modify-write across `await` points is not safe just because JavaScript is single-threaded:
 * two calls interleave at the awaits, both read the same value, and the second write silently
 * discards the first. On the route screen that meant two quick taps offline — marking a stop
 * visited, then logging a find — and one of them was simply gone.
 *
 * Different keys still run in parallel; only work touching the same thing queues behind itself.
 */
const chains = new Map<string, Promise<unknown>>();

export function serializeByKey<T>(key: string, work: () => Promise<T>): Promise<T> {
  const previous = chains.get(key) ?? Promise.resolve();

  // Chain off the previous call settling either way. Chaining off success alone would stall every
  // later call behind one failure for the rest of the session.
  const result = previous.then(work, work);

  const link: Promise<unknown> = result.catch(() => undefined);
  chains.set(key, link);

  // Drop the key once this is the last call waiting on it, so the map cannot grow without bound.
  void link.then(() => {
    if (chains.get(key) === link) chains.delete(key);
  });

  return result;
}

/** Forgets every queue. For tests; nothing in the app needs it. */
export function clearSerializedKeys(): void {
  chains.clear();
}
