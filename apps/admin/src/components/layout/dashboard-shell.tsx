"use client";

import { useEffect, useState, type ReactNode } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Menu, Package2 } from "lucide-react";
import { Button } from "@overhaul/ui";
import { CommandPalette } from "@/components/search/command-palette";
import type { Operator } from "@/lib/session";
import { CommandPaletteTrigger } from "@/components/search/command-palette-trigger";
import { Sidebar } from "./sidebar";
import { SessionKeepAlive } from "./session-keep-alive";

/** Typing into a field must never be hijacked by a single-key shortcut like "/". */
function isEditable(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  return target.isContentEditable || ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName);
}

/**
 * Dashboard chrome. On desktop the sidebar is in-flow; below lg it collapses to a drawer opened from a compact top
 * bar. Owns the drawer's open state (shared with the top-bar trigger), closes it on navigation, and locks body
 * scroll while it is open.
 */
export function DashboardShell({ children, operator }: { children: ReactNode; operator: Operator | null }) {
  const pathname = usePathname();
  const [mobileOpen, setMobileOpen] = useState(false);
  const [paletteOpen, setPaletteOpen] = useState(false);

  // ⌘K / Ctrl+K from anywhere, including from inside a field — it is not a text-editing chord.
  // "/" too, the way most consoles do it, but only when the key would not otherwise be typed.
  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if ((event.metaKey || event.ctrlKey) && !event.altKey && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setPaletteOpen((current) => !current);
        return;
      }
      if (event.key === "/" && !event.metaKey && !event.ctrlKey && !event.altKey && !isEditable(event.target)) {
        event.preventDefault();
        setPaletteOpen(true);
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  useEffect(() => {
    setMobileOpen(false);
  }, [pathname]);

  useEffect(() => {
    if (!mobileOpen) return;
    const previous = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = previous;
    };
  }, [mobileOpen]);

  return (
    <div className="flex h-screen overflow-hidden">
      <SessionKeepAlive />
      <CommandPalette open={paletteOpen} onOpenChange={setPaletteOpen} />
      <Sidebar
        operator={operator}
        mobileOpen={mobileOpen}
        onClose={() => setMobileOpen(false)}
        onOpenSearch={() => {
          setMobileOpen(false);
          setPaletteOpen(true);
        }}
      />
      <div className="flex min-w-0 flex-1 flex-col">
        {/* Mobile top bar — the only way to reach the nav below lg. pt safe-area keeps the menu button clear of the
            iOS status bar / Dynamic Island when launched as an installed PWA. */}
        <header className="bg-card flex h-14 shrink-0 items-center gap-3 border-b px-3 pt-[env(safe-area-inset-top)] lg:hidden">
          <Button
            variant="ghost"
            size="icon"
            className="h-9 w-9"
            onClick={() => setMobileOpen(true)}
            aria-label="Open menu"
          >
            <Menu className="h-5 w-5" />
          </Button>
          <Link href="/" className="flex items-center gap-2">
            <div className="bg-brand-600 flex h-7 w-7 items-center justify-center rounded-lg">
              <Package2 className="h-4 w-4 text-white" />
            </div>
            <span className="text-sm font-bold">HaulScout</span>
          </Link>
          <CommandPaletteTrigger compact onOpen={() => setPaletteOpen(true)} className="ml-auto" />
        </header>
        <main className="flex-1 overflow-y-auto">
          <div className="p-4 pb-[calc(1rem_+_env(safe-area-inset-bottom))] sm:p-6 lg:p-8">{children}</div>
        </main>
      </div>
    </div>
  );
}
