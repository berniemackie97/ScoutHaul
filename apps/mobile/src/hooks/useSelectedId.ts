import { useState } from 'react';

/**
 * Resolves which id is selected, given what the user picked and what is currently selectable.
 *
 * Split out from the hook so the rule can be tested directly: a pick wins only while it is still
 * one of the options, and otherwise the caller's fallback applies.
 */
export function resolveSelectedId(
  picked: string | null,
  validIds: readonly string[],
  fallbackId: string | null,
): string | null {
  return picked !== null && validIds.includes(picked) ? picked : fallbackId;
}

/**
 * A single-choice selection that follows its option list.
 *
 * The list this selects from usually arrives after the first render (teams, members and roles are
 * all fetched). Seeding `useState` from those props and then patching it up in an effect renders
 * twice on every load, and — because the patch-up guard only fires when nothing is selected — it
 * leaves a now-missing id selected whenever the list changes underneath it. Deriving the selection
 * during render fixes both: nothing is stored until the user actually picks, and a pick that is no
 * longer in the list falls back instead of pointing at something that isn't there.
 *
 * `validIds` and `fallbackId` should be memoized by the caller; this hook does not re-derive them.
 */
export function useSelectedId(
  validIds: readonly string[],
  fallbackId: string | null,
): [string | null, (id: string | null) => void] {
  const [picked, setPicked] = useState<string | null>(null);
  return [resolveSelectedId(picked, validIds, fallbackId), setPicked];
}
