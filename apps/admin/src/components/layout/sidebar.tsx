"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { UserButton } from "@clerk/nextjs";
import {
  LayoutDashboard, Package, ShoppingCart, Users, Wrench,
  HandCoins, Truck, BarChart3, FileText, Settings,
  ChevronLeft, ChevronRight, Menu, Package2,
} from "lucide-react";
import { useState } from "react";
import { cn, Button, Separator } from "@overhaul/ui";

const navItems = [
  { name: "Dashboard", href: "/", icon: LayoutDashboard },
  { name: "Inventory", href: "/inventory", icon: Package },
  { name: "Orders", href: "/orders", icon: ShoppingCart },
  { name: "Customers", href: "/customers", icon: Users },
  { name: "Repairs", href: "/repairs", icon: Wrench },
  { name: "Consignments", href: "/consignments", icon: HandCoins },
  { name: "Suppliers", href: "/suppliers", icon: Truck },
  { name: "Analytics", href: "/analytics", icon: BarChart3 },
  { name: "Blog", href: "/blog", icon: FileText },
  { name: "Settings", href: "/settings", icon: Settings },
];

export function Sidebar() {
  const pathname = usePathname();
  const [collapsed, setCollapsed] = useState(false);

  return (
    <aside
      className={cn(
        "flex flex-col border-r bg-card transition-all duration-200 shrink-0",
        collapsed ? "w-[68px]" : "w-[240px]"
      )}
    >
      {/* Header */}
      <div className="flex h-16 items-center justify-between px-4 border-b">
        {!collapsed && (
          <Link href="/" className="flex items-center gap-2">
            <div className="h-8 w-8 rounded-lg bg-brand-600 flex items-center justify-center">
              <Package2 className="h-4 w-4 text-white" />
            </div>
            <div>
              <p className="font-bold text-sm leading-none">Overhaul</p>
              <p className="text-[10px] text-muted-foreground leading-none mt-0.5">Admin Portal</p>
            </div>
          </Link>
        )}
        <Button
          variant="ghost"
          size="icon"
          className="h-8 w-8"
          onClick={() => setCollapsed(!collapsed)}
        >
          {collapsed ? <ChevronRight className="h-4 w-4" /> : <ChevronLeft className="h-4 w-4" />}
        </Button>
      </div>

      {/* Nav */}
      <nav className="flex-1 px-2 py-4 space-y-1 overflow-y-auto">
        {navItems.map((item) => {
          const isActive = pathname === item.href || (item.href !== "/" && pathname.startsWith(item.href));
          return (
            <Link
              key={item.href}
              href={item.href}
              className={cn(
                "flex items-center gap-3 rounded-lg px-3 py-2 text-sm font-medium transition-colors",
                isActive
                  ? "bg-brand-100 text-brand-700 dark:bg-brand-900 dark:text-brand-100"
                  : "text-muted-foreground hover:bg-accent hover:text-accent-foreground",
                collapsed && "justify-center px-2"
              )}
              title={collapsed ? item.name : undefined}
            >
              <item.icon className="h-4 w-4 shrink-0" />
              {!collapsed && <span>{item.name}</span>}
            </Link>
          );
        })}
      </nav>

      {/* Footer */}
      <div className="border-t p-4">
        <div className={cn("flex items-center", collapsed ? "justify-center" : "gap-3")}>
          {process.env.NEXT_PUBLIC_CLERK_PUBLISHABLE_KEY ? (
            <UserButton afterSignOutUrl="/" />
          ) : (
            <div className="h-8 w-8 rounded-full bg-brand-200 flex items-center justify-center text-xs font-bold text-brand-700">A</div>
          )}
          {!collapsed && (
            <div className="min-w-0">
              <p className="text-xs font-medium truncate">Admin User</p>
              <p className="text-[10px] text-muted-foreground truncate">Owner</p>
            </div>
          )}
        </div>
      </div>
    </aside>
  );
}
