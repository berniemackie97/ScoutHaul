import { resolveSelectedId } from './useSelectedId';

describe('resolveSelectedId', () => {
  it('falls back when nothing has been picked', () => {
    expect(resolveSelectedId(null, ['a', 'b'], 'a')).toBe('a');
  });

  it('returns null when there is nothing to fall back to', () => {
    expect(resolveSelectedId(null, [], null)).toBeNull();
  });

  it('keeps an explicit pick that is still selectable', () => {
    expect(resolveSelectedId('b', ['a', 'b'], 'a')).toBe('b');
  });

  it('drops a pick that is no longer in the list', () => {
    // 'b' was deleted elsewhere; the selection must not keep pointing at it.
    expect(resolveSelectedId('b', ['a', 'c'], 'a')).toBe('a');
  });

  it('falls back while the options are still loading', () => {
    expect(resolveSelectedId('b', [], null)).toBeNull();
  });
});
