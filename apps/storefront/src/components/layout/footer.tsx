import Link from "next/link";

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
    <footer className="mt-24 bg-ink text-white/80">
      <div className="mx-auto max-w-7xl px-6 py-16">
        <div className="grid gap-12 md:grid-cols-[1.4fr_1fr_1fr_1fr]">
          <div className="space-y-4">
            <div className="flex items-baseline gap-1.5">
              <span className="font-heading text-lg font-semibold tracking-tight text-white">
                OVERHAUL TRADING
              </span>
              <span className="font-mono text-[11px] font-semibold uppercase tracking-[0.2em] text-copper">
                LLC
              </span>
            </div>
            <p className="max-w-xs text-sm leading-relaxed text-white/70">
              Quality goods, a second life. Sourced carefully, described honestly, delivered
              reliably — a serious operator, not a flea-market stall.
            </p>
            <p className="font-mono text-[11px] uppercase leading-relaxed tracking-wider text-white/50">
              1675 55th Avenue Cir E, Suite 201
              <br />
              Bradenton, FL 34203
            </p>
          </div>

          {columns.map((col) => (
            <div key={col.heading}>
              <h3 className="font-mono text-[11px] font-semibold uppercase tracking-widest text-copper">
                {col.heading}
              </h3>
              <ul className="mt-4 space-y-2.5">
                {col.links.map((link) => (
                  <li key={link.href}>
                    <Link
                      href={link.href}
                      className="text-sm text-white/70 transition-colors hover:text-white"
                    >
                      {link.name}
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="mt-14 flex flex-col gap-3 border-t border-white/10 pt-6 font-mono text-[11px] uppercase tracking-wider text-white/45 md:flex-row md:items-center md:justify-between">
          <span>© {new Date().getFullYear()} Overhaul Trading Company LLC · FL Doc. L26000300910</span>
          <span>Sourced well · Described honestly · Shipped fast</span>
        </div>
      </div>
    </footer>
  );
}
