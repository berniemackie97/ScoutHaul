import { ApiError } from '../api/apiError';
import { isWorthQueueing } from './offlineWrites';

const failure = (reason: Parameters<typeof makeError>[0], status?: number) => makeError(reason, status);

function makeError(
  reason: 'offline' | 'network' | 'timeout' | 'unauthenticated' | 'http',
  status?: number,
) {
  return new ApiError({ reason, message: 'x', status: status ?? null });
}

describe('isWorthQueueing', () => {
  it('keeps writes that never reached the server', () => {
    expect(isWorthQueueing(failure('offline'))).toBe(true);
    expect(isWorthQueueing(failure('network'))).toBe(true);
    expect(isWorthQueueing(failure('timeout'))).toBe(true);
  });

  it('keeps writes the server blamed on itself', () => {
    expect(isWorthQueueing(failure('http', 500))).toBe(true);
    expect(isWorthQueueing(failure('http', 503))).toBe(true);
    expect(isWorthQueueing(failure('http', 408))).toBe(true);
    expect(isWorthQueueing(failure('http', 429))).toBe(true);
  });

  it('drops writes that will be rejected the same way every time', () => {
    // Queuing these builds a backlog that can never drain, and hides the real problem.
    expect(isWorthQueueing(failure('http', 400))).toBe(false);
    expect(isWorthQueueing(failure('http', 403))).toBe(false);
    expect(isWorthQueueing(failure('http', 404))).toBe(false);
    expect(isWorthQueueing(failure('http', 409))).toBe(false);
    expect(isWorthQueueing(failure('http', 422))).toBe(false);
    expect(isWorthQueueing(failure('unauthenticated'))).toBe(false);
  });

  it('does not queue on something that is not an API failure at all', () => {
    expect(isWorthQueueing(new Error('boom'))).toBe(false);
    expect(isWorthQueueing('boom')).toBe(false);
    expect(isWorthQueueing(undefined)).toBe(false);
  });
});
