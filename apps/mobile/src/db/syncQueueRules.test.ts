import { ApiError } from '../api/apiError';
import { MAX_ATTEMPTS, backoffMs, classifyFailure } from './syncQueueRules';

const at = (reason: 'offline' | 'network' | 'timeout' | 'unauthenticated' | 'http', status?: number) =>
  new ApiError({ reason, message: 'boom', status: status ?? null });

const NOW = new Date('2026-09-03T12:00:00.000Z');

describe('backoffMs', () => {
  it('waits longer after each attempt', () => {
    expect(backoffMs(1)).toBeLessThan(backoffMs(2));
    expect(backoffMs(2)).toBeLessThan(backoffMs(3));
  });

  it('stops growing, so a long-dead row does not schedule itself past the heat death', () => {
    expect(backoffMs(50)).toBe(backoffMs(99));
  });
});

describe('classifyFailure', () => {
  it('pauses the whole queue when the device is offline', () => {
    // Not this row's fault, and every other row would fail identically.
    expect(classifyFailure(at('offline'), 0, NOW)).toEqual({ action: 'pause' });
  });

  it('retries what could not reach the server, after a wait', () => {
    const verdict = classifyFailure(at('network'), 0, NOW);

    expect(verdict.action).toBe('retry');
    if (verdict.action !== 'retry') throw new Error('unreachable');
    expect(verdict.attempts).toBe(1);
    expect(verdict.nextAttemptAt.getTime()).toBeGreaterThan(NOW.getTime());
  });

  it('retries what the server blamed on itself', () => {
    expect(classifyFailure(at('http', 500), 0, NOW).action).toBe('retry');
    expect(classifyFailure(at('http', 503), 0, NOW).action).toBe('retry');
    expect(classifyFailure(at('timeout'), 0, NOW).action).toBe('retry');
  });

  it('gives up immediately on anything the server will reject the same way for ever', () => {
    // Retrying these builds a backlog that can never drain and hides the real problem.
    for (const error of [at('http', 400), at('http', 403), at('http', 404), at('http', 422)]) {
      const verdict = classifyFailure(error, 0, NOW);
      expect(verdict.action).toBe('give-up');
    }
  });

  it('gives up when the user is signed out rather than burning attempts', () => {
    expect(classifyFailure(at('unauthenticated'), 0, NOW).action).toBe('give-up');
  });

  it('gives up once a retryable write has been tried enough times', () => {
    const verdict = classifyFailure(at('network'), MAX_ATTEMPTS - 1, NOW);

    expect(verdict.action).toBe('give-up');
    if (verdict.action !== 'give-up') throw new Error('unreachable');
    expect(verdict.attempts).toBe(MAX_ATTEMPTS);
    expect(verdict.reason).toContain('gave up after');
  });

  it('keeps retrying right up to the last allowed attempt', () => {
    expect(classifyFailure(at('network'), MAX_ATTEMPTS - 2, NOW).action).toBe('retry');
  });

  it('records the server message so it can be shown to the user', () => {
    const verdict = classifyFailure(
      new ApiError({ reason: 'http', status: 400, message: 'Price must be positive.' }),
      0,
      NOW,
    );

    if (verdict.action !== 'give-up') throw new Error('unreachable');
    expect(verdict.reason).toContain('Price must be positive.');
  });

  it('gives up on a plain error, which is not something a retry can fix', () => {
    expect(classifyFailure(new Error('bad json'), 0, NOW).action).toBe('give-up');
  });
});
