import { ShopMessage } from "@/components/feedback/shop-message";

/**
 * A genuinely missing page or piece — distinct from the catalog being unreachable, which now throws
 * and lands on the error boundary instead of quietly turning into this.
 */
export default function ShopNotFound() {
  return (
    <ShopMessage
      eyebrow="Not found"
      title="This page isn't here"
      body="Every piece is one of a kind, so this one may already have found its home. The rest of the shelf is still waiting."
    />
  );
}
