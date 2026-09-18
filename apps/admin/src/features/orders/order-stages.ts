/**
 * The order workflow's stages, as the portal names them.
 *
 * The API derives each order's stage (OrderWorkflow) and reports it by these keys. The orders page
 * uses them as its queue tabs and the home console links into those tabs, so the list lives here
 * once rather than in each — a stage renamed in one place and not the other would link to a tab
 * that does not exist. "Needs attention" leads because it is the one that costs money to ignore.
 */
export const ORDER_STAGES = [
  { key: "NeedsAttention", label: "Needs attention" },
  { key: "AwaitingPayment", label: "Awaiting payment" },
  { key: "ToPack", label: "To pack" },
  { key: "ToShip", label: "To ship" },
  { key: "ReadyForPickup", label: "Ready for pickup" },
  { key: "InTransit", label: "In transit" },
  { key: "Delivered", label: "Delivered" },
  { key: "Closed", label: "Closed" },
] as const;

export type OrderStage = (typeof ORDER_STAGES)[number]["key"];

export function isOrderStage(value: string | null | undefined): value is OrderStage {
  return ORDER_STAGES.some((stage) => stage.key === value);
}
