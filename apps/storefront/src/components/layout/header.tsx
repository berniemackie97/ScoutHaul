import Link from "next/link";
import { Search, ShoppingBag } from "lucide-react";

const navigation = [
  { name: "Shop", href: "/products" },
  { name: "Categories", href: "/categories" },
  { name: "Repairs", href: "/repairs" },
  { name: "Consignment", href: "/consignment" },
  { name: "About", href: "/about" },
];

export function Header() {
  return (
    <header className="sticky top-0 z-40 border-b border-rule bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/80">
      {/* Top utility strip */}
      <div className="hidden border-b border-rule/60 bg-surface md:block">
        <div className="mx-auto flex max-w-7xl items-center justify-between px-6 py-1.5 font-mono text-[11px] uppercase tracking-wider text-slate/80">
          <span>Overhaul Trading Co. — Bradenton, Florida</span>
          <span>Free shipping over $100 · Local pickup in Bradenton</span>
        </div>
      </div>

      <div className="mx-auto flex h-16 max-w-7xl items-center gap-8 px-4 md:px-6">
        <Link href="/" className="flex shrink-0 items-baseline gap-1.5">
          <span className="font-heading text-lg font-semibold leading-none tracking-tight text-ink">
            OVERHAUL TRADING
          </span>
          <span className="font-mono text-[11px] font-semibold uppercase tracking-[0.2em] text-copper">
            Co.
          </span>
        </Link>

        <nav className="hidden items-center gap-7 lg:flex">
          {navigation.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              className="text-sm font-medium text-slate transition-colors hover:text-ink"
            >
              {item.name}
            </Link>
          ))}
        </nav>

        <div className="ml-auto flex items-center gap-2">
          <label className="hidden items-center gap-2 border border-rule bg-background px-3 py-2 text-slate transition-colors focus-within:border-copper md:flex">
            <Search className="h-4 w-4 shrink-0" aria-hidden />
            <input
              placeholder="Search the floor…"
              aria-label="Search products"
              className="w-44 bg-transparent text-sm text-ink outline-none placeholder:text-slate/60"
            />
          </label>
          <Link
            href="/cart"
            aria-label="Cart"
            className="relative border border-transparent p-2 text-ink transition-colors hover:bg-surface"
          >
            <ShoppingBag className="h-5 w-5" aria-hidden />
            <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center bg-copper px-1 font-mono text-[10px] font-semibold text-white">
              0
            </span>
          </Link>
        </div>
      </div>
    </header>
  );
}
