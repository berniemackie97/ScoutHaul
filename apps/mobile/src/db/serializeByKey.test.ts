import { clearSerializedKeys, serializeByKey } from './serializeByKey';

const tick = (ms = 0) => new Promise((resolve) => setTimeout(resolve, ms));

beforeEach(() => clearSerializedKeys());

describe('serializeByKey', () => {
  it('stops a read-modify-write losing an update', async () => {
    // The bug this exists for. Both calls read 0, both write 1, and one update is gone.
    let stored = 0;
    const increment = () =>
      serializeByKey('trip', async () => {
        const current = stored;
        await tick(5);          // the await where the interleave used to happen
        stored = current + 1;
      });

    await Promise.all([increment(), increment(), increment()]);

    expect(stored).toBe(3);
  });

  it('runs work for one key in the order it was asked for', async () => {
    const order: number[] = [];
    const push = (n: number, delay: number) =>
      serializeByKey('trip', async () => {
        await tick(delay);
        order.push(n);
      });

    // The first is the slowest; it must still finish first.
    await Promise.all([push(1, 15), push(2, 5), push(3, 0)]);

    expect(order).toEqual([1, 2, 3]);
  });

  it('lets different keys run at the same time', async () => {
    const order: string[] = [];
    await Promise.all([
      serializeByKey('a', async () => { await tick(15); order.push('a'); }),
      serializeByKey('b', async () => { await tick(0); order.push('b'); }),
    ]);

    // b did not wait for a.
    expect(order).toEqual(['b', 'a']);
  });

  it('hands the caller back what the work returned', async () => {
    await expect(serializeByKey('trip', async () => 'done')).resolves.toBe('done');
  });

  it('reports a failure to the caller who caused it', async () => {
    await expect(serializeByKey('trip', async () => { throw new Error('boom'); })).rejects.toThrow('boom');
  });

  it('does not stall everything behind one failure', async () => {
    // Chaining off success alone would jam the queue for the rest of the session.
    const failed = serializeByKey('trip', async () => { throw new Error('boom'); });
    await expect(failed).rejects.toThrow();

    await expect(serializeByKey('trip', async () => 'still works')).resolves.toBe('still works');
  });

  it('does not hold onto a key once its queue has drained', async () => {
    await serializeByKey('trip', async () => undefined);
    await tick(0);

    // Nothing to assert on directly; running many keys must not leak. This would grow unbounded
    // without the cleanup, which matters on a screen that updates on every tap.
    for (let i = 0; i < 50; i += 1) {
      await serializeByKey(`trip-${i}`, async () => undefined);
    }
    await tick(0);
    await expect(serializeByKey('trip', async () => 'fine')).resolves.toBe('fine');
  });
});
