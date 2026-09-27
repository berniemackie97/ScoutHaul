import { apiRequest, type CallOptions } from './client';
import type {
  IssueReturnLabelRequest,
  MergeOrdersRequest,
  OpenReturnRequest,
  OrderDetail,
  OrderStatus,
  OrderSummary,
  RecordRefundRequest,
  RecordSaleRequest,
  Refund,
  ResolveReturnRequest,
  Return,
  SaleCandidate,
  SaleFinancials,
  SalesReferenceData,
  UpdateOrderTrackingRequest,
} from '../domain/order.types';

function query(params: Record<string, string | undefined>): string {
  const pairs = Object.entries(params)
    .filter(([, v]) => v !== undefined && v !== '' && v !== 'All')
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v as string)}`);
  return pairs.length ? `?${pairs.join('&')}` : '';
}

export const ordersApi = {
  // ── Orders ────────────────────────────────────────────────────────────────
  list: (filters: { status?: string; search?: string; channel?: string } = {}) =>
    apiRequest<OrderSummary[]>(`/api/orders${query(filters)}`),
  get: (id: string) => apiRequest<OrderDetail>(`/api/orders/${id}`),
  updateStatus: (id: string, status: OrderStatus) =>
    apiRequest<OrderDetail>(`/api/orders/${id}/status`, { method: 'PATCH', body: { status } }),
  updateTracking: (id: string, body: UpdateOrderTrackingRequest) =>
    apiRequest<OrderDetail>(`/api/orders/${id}/tracking`, { method: 'PATCH', body }),
  merge: (body: MergeOrdersRequest) =>
    apiRequest<OrderDetail>('/api/orders/merge', { method: 'POST', body }),
  remove: (id: string) => apiRequest<void>(`/api/orders/${id}`, { method: 'DELETE' }),

  // ── Sale (record + reference) ───────────────────────────────────────────────
  // Asks for a page of matches: the unsearched list stops at the most recent items, a search reaches all of them.
  candidates: (search = '', options: CallOptions = {}) =>
    apiRequest<SaleCandidate[]>(`/api/sales/candidates${query({ search, take: '20' })}`, options),
  recordSale: (body: RecordSaleRequest, idempotencyKey?: string) =>
    apiRequest<SaleFinancials>('/api/sales', { method: 'POST', body, idempotencyKey }),
  referenceData: () => apiRequest<SalesReferenceData>('/api/sales/reference-data'),

  // ── Refunds (money events on a sale line) ────────────────────────────────────
  listRefunds: (saleId: string) => apiRequest<Refund[]>(`/api/sales/${saleId}/refunds`),
  recordRefund: (saleId: string, body: RecordRefundRequest) =>
    apiRequest<SaleFinancials>(`/api/sales/${saleId}/refunds`, { method: 'POST', body }),
  voidRefund: (saleId: string, refundId: string) =>
    apiRequest<void>(`/api/sales/${saleId}/refunds/${refundId}`, { method: 'DELETE' }),

  // ── Returns (RMA lifecycle on a sale line) ───────────────────────────────────
  listReturns: (saleId: string) => apiRequest<Return[]>(`/api/sales/${saleId}/returns`),
  openReturn: (saleId: string, body: OpenReturnRequest) =>
    apiRequest<Return>(`/api/sales/${saleId}/returns`, { method: 'POST', body }),
  issueReturnLabel: (returnId: string, body: IssueReturnLabelRequest) =>
    apiRequest<Return>(`/api/sales/returns/${returnId}/label`, { method: 'POST', body }),
  receiveReturn: (returnId: string) =>
    apiRequest<Return>(`/api/sales/returns/${returnId}/receive`, { method: 'POST' }),
  resolveReturn: (returnId: string, body: ResolveReturnRequest) =>
    apiRequest<Return>(`/api/sales/returns/${returnId}/resolve`, { method: 'POST', body }),
  rejectReturn: (returnId: string, reason: string) =>
    apiRequest<Return>(`/api/sales/returns/${returnId}/reject`, { method: 'POST', body: { reason } }),
  cancelReturn: (returnId: string) =>
    apiRequest<Return>(`/api/sales/returns/${returnId}/cancel`, { method: 'POST' }),
};
