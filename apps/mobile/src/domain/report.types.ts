export type ReportChange = {
  current?: number;
  previous?: number;
  percentChange?: number | null;
};

export type ReportComparison = {
  grossSales?: ReportChange;
  netProfit?: ReportChange;
  salesCount?: ReportChange;
  averageSaleValue?: ReportChange;
  operatingExpenses?: ReportChange;
};

export type ReportKpi = {
  salesCount?: number;
  returnedSalesCount?: number;
  grossSales?: number;
  costOfGoods?: number;
  grossProfit?: number;
  saleCosts?: number;
  realizedNetProfit?: number;
  operatingExpenses?: number;
  inventorySpend?: number;
  averageSaleValue?: number;
  netMarginPercent?: number;
  salesTaxOwed?: number;
  netIncomeAfterExpenses?: number;
};

export type ReportInventorySnapshot = {
  itemCount?: number;
  cost?: number;
  estimatedValue?: number;
  unlistedCount?: number;
  staleCount?: number;
  missingCostCount?: number;
  missingValueCount?: number;
  missingLocationCount?: number;
  missingPhotoCount?: number;
};

export type ReportSeriesPoint = {
  periodStart?: string;
  label?: string | null;
  grossSales?: number;
  netProfit?: number;
  operatingExpenses?: number;
  inventorySpend?: number;
  salesCount?: number;
};

export type ReportDimension = {
  name?: string | null;
  count?: number;
  amount?: number;
  secondaryAmount?: number;
};

export type ReportAgingBucket = {
  label?: string | null;
  count?: number;
  cost?: number;
  estimatedValue?: number;
};

export type ReportSaleRow = {
  saleId?: string;
  inventoryItemId?: string;
  title?: string | null;
  platform?: string | null;
  soldPrice?: number;
  purchaseCost?: number;
  netProfit?: number;
  marginPercent?: number;
  soldAt?: string;
};

export type BusinessReport = {
  from?: string;
  to?: string;
  granularity?: string | null;
  generatedAt?: string;
  kpis?: ReportKpi;
  comparison?: ReportComparison;
  inventory?: ReportInventorySnapshot;
  series?: ReportSeriesPoint[] | null;
  platforms?: ReportDimension[] | null;
  expenseCategories?: ReportDimension[] | null;
  inventoryCategories?: ReportDimension[] | null;
  inventoryStatuses?: ReportDimension[] | null;
  aging?: ReportAgingBucket[] | null;
  topSales?: ReportSaleRow[] | null;
};
