import Link from "next/link";
import { ArrowRight, Mail, MapPin, Phone } from "lucide-react";
import { Reveal } from "@/components/ui/reveal";

export const metadata = {
  title: "Contact",
  description: "Contact Overhaul Trading Company for order questions, local pickup, consignment, repairs, and product support.",
};

const contactMethods = [
  {
    icon: Mail,
    label: "Email",
    value: "hello@overhaultrading.com",
    href: "mailto:hello@overhaultrading.com",
    detail: "Best for order questions, offers, and consignment details.",
  },
  {
    icon: Phone,
    label: "Phone",
    value: "(941) 555-0142",
    href: "tel:+19415550142",
    detail: "Use for local pickup coordination and time-sensitive questions.",
  },
  {
    icon: MapPin,
    label: "Location",
    value: "1675 55th Avenue Cir E, Suite 201 · Bradenton, FL 34203",
    href: "https://www.google.com/maps/search/?api=1&query=1675%2055th%20Avenue%20Cir%20E%20Suite%20201%20Bradenton%20FL%2034203",
    detail: "Local pickup is by appointment after checkout confirmation.",
  },
];

export default function ContactPage() {
  return (
    <section className="mx-auto max-w-7xl px-6 py-20 md:py-28">
      <Reveal>
        <p className="font-mono text-[11px] uppercase tracking-[0.28em] text-copper">Support</p>
        <h1 className="mt-4 max-w-3xl font-heading text-[clamp(2.5rem,6vw,4.75rem)] font-semibold leading-[0.95] tracking-[-0.025em] text-ink">
          Contact Overhaul Trading.
        </h1>
        <p className="mt-6 max-w-2xl text-lg leading-relaxed text-slate">
          Questions about an item, an order, pickup, returns, consignment, or repairs can all start here.
        </p>
      </Reveal>

      <div className="mt-14 grid gap-5 lg:grid-cols-3">
        {contactMethods.map((method, index) => (
          <Reveal key={method.label} delay={index * 80}>
            <Link
              href={method.href}
              className="group block h-full border border-rule bg-background p-6 transition-transform hover:-translate-y-1"
              target={method.href.startsWith("http") ? "_blank" : undefined}
              rel={method.href.startsWith("http") ? "noreferrer" : undefined}
            >
              <method.icon className="h-7 w-7 text-copper" aria-hidden />
              <p className="mt-6 font-mono text-[10px] uppercase tracking-[0.2em] text-copper">{method.label}</p>
              <h2 className="mt-2 font-heading text-2xl font-semibold leading-tight text-ink">{method.value}</h2>
              <p className="mt-4 text-sm leading-relaxed text-slate">{method.detail}</p>
              <span className="mt-6 inline-flex items-center gap-2 text-sm font-semibold text-ink">
                Open
                <ArrowRight className="h-4 w-4 transition-transform group-hover:translate-x-1" aria-hidden />
              </span>
            </Link>
          </Reveal>
        ))}
      </div>
    </section>
  );
}
