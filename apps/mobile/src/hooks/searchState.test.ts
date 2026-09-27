import { ApiError } from '../api/apiError';
import { emptyMessage, initialSearchState, searchReducer, type SearchState } from './searchState';

type Item = { id: string };
const reduce = (state: SearchState<Item>, action: Parameters<typeof searchReducer<Item>>[1]) => searchReducer(state, action);
const searching = (query: string) => reduce(initialSearchState<Item>(), { type: 'search', query });

describe('a search as someone types', () => {
  it('says it is searching, and keeps the last matches up meanwhile so the list does not flash empty', () => {
    const shown = reduce(searching('nik'), { type: 'resolved', query: 'nik', results: [{ id: 'a' }] });
    const next = reduce(shown, { type: 'search', query: 'nikon' });

    expect(next).toMatchObject({ status: 'searching', query: 'nikon', results: [{ id: 'a' }] });
  });

  it('drops a slow answer to an earlier query, so it never replaces what is in the box now', () => {
    const state = reduce(searching('nik'), { type: 'search', query: 'nikon' });

    expect(reduce(state, { type: 'resolved', query: 'nik', results: [{ id: 'stale' }] })).toBe(state);
    expect(reduce(state, { type: 'rejected', query: 'nik', error: new Error('late') })).toBe(state);
  });

  it('shows the answer to the latest query', () => {
    const next = reduce(searching('nikon'), { type: 'resolved', query: 'nikon', results: [{ id: 'f3' }] });

    expect(next).toMatchObject({ status: 'ready', results: [{ id: 'f3' }], error: null });
  });
});

describe('a failed search', () => {
  it('says why in the words the client chose, and knows when the phone is offline', () => {
    const offline = new ApiError({ reason: 'offline', message: "You're offline. This will work again once you have a connection." });
    const next = reduce(searching('nikon'), { type: 'rejected', query: 'nikon', error: offline });

    expect(next).toMatchObject({ status: 'failed', failure: 'offline', results: [] });
    expect(next.error).toContain("You're offline");
  });

  it('never shows an empty message for something that was not an error with words', () => {
    expect(reduce(searching('x'), { type: 'rejected', query: 'x', error: 'boom' }).error).toBe('The search could not be run.');
  });
});

describe('the empty state', () => {
  it('names what was searched for when nothing matched', () => {
    const state = reduce(searching('nikon'), { type: 'resolved', query: 'nikon', results: [] });

    expect(emptyMessage(state, 'unsold item')).toBe('No unsold item matches “nikon”.');
  });

  it('says there is nothing at all when nothing was typed and nothing came back', () => {
    const state = reduce(searching(''), { type: 'resolved', query: '', results: [] });

    expect(emptyMessage(state, 'unsold item')).toBe('No unsold item to choose from yet.');
  });

  it('stays quiet while searching, after a failure, or when there are matches', () => {
    expect(emptyMessage(searching('nikon'), 'unsold item')).toBeNull();
    expect(emptyMessage(reduce(searching('n'), { type: 'rejected', query: 'n', error: new Error('down') }), 'unsold item')).toBeNull();
    expect(emptyMessage(reduce(searching('n'), { type: 'resolved', query: 'n', results: [{ id: 'a' }] }), 'unsold item')).toBeNull();
  });
});
