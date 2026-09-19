import {
  BarChart3,
  FileSpreadsheet,
  Handshake,
  HandCoins,
  LayoutDashboard,
  Package,
  Radio,
  ReceiptText,
  RefreshCw,
  Settings,
  ShoppingCart,
  Truck,
  Users,
  Wrench,
  type LucideIcon,
} from "lucide-react";

export type NavItem = { name: string; href: string; icon: LucideIcon; keywords?: string[] };
export type NavGroup = { label: string; items: NavItem[] };

/**
 * Every place in the portal, once.
 *
 * The sidebar and the command palette both read this. Before there was a palette the sidebar owned
 * the list inline, and the palette would otherwise have needed a second copy that drifted the first
 * time a page was added or renamed. `keywords` are the other words someone might type for a page —
 * nobody searches "Consignments" when what they mean is "stuff I'm selling for a friend".
 */
export const navGroups: NavGroup[] = [
  {
    label: "Control",
    items: [
      { name: "Command Center", href: "/", icon: LayoutDashboard, keywords: ["home", "dashboard", "overview"] },
      { name: "Inventory", href: "/inventory", icon: Package, keywords: ["items", "stock", "products"] },
    ],
  },
  {
    label: "Commerce",
    items: [
      { name: "Orders", href: "/orders", icon: ShoppingCart, keywords: ["sales", "sold", "ship", "pack", "fulfil"] },
      { name: "Offers", href: "/offers", icon: Handshake, keywords: ["bids", "best offer", "negotiate"] },
      { name: "Customers", href: "/customers", icon: Users, keywords: ["buyers", "people", "contacts"] },
      { name: "Channels", href: "/channels", icon: Radio, keywords: ["marketplaces", "ebay", "cross-list", "listings"] },
    ],
  },
  {
    label: "Operations",
    items: [
      { name: "Purchases", href: "/purchases", icon: ReceiptText, keywords: ["lots", "acquisition", "buys", "receipts", "cost"] },
      { name: "Repairs", href: "/repairs", icon: Wrench, keywords: ["bench", "broken", "untested", "fix"] },
      { name: "Consignments", href: "/consignments", icon: HandCoins, keywords: ["consignor", "on behalf"] },
      { name: "Suppliers", href: "/suppliers", icon: Truck, keywords: ["sources", "vendors", "where from"] },
    ],
  },
  {
    label: "Data",
    items: [
      { name: "Sync", href: "/sync", icon: RefreshCw, keywords: ["extension", "browser", "scan", "ebay sales", "automatic", "schedule"] },
      { name: "Imports", href: "/imports", icon: FileSpreadsheet, keywords: ["csv", "upload", "ebay report", "history"] },
    ],
  },
  {
    label: "Intelligence",
    items: [{ name: "Reports", href: "/analytics", icon: BarChart3, keywords: ["analytics", "profit", "numbers", "p&l"] }],
  },
  {
    label: "System",
    items: [{ name: "Settings", href: "/settings", icon: Settings, keywords: ["team", "roles", "preferences", "store"] }],
  },
];

/**
 * Whether a nav entry is the page being shown, or a page beneath it.
 *
 * Matches on a path segment, not a string prefix: a bare prefix would light "/orders" up on a page
 * called "/orders-archive". Home only ever matches itself, or it would be active everywhere.
 */
export function isActiveHref(pathname: string, href: string): boolean {
  if (pathname === href) return true;
  return href !== "/" && pathname.startsWith(`${href}/`);
}
