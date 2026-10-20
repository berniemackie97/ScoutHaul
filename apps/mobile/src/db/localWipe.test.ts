import { getDatabase } from './database';
import { clearLocalWorkspace } from './localWipe';

jest.mock('./database', () => ({ getDatabase: jest.fn() }));
jest.mock('react-native', () => ({ Platform: { OS: 'ios' } }));
const mockDeleted: string[] = [];
jest.mock('expo-file-system', () => ({
  File: class {
    mockUri: string;
    constructor(mockPath: string) {
      this.mockUri = mockPath;
    }
    get exists() {
      return true;
    }
    delete() {
      mockDeleted.push(this.mockUri);
    }
  },
}));

it("clears the previous person's queue, drafts, photos and routes in one go, files included", async () => {
  const statements: string[] = [];
  (getDatabase as jest.Mock).mockResolvedValue({
    getAllAsync: async () => [{ localUri: 'file:///a.jpg' }, { localUri: 'file:///b.jpg' }],
    withTransactionAsync: async (work: () => Promise<void>) => work(),
    execAsync: async (sql: string) => void statements.push(sql),
  });

  await clearLocalWorkspace();

  expect(mockDeleted).toEqual(['file:///a.jpg', 'file:///b.jpg']);
  expect(statements).toEqual([
    'delete from item_photos;',
    'delete from sync_queue;',
    'delete from pending_comps;',
    'delete from item_drafts;',
    'delete from pending_trip_entries;',
    'delete from route_snapshots;',
  ]);
});
