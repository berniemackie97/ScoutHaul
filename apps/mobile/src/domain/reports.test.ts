import { maxSeriesAmount, reportGrossMargin, reportRange, topDimensions } from './reports';

describe('report helpers', () => {
  it('builds deterministic preset ranges', () => {
    const now = new Date('2026-07-14T12:00:00Z');

    expect(reportRange('30d', now).label).toBe('Last 30 days');
    expect(reportRange('90d', now).from).toBe('2026-04-15T12:00:00.000Z');
    expect(reportRange('ytd', now).from).toBe('2026-01-01T00:00:00.000Z');
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
