import type { LocalItemDraft } from '../domain/types';
import { createLocalId } from '../utils/id';
import { getDatabase } from './database';
import { enqueueOperation } from './syncQueue';

type LocalItemDraftRow = Omit<LocalItemDraft, 'needsResearch'> & {
  needsResearch: number;
};

export async function saveItemDraft(input: Partial<LocalItemDraft> & { title?: string }): Promise<LocalItemDraft> {
  const db = await getDatabase();
  const now = new Date().toISOString();
  const draft: LocalItemDraft = {
    id: input.id ?? createLocalId('draft'),
    title: input.title?.trim() || 'Untitled draft',
    barcode: input.barcode ?? null,
    sellerAskingPrice: input.sellerAskingPrice ?? null,
    condition: input.condition ?? null,
    notes: input.notes ?? null,
    sourceName: input.sourceName ?? null,
    expectedSalePrice: input.expectedSalePrice ?? null,
    needsResearch: input.needsResearch ?? true,
    syncStatus: input.syncStatus ?? 'local',
    createdAt: input.createdAt ?? now,
    updatedAt: now,
  };

  await db.runAsync(
    `insert into item_drafts (
      id, title, barcode, sellerAskingPrice, condition, notes, sourceName, expectedSalePrice, needsResearch, syncStatus, createdAt, updatedAt
    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    on conflict(id) do update set
      title = excluded.title,
      barcode = excluded.barcode,
      sellerAskingPrice = excluded.sellerAskingPrice,
      condition = excluded.condition,
      notes = excluded.notes,
      sourceName = excluded.sourceName,
      expectedSalePrice = excluded.expectedSalePrice,
      needsResearch = excluded.needsResearch,
      syncStatus = excluded.syncStatus,
      updatedAt = excluded.updatedAt`,
    [
      draft.id, draft.title, draft.barcode ?? null, draft.sellerAskingPrice ?? null,
      draft.condition ?? null, draft.notes ?? null, draft.sourceName ?? null,
      draft.expectedSalePrice ?? null, draft.needsResearch ? 1 : 0,
      draft.syncStatus, draft.createdAt, draft.updatedAt,
    ],
  );

  return draft;
}

export async function listItemDrafts(): Promise<LocalItemDraft[]> {
  const db = await getDatabase();
  const rows = await db.getAllAsync<LocalItemDraftRow>(
    `select * from item_drafts order by updatedAt desc`,
  );
  return rows.map((row) => ({ ...row, needsResearch: Boolean(row.needsResearch) }));
}

export async function enqueueCreateInventoryFromDraft(draft: LocalItemDraft): Promise<void> {
  await enqueueOperation('CreateInventoryDraft', '/api/inventory', 'POST', {
    title: draft.title,
    sellerAskingPrice: draft.sellerAskingPrice,
    estimatedSalePrice: draft.expectedSalePrice,
    condition: draft.condition,
    barcode: draft.barcode,
    notes: [draft.notes, draft.sourceName ? `Source: ${draft.sourceName}` : null].filter(Boolean).join('\n'),
  });

  const db = await getDatabase();
  await db.runAsync(`update item_drafts set syncStatus = 'queued', updatedAt = ? where id = ?`, [
    new Date().toISOString(), draft.id,
  ]);
}
