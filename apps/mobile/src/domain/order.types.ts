// Mirrors the current backend commerce contracts (ApiContracts.cs). The admin unified everything onto the Order:
// order → lines → per-line sale financials → refunds → returns → fulfillment (tracking-driven). These hand-typed
// shapes replace the old flat Sale/Listing model the mobile app was built against.

export type OrderStatus =
  | 'Pending'
  | 'Paid'
  | 'Fulfilling'
  | 'Shipped'
  | 'Delivered'
  | 'Cancelled'
  | 'Refunded';

export type PaymentStatus = 'Pending' | 'Paid' | 'PartiallyRefunded' | 'Refunded' | 'Failed';
export type FulfillmentStatus = 'Unfulfilled' | 'Partial' | 'Fulfilled';

export type SaleStatus =
  | 'Recorded'
  | 'Packed'
  | 'Shipped'
  | 'Delivered'
  | 'ReturnRequested'
  | 'Returned'
  | 'Refunded'
  | 'Cancelled'
  | 'PartiallyRefunded';

export type RefundType = 'Partial' | 'Full';
export type ReturnStatus = 'Requested' | 'LabelIssued' | 'Received' | 'Refunded' | 'Rejected' | 'Cancelled';

/** Per-line sale financials — the money truth for one item on an order. */
export interface SaleFinancials {
  id: string;
  inventoryItemId: string;
  inventoryItemTitle: string;
  listingId: string | null;
  platform: string;
  soldPrice: number;
  shippingCharged: number;
  costOfGoods: number;
  platformFees: number;
  paymentFees: number;
  shippingCost: number;
  suppliesCost: number;
  otherCosts: number;
  salesTaxCollected: number;
  taxRemittedByPlatform: boolean;
  salesTaxOwed: number;
  netProfit: number;
  status: SaleStatus;
  soldAt: string;
  notes: string | null;
  allowedTransitions: SaleStatus[];
  totalRefunded: number;
  feeCreditTotal: number;
  refundCount: number;
  orderId: string | null;
}

export interface OrderLine {
  id: string;
  inventoryItemId: string;
  titleSnapshot: string;
  skuSnapshot: string | null;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
  unitCostSnapshot: number | null;
  sale: SaleFinancials | null;
}

export interface OrderSummary {
  id: string;
  orderNumber: string;
  status: OrderStatus;
  paymentStatus: PaymentStatus;
  fulfillmentStatus: FulfillmentStatus;
  email: string | null;
  customerName: string | null;
  customerId: string | null;
  grandTotal: number;
  currency: string;
  itemCount: number;
  isLocalPickup: boolean;
  createdAt: string;
  paidAt: string | null;
  shippingCarrier: string | null;
  trackingNumber: string | null;
  shippedAt: string | null;
  deliveredAt: string | null;
  channel: string;
  externalOrderId: string | null;
  buyerReference: string | null;
}

export interface OrderDetail extends OrderSummary {
  subtotal: number;
  discountTotal: number;
  taxTotal: number;
  shippingTotal: number;
  costOfGoods: number;
  grossMargin: number;
  shipToName: string | null;
  shipToLine1: string | null;
  shipToLine2: string | null;
  shipToCity: string | null;
  shipToRegion: string | null;
  shipToPostalCode: string | null;
  shipToCountry: string | null;
  customerNote: string | null;
  trackingUrl: string | null;
  totalRefunded: number;
  lines: OrderLine[];
}

export interface Refund {
  id: string;
  saleId: string;
  type: RefundType;
  itemAmount: number;
  shippingRefunded: number;
  taxRefunded: number;
  feeCredit: number;
  itemReturned: boolean;
  reason: string;
  notes: string | null;
  refundedAt: string;
  recordedBy: string;
}

export interface Return {
  id: string;
  saleId: string;
  orderId: string | null;
  inventoryItemTitle: string;
  status: ReturnStatus;
  returnShippingCost: number;
  reason: string;
  notes: string | null;
  restocked: boolean;
  refundId: string | null;
  requestedAt: string;
  labelIssuedAt: string | null;
  receivedAt: string | null;
  resolvedAt: string | null;
}

// ── Requests ────────────────────────────────────────────────────────────────
export interface RecordSaleRequest {
  inventoryItemId: string;
  listingId?: string | null;
  platform: string;
  soldPrice: number;
  platformFees: number;
  paymentFees: number;
  shippingCost: number;
  suppliesCost: number;
  otherCosts: number;
  soldAt?: string | null;
  notes?: string | null;
  salesTaxCollected?: number;
  taxRemittedByPlatform?: boolean;
  shippingCharged?: number;
  externalOrderId?: string | null;
  buyerReference?: string | null;
  buyerName?: string | null;
  shippingCarrier?: string | null;
  trackingNumber?: string | null;
}

export interface RecordRefundRequest {
  type: RefundType;
  itemAmount: number;
  reason: string;
  shippingRefunded?: number;
  taxRefunded?: number;
  feeCredit?: number;
  itemReturned?: boolean;
  refundedAt?: string | null;
  notes?: string | null;
}

export interface OpenReturnRequest {
  reason: string;
  notes?: string | null;
}

export interface IssueReturnLabelRequest {
  returnShippingCost: number;
}

export interface ResolveReturnRequest {
  itemAmount: number;
  type?: RefundType;
  shippingRefunded?: number;
  taxRefunded?: number;
  feeCredit?: number;
  restock?: boolean;
  reason?: string | null;
  notes?: string | null;
}

export interface UpdateOrderTrackingRequest {
  shippingCarrier?: string | null;
  trackingNumber?: string | null;
  trackingUrl?: string | null;
  shippedAt?: string | null;
  deliveredAt?: string | null;
  markShipped?: boolean;
  markDelivered?: boolean;
}

export interface MergeOrdersRequest {
  orderIds: string[];
  survivingOrderId?: string | null;
}

export interface SaleChannelOption {
  value: string;
  label: string;
  group: string;
  isLegacy?: boolean;
  feeProfiles?: ChannelFeeProfile[] | null;
}
export interface SalesReferenceData {
  channels: SaleChannelOption[];
}

export interface SaleCandidate {
  inventoryItemId: string;
  title: string;
  status: string;
  costOfGoods: number;
  expectedSalePrice: number | null;
  listingId: string | null;
  listingPlatform: string | null;
  listingPrice: number | null;
}

export interface ChannelFeeRule {
  name: string;
  percent?: number | null;
  fixedAmount?: number | null;
  appliesAboveAmount?: number | null;
  appliesUpToAmount?: number | null;
  minimumFee?: number | null;
  maximumFee?: number | null;
  notes?: string | null;
}

export interface ChannelFeeProfile {
  id: string;
  label: string;
  description: string;
  isDefault: boolean;
  platformRules: ChannelFeeRule[];
  paymentRules: ChannelFeeRule[];
}
