import { api } from '../api';
import { ApiError } from '../api/apiError';
import type { CreateCompRequest } from '../api/marketplace';
import { enqueueComp } from '../db/localStore';
import { isWorthQueueing } from './offlineWrites';

/**
 * What happened to a comp the user asked to save.
 *
 * Saving and queuing are both successes — the comp is kept either way — and only `rejected` means
 * the user has to do something. Screens used to render the queued case through the same red error
 * banner as a real failure, which read as "this did not work" and invited a retry that saved it twice.
 */
export type CompSaveOutcome =
  | { status: 'saved' }
  | { status: 'queued'; because: 'offline' | 'unreachable' }
  | { status: 'rejected'; message: string };


/**
 * Saves a comp, keeping it locally when the server cannot be reached.
 *
 * This is the only way a comp should be written. There used to be three: comp-capture checked
 * connectivity and queued, while the scanner and its results list called the API directly with no
 * fallback — on the two screens most used mid-aisle, where the signal is worst, so those comps were
 * simply lost.
 */
export async function saveComp(body: CreateCompRequest): Promise<CompSaveOutcome> {
  try {
    await api.addComp(body);
    return { status: 'saved' };
  } catch (error) {
    if (!isWorthQueueing(error)) {
      return {
        status: 'rejected',
        message: error instanceof Error ? error.message : 'Could not save this comp.',
      };
    }

    await enqueueComp(body);
    return {
      status: 'queued',
      because: error instanceof ApiError && error.reason === 'offline' ? 'offline' : 'unreachable',
    };
  }
}

/** The sentence to show the user for an outcome. */
export function describeCompOutcome(outcome: CompSaveOutcome): string {
  switch (outcome.status) {
    case 'saved':
      return 'Comp saved.';
    case 'queued':
      return outcome.because === 'offline'
        ? "Saved on this phone. It'll sync when you're back online."
        : "Saved on this phone. The server didn't answer, so it'll sync later.";
    case 'rejected':
      return outcome.message;
  }
}
