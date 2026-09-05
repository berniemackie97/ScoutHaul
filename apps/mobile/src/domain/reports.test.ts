import { maxSeriesAmount, reportGrossMargin, reportRange, topDimensions } from './reports';

describe('report helpers', () => {
  it('builds deterministic preset ranges', () => {
    const now = new Date('2026-07-14T12:00:00Z');

    expect(reportRange('30d', now).label).toBe('Last 30 days');
    // Calendar days. These used to assert full timestamps, which pinned the bug in place: the
    // endpoint binds DateOnly, so nothing the screen sent was ever accepted.
    expect(reportRange('90d', now).from).toBe('2026-04-15');
    expect(reportRange('ytd', now).from).toBe('2026-01-01');
  });

  it('computes gross margin only when gross sales exist', () => {
    expect(reportGrossMargin({ kpis: { grossSales: 100, grossProfit: 35 } })).toBe(35);
    expect(reportGrossMargin({ kpis: { grossSales: 0, grossProfit: 35 } })).toBeNull();
  });

  it('sorts dimensions and finds series max safely', () => {
    expect(topDimensions([{ name: 'B', amount: 10 }, { name: 'A', amount: 30 }]).map((row) => row.name)).toEqual(['A', 'B']);
    expect(maxSeriesAmount([{ grossSales: 12 }, { netProfit: 40 }, { inventorySpend: 8 }])).toBe(40);
    expect(maxSeriesAmount([])).toBe(1);
  });
});

describe('reportRange', () => {
  const now = new Date('2026-09-05T03:09:37.178Z');

  it('sends a calendar day, which is what the endpoint binds', () => {
    // A full timestamp cannot bind to DateOnly, so every range failed with
    // "The value '2026-09-05T03:09:37.178Z' is not valid" and the screen never loaded.
    for (const key of ['30d', '90d', 'ytd'] as const) {
      const range = reportRange(key, now);
      expect(range.from).toMatch(/^\d{4}-\d{2}-\d{2}$/);
      expect(range.to).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    }
  });

  it('counts back the right number of days', () => {
    expect(reportRange('30d', now).from).toBe('2026-08-06');
    expect(reportRange('90d', now).from).toBe('2026-06-07');
    expect(reportRange('30d', now).to).toBe('2026-09-05');
  });

  it('starts year to date at the first of January', () => {
    expect(reportRange('ytd', now).from).toBe('2026-01-01');
  });
});
