import Link from "next/link";
import { BadgeCheck } from "lucide-react";

const columns = [
  {
    heading: "Shop",
    links: [
      { name: "All products", href: "/products" },
      { name: "Furniture", href: "/categories/furniture" },
      { name: "Electronics", href: "/categories/electronics" },
      { name: "Collectibles", href: "/categories/collectibles" },
    ],
  },
  {
    heading: "Services",
    links: [
      { name: "Repairs", href: "/repairs" },
      { name: "Consignment", href: "/consignment" },
      { name: "How we grade", href: "/grading" },
    ],
  },
  {
    heading: "Company",
    links: [
      { name: "About", href: "/about" },
      { name: "How we grade", href: "/grading" },
      { name: "Shipping & returns", href: "/policies/shipping" },
      { name: "Contact", href: "/contact" },
    ],
  },
];

export function Footer() {
  return (
    <footer className="border-t-2 border-copper/30 bg-ink text-background/75">
      <div className="mx-auto max-w-7xl px-6 py-16">
        <div className="grid gap-12 md:grid-cols-[1.4fr_1fr_1fr_1fr]">
          <div className="space-y-4">
            <div className="flex items-baseline gap-1.5">
              <span className="font-heading text-lg font-semibold tracking-tight text-background">OVERHAUL TRADING</span>
              <span className="font-mono text-[11px] font-semibold uppercase tracking-[0.2em] text-copper">LLC</span>
            </div>
            <p className="max-w-xs text-sm leading-relaxed text-background/65">
              Quality goods, a second life. Sourced carefully, described honestly, delivered reliably — a serious
              operator, not a flea-market stall.
            </p>
            <p className="inline-flex items-center gap-2 font-mono text-[10px] uppercase tracking-[0.18em] text-copper">
              <BadgeCheck className="h-4 w-4" aria-hidden /> Every item inspected &amp; graded
            </p>
            <p className="font-mono text-[11px] uppercase leading-relaxed tracking-[0.16em] text-background/45">
              1675 55th Avenue Cir E, Suite 201
              <br />
              Bradenton, FL 34203
            </p>
          </div>

          {columns.map((col) => (
            <div key={col.heading}>
              <h3 className="font-mono text-[11px] font-semibold uppercase tracking-[0.2em] text-copper">{col.heading}</h3>
              <ul className="mt-4 space-y-2.5">
                {col.links.map((link) => (
                  <li key={link.href}>
                    <Link href={link.href} className="text-sm text-background/65 transition-colors hover:text-background">
                      {link.name}
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="mt-14 flex flex-col gap-3 border-t border-background/10 pt-6 font-mono text-[11px] uppercase tracking-[0.14em] text-background/45 md:flex-row md:items-center md:justify-between">
          <span>© {new Date().getFullYear()} Overhaul Trading Company LLC · FL Doc. L26000300910</span>
          <span>Sourced well · Described honestly · Shipped fast</span>
        </div>
      </div>
    </footer>
  );
}
