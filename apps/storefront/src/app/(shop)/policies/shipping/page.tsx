import Link from "next/link";
import { ArrowRight, RotateCcw, ShieldCheck, Truck } from "lucide-react";
import { Reveal } from "@/components/ui/reveal";

export const metadata = {
  title: "Shipping & returns",
  description: "Shipping, pickup, returns, and condition guarantee policies for Overhaul Trading Company orders.",
  alternates: { canonical: "/policies/shipping" },
};

const policies = [
  {
    icon: Truck,
    title: "Shipping",
    body:
      "Most orders ship from Bradenton, Florida. Shipping cost is confirmed during checkout before payment. Orders are packed to match the item type, size, and fragility.",
  },
  {
    icon: ShieldCheck,
    title: "Local pickup",
    body:
      "Local pickup is available by appointment after checkout confirmation. Bring your order confirmation so we can release the correct item.",
  },
  {
    icon: RotateCcw,
    title: "Returns",
    body:
      "If an item arrives materially not as described, contact us promptly. We inspect every return against the original listing, photos, and shipment record.",
  },
];

export default function ShippingPolicyPage() {
  return (
    <section className="mx-auto max-w-7xl px-6 py-20 md:py-28">
      <Reveal>
        <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">Policy</p>
        <h1 className="mt-4 max-w-3xl font-heading text-[clamp(2.5rem,6vw,4.75rem)] font-semibold leading-[0.95] tracking-[-0.025em] text-ink">
          Shipping, pickup, and returns.
        </h1>
        <p className="mt-6 max-w-2xl text-lg leading-relaxed text-slate">
          Checkout confirms the live payment total before your card is charged. Every order is tied to one-of-one inventory,
          tracking evidence, and the listing condition record.
        </p>
      </Reveal>

      <div className="mt-14 grid gap-5 lg:grid-cols-3">
        {policies.map((policy, index) => (
          <Reveal key={policy.title} delay={index * 80}>
            <article className="h-full border border-rule bg-background p-6">
              <policy.icon className="h-7 w-7 text-copper" aria-hidden />
              <h2 className="mt-6 font-heading text-2xl font-semibold text-ink">{policy.title}</h2>
              <p className="mt-4 text-sm leading-relaxed text-slate">{policy.body}</p>
            </article>
          </Reveal>
        ))}
      </div>

      <Reveal delay={260}>
        <div className="mt-12 border border-copper/35 bg-copper/[0.05] p-6">
          <h2 className="font-heading text-2xl font-semibold text-ink">Need help with an order?</h2>
          <p className="mt-3 max-w-2xl text-sm leading-relaxed text-slate">
            Send the order number and item name. We can verify status, tracking, pickup timing, or return eligibility
            from the same order record used by checkout and fulfillment.
          </p>
          <Link
            href="/contact"
            className="mt-6 inline-flex items-center gap-2 bg-ink px-5 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:bg-copper"
          >
            Contact support
            <ArrowRight className="h-4 w-4" aria-hidden />
          </Link>
        </div>
      </Reveal>
    </section>
  );
}
