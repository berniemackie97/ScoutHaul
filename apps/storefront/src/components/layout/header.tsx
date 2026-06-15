"use client";

import Link from "next/link";
import { useState } from "react";
import { SignedIn, SignedOut, UserButton } from "@clerk/nextjs";
import {
  Search,
  ShoppingCart,
  Heart,
  Menu,
  X,
  Wrench,
  Package,
  ChevronDown,
} from "lucide-react";
import { Button, Input, Badge } from "@overhaul/ui";

const navigation = [
  { name: "Shop All", href: "/products" },
  {
    name: "Categories",
    href: "/categories",
    children: [
      { name: "Furniture", href: "/categories/furniture" },
      { name: "Electronics", href: "/categories/electronics" },
      { name: "Collectibles", href: "/categories/collectibles" },
      { name: "Household", href: "/categories/household" },
      { name: "Sporting Goods", href: "/categories/sporting-goods" },
      { name: "Tools & Equipment", href: "/categories/tools-equipment" },
    ],
  },
  { name: "Repair Services", href: "/repairs" },
  { name: "Consignment", href: "/consignment" },
  { name: "Blog", href: "/blog" },
  { name: "About", href: "/about" },
];

export function Header() {
  const [mobileOpen, setMobileOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);

  return (
    <header className="sticky top-0 z-50 w-full border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      {/* Top bar */}
      <div className="bg-brand-600 text-white text-center text-sm py-1.5 px-4">
        Free shipping on orders over $100 | Quality secondhand goods, fair prices
      </div>

      <div className="container mx-auto px-4">
        <div className="flex h-16 items-center justify-between gap-4">
          {/* Mobile menu */}
          <button
            className="lg:hidden"
            onClick={() => setMobileOpen(!mobileOpen)}
          >
            {mobileOpen ? <X className="h-6 w-6" /> : <Menu className="h-6 w-6" />}
          </button>

          {/* Logo */}
          <Link href="/" className="flex items-center gap-2 shrink-0">
            <div className="h-9 w-9 rounded-lg bg-brand-600 flex items-center justify-center">
              <Package className="h-5 w-5 text-white" />
            </div>
            <div className="hidden sm:block">
              <span className="text-lg font-bold tracking-tight">Overhaul</span>
              <span className="text-lg font-light text-muted-foreground ml-1">Trading</span>
            </div>
          </Link>

          {/* Desktop nav */}
          <nav className="hidden lg:flex items-center gap-1">
            {navigation.map((item) => (
              <div key={item.name} className="relative group">
                <Link
                  href={item.href}
                  className="flex items-center gap-1 px-3 py-2 text-sm font-medium text-muted-foreground hover:text-foreground transition-colors rounded-md hover:bg-accent"
                >
                  {item.name}
                  {item.children && <ChevronDown className="h-3 w-3" />}
                </Link>
                {item.children && (
                  <div className="absolute left-0 top-full pt-1 hidden group-hover:block">
                    <div className="bg-popover border rounded-lg shadow-lg py-2 min-w-[200px]">
                      {item.children.map((child) => (
                        <Link
                          key={child.href}
                          href={child.href}
                          className="block px-4 py-2 text-sm hover:bg-accent transition-colors"
                        >
                          {child.name}
                        </Link>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            ))}
          </nav>

          {/* Search + Actions */}
          <div className="flex items-center gap-2">
            {searchOpen ? (
              <div className="flex items-center gap-2">
                <Input
                  placeholder="Search products..."
                  className="w-48 sm:w-64"
                  autoFocus
                  onBlur={() => setSearchOpen(false)}
                />
              </div>
            ) : (
              <Button variant="ghost" size="icon" onClick={() => setSearchOpen(true)}>
                <Search className="h-5 w-5" />
              </Button>
            )}

            {process.env.NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY ? (
              <>
                <SignedIn>
                  <Link href="/account/wishlist">
                    <Button variant="ghost" size="icon">
                      <Heart className="h-5 w-5" />
                    </Button>
                  </Link>
                  <Link href="/cart">
                    <Button variant="ghost" size="icon" className="relative">
                      <ShoppingCart className="h-5 w-5" />
                    </Button>
                  </Link>
                  <UserButton afterSignOutUrl="/" />
                </SignedIn>
                <SignedOut>
                  <Link href="/sign-in">
                    <Button variant="outline" size="sm">Sign In</Button>
                  </Link>
                  <Link href="/sign-up">
                    <Button size="sm">Sign Up</Button>
                  </Link>
                </SignedOut>
              </>
            ) : (
              <>
                <Link href="/cart">
                  <Button variant="ghost" size="icon" className="relative">
                    <ShoppingCart className="h-5 w-5" />
                  </Button>
                </Link>
                <Link href="/sign-in">
                  <Button variant="outline" size="sm">Sign In</Button>
                </Link>
              </>
            )}
          </div>
        </div>
      </div>

      {/* Mobile nav */}
      {mobileOpen && (
        <div className="lg:hidden border-t bg-background">
          <nav className="container mx-auto px-4 py-4 space-y-1">
            {navigation.map((item) => (
              <div key={item.name}>
                <Link
                  href={item.href}
                  className="block px-3 py-2 text-sm font-medium rounded-md hover:bg-accent"
                  onClick={() => setMobileOpen(false)}
                >
                  {item.name}
                </Link>
                {item.children?.map((child) => (
                  <Link
                    key={child.href}
                    href={child.href}
                    className="block pl-8 py-1.5 text-sm text-muted-foreground hover:text-foreground"
                    onClick={() => setMobileOpen(false)}
                  >
                    {child.name}
                  </Link>
                ))}
              </div>
            ))}
          </nav>
        </div>
      )}
    </header>
  );
}
