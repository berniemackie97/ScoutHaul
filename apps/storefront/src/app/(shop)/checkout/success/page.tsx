import Image from "next/image";
import Link from "next/link";
import { CheckCircle2, ShoppingBag, Truck } from "lucide-react";
import { imageUrl, money, type OrderSummary } from "@/lib/commerce";
import { storefrontServerJson } from "@/lib/storefront-proxy";

export const dynamic = "force-dynamic";
export const metadata = { title: "Order confirmed" };

export default async function CheckoutSuccessPage({
  searchParams,
}: {
  searchParams: Promise<{ session_id?: string }>;
}) {
  const { session_id: sessionId } = await searchParams;
  const order = sessionId
    ? await storefrontServerJson<OrderSummary>(`/api/storefront/orders/by-session/${encodeURIComponent(sessionId)}`)
    : null;

  if (!order) {
    return (
      <main className="mx-auto max-w-3xl px-6 py-16">
        <div className="border border-rule bg-surface p-8 text-center">
          <ShoppingBag className="mx-auto h-10 w-10 text-copper" aria-hidden />
          <h1 className="mt-4 font-heading text-3xl font-semibold text-ink">We could not find that order.</h1>
          <p className="mt-3 text-sm leading-6 text-slate">
            If payment completed, check your Stripe email receipt. Otherwise return to the cart and try again.
          </p>
          <Link
            href="/cart"
            className="mt-6 inline-flex bg-copper px-6 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-ink"
          >
            Back to cart
          </Link>
        </div>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-6xl px-6 py-12 md:py-16">
      <div className="border border-emerald-500/25 bg-emerald-500/10 p-6">
        <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
          <div className="flex gap-4">
            <CheckCircle2 className="mt-1 h-8 w-8 shrink-0 text-emerald-700" aria-hidden />
            <div>
              <p className="font-mono text-[11px] uppercase tracking-[0.24em] text-emerald-800">Payment received</p>
              <h1 className="mt-2 font-heading text-4xl font-semibold tracking-[-0.02em] text-ink">
                Order {order.orderNumber}
              </h1>
              <p className="mt-2 text-sm leading-6 text-slate">
                Confirmation was sent{order.email ? ` to ${order.email}` : ""}. We will update the order when it ships.
              </p>
            </div>
          </div>
          <Link href="/products" className="inline-flex bg-ink px-5 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-background">
            Keep shopping
          </Link>
        </div>
      </div>

      <div className="mt-10 grid gap-8 lg:grid-cols-[1fr_24rem]">
        <section className="border border-rule bg-background">
          <div className="border-b border-rule px-5 py-4">
            <h2 className="font-heading text-2xl font-semibold text-ink">Items</h2>
          </div>
          <div className="divide-y divide-rule">
            {order.lines.map((line, index) => (
              <article key={`${line.title}-${index}`} className="grid gap-4 p-5 sm:grid-cols-[6rem_1fr_auto]">
                <div className="relative aspect-square overflow-hidden bg-surface ring-1 ring-rule">
                  {line.imageUrl ? (
                    <Image src={imageUrl(line.imageUrl)!} alt={line.title} fill sizes="96px" className="object-cover" />
                  ) : (
                    <span className="absolute inset-0 flex items-center justify-center font-heading text-3xl text-ink/[0.06]">OT</span>
                  )}
                </div>
                <div>
                  <h3 className="font-heading text-xl font-medium text-ink">{line.title}</h3>
                  <p className="mt-2 text-sm text-slate">Quantity 1</p>
                </div>
                <p className="font-heading text-lg font-semibold text-ink">{money(line.unitPrice)}</p>
              </article>
            ))}
          </div>
        </section>

        <aside className="h-fit border border-rule bg-surface p-6 lg:sticky lg:top-28">
          <h2 className="font-heading text-2xl font-semibold text-ink">Receipt summary</h2>
          <dl className="mt-6 space-y-3 border-b border-rule pb-5 text-sm">
            <div className="flex justify-between gap-4">
              <dt className="text-slate">Subtotal</dt>
              <dd className="font-semibold text-ink">{money(order.subtotal)}</dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-slate">Shipping</dt>
              <dd className="font-semibold text-ink">{money(order.shippingTotal)}</dd>
            </div>
            <div className="flex justify-between gap-4">
              <dt className="text-slate">Tax</dt>
              <dd className="font-semibold text-ink">{money(order.taxTotal)}</dd>
            </div>
          </dl>
          <div className="mt-5 flex justify-between gap-4 font-heading text-2xl font-semibold text-ink">
            <span>Total</span>
            <span>{money(order.grandTotal)}</span>
          </div>
          <p className="mt-5 flex gap-2 text-sm text-slate">
            <Truck className="mt-0.5 h-4 w-4 shrink-0 text-copper" aria-hidden />
            {order.isLocalPickup ? "Local pickup selected." : "Shipping details are attached to your paid order."}
          </p>
        </aside>
      </div>
    </main>
  );
}
