"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { ChevronLeft, ChevronRight, LogOut, Package2, X } from "lucide-react";
import { useState } from "react";
import { cn, Button } from "@overhaul/ui";
import { CommandPaletteTrigger } from "@/components/search/command-palette-trigger";
import { createSupabaseBrowserClient } from "@/lib/supabase/client";
import { isActiveHref, navGroups } from "./navigation";

/**
 * Below lg the sidebar is an off-canvas drawer (fixed, slides in over a backdrop, controlled by `mobileOpen`); at lg+
 * it is the in-flow, collapsible rail. `collapsed` is a desktop-only concept — its label-hiding is applied with `lg:`
 * prefixes so the mobile drawer always shows full labels.
 */
export function Sidebar({
  mobileOpen = false,
  onClose,
  onOpenSearch,
}: {
  mobileOpen?: boolean;
  onClose?: () => void;
  onOpenSearch: () => void;
}) {
  const pathname = usePathname();
  const router = useRouter();
  const [collapsed, setCollapsed] = useState(false);

  async function handleSignOut() {
    const supabase = createSupabaseBrowserClient();
    await supabase.auth.signOut();
    router.replace("/sign-in");
    router.refresh();
  }

  return (
    <>
      {/* Mobile backdrop */}
      {mobileOpen && <div className="fixed inset-0 z-40 bg-black/40 lg:hidden" onClick={onClose} aria-hidden />}

      <aside
        className={cn(
          "bg-card flex flex-col border-r",
          // mobile: off-canvas drawer
          "fixed inset-y-0 left-0 z-50 w-[264px] max-w-[85vw] transition-transform duration-200",
          mobileOpen ? "translate-x-0" : "-translate-x-full",
          // desktop: static, in-flow, collapsible rail
          "lg:static lg:z-auto lg:max-w-none lg:translate-x-0 lg:shrink-0 lg:transition-[width]",
          collapsed ? "lg:w-[68px]" : "lg:w-[240px]"
        )}
      >
        {/* Header */}
        <div className="flex h-16 shrink-0 items-center justify-between border-b px-4 pt-[env(safe-area-inset-top)]">
          <Link
            href="/"
            onClick={onClose}
            className={cn("flex items-center gap-2", collapsed && "lg:hidden")}
          >
            <div className="bg-brand-600 flex h-8 w-8 items-center justify-center rounded-lg">
              <Package2 className="h-4 w-4 text-white" />
            </div>
            <div>
              <p className="text-sm font-bold leading-none">HaulScout</p>
              <p className="text-muted-foreground mt-0.5 text-[10px] leading-none">Operator Console</p>
            </div>
          </Link>
          {/* Desktop collapse toggle */}
          <Button
            variant="ghost"
            size="icon"
            className="hidden h-8 w-8 lg:flex"
            onClick={() => setCollapsed(!collapsed)}
            aria-label={collapsed ? "Expand sidebar" : "Collapse sidebar"}
          >
            {collapsed ? <ChevronRight className="h-4 w-4" /> : <ChevronLeft className="h-4 w-4" />}
          </Button>
          {/* Mobile close */}
          <Button variant="ghost" size="icon" className="h-8 w-8 lg:hidden" onClick={onClose} aria-label="Close menu">
            <X className="h-4 w-4" />
          </Button>
        </div>

        {/* Search — the fastest way to anywhere, so it sits above everything it can reach. */}
        <div className={cn("shrink-0 px-2 pt-3", collapsed && "lg:flex lg:justify-center")}>
          {collapsed ? (
            <>
              <CommandPaletteTrigger compact onOpen={onOpenSearch} className="hidden lg:flex" />
              <CommandPaletteTrigger onOpen={onOpenSearch} className="lg:hidden" />
            </>
          ) : (
            <CommandPaletteTrigger onOpen={onOpenSearch} />
          )}
        </div>

        {/* Nav */}
        <nav className="flex-1 space-y-1 overflow-y-auto px-2 py-4">
          {navGroups.map((group) => (
            <div key={group.label} className="mb-4">
              <p
                className={cn(
                  "text-muted-foreground/70 mb-1 px-3 text-[10px] font-semibold uppercase tracking-widest",
                  collapsed && "lg:hidden"
                )}
              >
                {group.label}
              </p>
              {group.items.map((item) => {
                const isActive = isActiveHref(pathname, item.href);
                return (
                  <Link
                    key={item.href}
                    href={item.href}
                    onClick={onClose}
                    className={cn(
                      "flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                      isActive
                        ? "bg-brand-100 text-brand-700 dark:bg-brand-900 dark:text-brand-100"
                        : "text-muted-foreground hover:bg-accent hover:text-accent-foreground",
                      collapsed && "lg:justify-center lg:px-2"
                    )}
                    title={collapsed ? item.name : undefined}
                  >
                    <item.icon className="h-4 w-4 shrink-0" />
                    <span className={cn("truncate", collapsed && "lg:hidden")}>{item.name}</span>
                  </Link>
                );
              })}
            </div>
          ))}
        </nav>

        {/* Footer */}
        <div className="shrink-0 border-t p-4 pb-[calc(1rem_+_env(safe-area-inset-bottom))]">
          <div className={cn("flex items-center gap-3", collapsed && "lg:justify-center lg:gap-0")}>
            <div className="bg-brand-200 text-brand-700 flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-xs font-bold">
              A
            </div>
            <div className={cn("min-w-0 flex-1", collapsed && "lg:hidden")}>
              <p className="truncate text-xs font-medium">Operator</p>
              <p className="text-muted-foreground truncate text-[10px]">Signed in</p>
            </div>
            <Button
              variant="ghost"
              size="icon"
              className={cn("h-8 w-8 shrink-0", collapsed && "lg:hidden")}
              onClick={handleSignOut}
              title="Sign out"
              aria-label="Sign out"
            >
              <LogOut className="h-4 w-4" />
            </Button>
          </div>
        </div>
      </aside>
    </>
  );
}
