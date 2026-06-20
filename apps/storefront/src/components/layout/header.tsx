"use client";

import { useEffect, useState, type FormEvent } from "react";
import { usePathname, useRouter } from "next/navigation";
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
  const pathname = usePathname();
  const router = useRouter();
  const [scrolled, setScrolled] = useState(false);
  const [query, setQuery] = useState("");

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 16);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  const onHome = pathname === "/";
  const solid = scrolled || !onHome;

  function onSearch(e: FormEvent) {
    e.preventDefault();
    const term = query.trim();
    router.push(term ? `/products?search=${encodeURIComponent(term)}` : "/products");
  }

  return (
    <>
      <header
        className={`fixed top-0 z-50 w-full transition-colors duration-300 ${
          solid
            ? "border-b border-rule bg-background/85 backdrop-blur supports-[backdrop-filter]:bg-background/75"
            : "border-b border-transparent"
        }`}
      >
        <div className="mx-auto flex h-16 max-w-7xl items-center gap-8 px-4 md:h-20 md:px-6">
          <Link href="/" className="flex shrink-0 items-baseline gap-1.5">
            <span
              className={`font-heading text-lg font-semibold tracking-tight transition-colors ${
                solid ? "text-ink" : "text-background"
              }`}
            >
              OVERHAUL TRADING
            </span>
            <span className="font-mono text-[11px] font-semibold uppercase tracking-[0.2em] text-copper">Co.</span>
          </Link>

          <nav className="hidden items-center gap-7 lg:flex">
            {navigation.map((item) => (
              <Link
                key={item.href}
                href={item.href}
                className={`text-sm font-medium transition-colors ${
                  solid ? "text-slate hover:text-ink" : "text-background/75 hover:text-copper"
                }`}
              >
                {item.name}
              </Link>
            ))}
          </nav>

          <div className="ml-auto flex items-center gap-2">
            <form
              onSubmit={onSearch}
              className={`hidden items-center gap-2 border px-3 py-2 transition-colors md:flex ${
                solid
                  ? "border-rule focus-within:border-copper"
                  : "border-background/25 focus-within:border-copper"
              }`}
            >
              <Search className={`h-4 w-4 shrink-0 ${solid ? "text-slate" : "text-background/70"}`} aria-hidden />
              <input
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Search the floor…"
                aria-label="Search products"
                className={`w-40 bg-transparent text-sm outline-none ${
                  solid ? "text-ink placeholder:text-slate/60" : "text-background placeholder:text-background/50"
                }`}
              />
            </form>
            <Link
              href="/cart"
              aria-label="Cart"
              className={`relative p-2 transition-colors ${solid ? "text-ink hover:text-copper" : "text-background hover:text-copper"}`}
            >
              <ShoppingBag className="h-5 w-5" aria-hidden />
              <span className="absolute -right-0.5 -top-0.5 flex h-4 min-w-4 items-center justify-center bg-copper px-1 font-mono text-[10px] font-semibold text-ink">
                0
              </span>
            </Link>
          </div>
        </div>
      </header>
      {!onHome ? <div aria-hidden className="h-16 md:h-20" /> : null}
    </>
  );
}
