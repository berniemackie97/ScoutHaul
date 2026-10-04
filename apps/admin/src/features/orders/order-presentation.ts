import type { BadgeProps } from "@overhaul/ui";
import { ORDER_STAGES } from "./order-stages";

type Variant = NonNullable<BadgeProps["variant"]>;

/** One status per order: the workflow stage. Payment only speaks up when it is not simply "paid". */
export function stageLabel(stage: string | null | undefined): string {
  return ORDER_STAGES.find((entry) => entry.key === stage)?.label ?? stage ?? "Unknown";
}

export function stageVariant(stage: string | null | undefined): Variant {
  if (stage === "NeedsAttention") return "destructive";
  if (stage === "Delivered") return "success";
  if (stage === "AwaitingPayment") return "warning";
  if (stage === "Closed") return "secondary";
  return "info";
}

export function paymentNote(paymentStatus: string | null | undefined): { label: string; variant: Variant } | null {
  switch (paymentStatus) {
    case "Pending":
      return { label: "Unpaid", variant: "warning" };
    case "PartiallyRefunded":
      return { label: "Partly refunded", variant: "warning" };
    case "Refunded":
      return { label: "Refunded", variant: "destructive" };
    case "Failed":
      return { label: "Payment failed", variant: "destructive" };
    default:
      return null;
  }
}

const CARRIER_TRACKING: Array<[RegExp, (number: string) => string]> = [
  [/usps|standard envelope/i, (n) => `https://tools.usps.com/go/TrackConfirmAction?tLabels=${n}`],
  [/ups/i, (n) => `https://www.ups.com/track?tracknum=${n}`],
  [/fedex/i, (n) => `https://www.fedex.com/fedextrack/?trknbr=${n}`],
  [/dhl/i, (n) => `https://www.dhl.com/us-en/home/tracking/tracking-express.html?tracking-id=${n}`],
  [/ontrac/i, (n) => `https://www.ontrac.com/tracking/?number=${n}`],
];

/** The recorded tracking link, or the carrier's public tracking page when none was saved. */
export function trackingHref(
  carrier: string | null | undefined,
  trackingNumber: string | null | undefined,
  trackingUrl?: string | null,
): string | null {
  if (trackingUrl) return trackingUrl;
  if (!carrier || !trackingNumber) return null;
  const number = encodeURIComponent(trackingNumber.replace(/\s+/g, ""));
  return CARRIER_TRACKING.find(([pattern]) => pattern.test(carrier))?.[1](number) ?? null;
}

/** Whole days between two instants, or null when either is missing. */
export function daysBetween(from: string | null | undefined, to: string | null | undefined): number | null {
  if (!from || !to) return null;
  const ms = new Date(to).getTime() - new Date(from).getTime();
  return Number.isFinite(ms) ? Math.max(0, Math.round(ms / 86_400_000)) : null;
}
