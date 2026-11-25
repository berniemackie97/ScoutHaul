import {
  BarChart3,
  FileSpreadsheet,
  Handshake,
  HandCoins,
  Inbox,
  Landmark,
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
import { allows, permission, type Granted, type Permission, type Requirement } from "@/lib/permissions";

/** `requires` is what the page's API is gated on, so a page is offered only to someone it will work for. */
export type NavItem = { name: string; href: string; icon: LucideIcon; keywords?: string[]; requires?: Requirement };
export type NavGroup = { label: string; items: NavItem[] };

const needs = (...keys: Permission[]): Requirement => ({ all: keys });

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
      { name: "Work", href: "/work", icon: Inbox, keywords: ["inbox", "tasks", "to do", "queue", "pack", "ship", "delist", "reconcile"] },
      { name: "Inventory", href: "/inventory", icon: Package, keywords: ["items", "stock", "products"], requires: needs(permission.inventory) },
    ],
  },
  {
    label: "Commerce",
    items: [
      { name: "Orders", href: "/orders", icon: ShoppingCart, keywords: ["sales", "sold", "ship", "pack", "fulfil"], requires: needs(permission.orders) },
      { name: "Offers", href: "/offers", icon: Handshake, keywords: ["bids", "best offer", "negotiate"], requires: needs(permission.orders) },
      { name: "Customers", href: "/customers", icon: Users, keywords: ["buyers", "people", "contacts"], requires: needs(permission.orders) },
      { name: "Channels", href: "/channels", icon: Radio, keywords: ["marketplaces", "ebay", "cross-list", "listings"], requires: needs(permission.listings) },
    ],
  },
  {
    label: "Operations",
    items: [
      { name: "Purchases", href: "/purchases", icon: ReceiptText, keywords: ["lots", "acquisition", "buys", "receipts", "cost"], requires: needs(permission.finance) },
      { name: "Repairs", href: "/repairs", icon: Wrench, keywords: ["bench", "broken", "untested", "fix"], requires: needs(permission.inventory) },
      { name: "Consignments", href: "/consignments", icon: HandCoins, keywords: ["consignor", "on behalf"], requires: needs(permission.inventory) },
      { name: "Suppliers", href: "/suppliers", icon: Truck, keywords: ["sources", "vendors", "where from"], requires: needs(permission.finance) },
    ],
  },
  {
    label: "Data",
    items: [
      { name: "Sync", href: "/sync", icon: RefreshCw, keywords: ["extension", "browser", "scan", "ebay sales", "automatic", "schedule"], requires: needs(permission.listings) },
      { name: "Imports", href: "/imports", icon: FileSpreadsheet, keywords: ["csv", "upload", "ebay report", "history"] },
    ],
  },
  {
    label: "Intelligence",
    items: [
      { name: "Reports", href: "/analytics", icon: BarChart3, keywords: ["analytics", "profit", "numbers", "p&l"], requires: needs(permission.reports) },
      { name: "Sales tax", href: "/tax", icon: Landmark, keywords: ["dr-15", "florida", "return", "filing", "surtax", "use tax", "dor"], requires: needs(permission.finance) },
    ],
  },
  {
    label: "System",
    items: [{ name: "Settings", href: "/settings", icon: Settings, keywords: ["team", "roles", "preferences", "store"], requires: { any: [permission.organization, permission.members, permission.inventory] } }],
  },
];

/** Pages beneath an entry whose API asks for more, or other, than the entry's own. */
export const subPages: { name: string; href: string; requires: Requirement }[] = [
  // Recording a sale is gated on listings by its controller and on money by the action itself.
  { name: "Record a sale", href: "/orders/new", requires: needs(permission.listings, permission.finance) },
  { name: "Record a bundled sale", href: "/orders/bundled", requires: needs(permission.listings, permission.finance) },
  { name: "Orders to match", href: "/imports/orders", requires: needs(permission.finance) },
  { name: "Listings to match", href: "/imports/listings", requires: needs(permission.listings, permission.inventory) },
  { name: "Team & access", href: "/settings/team", requires: needs(permission.members) },
  { name: "Menus & controlled values", href: "/settings/menus", requires: needs(permission.inventory) },
  { name: "Florida registration", href: "/tax/settings", requires: needs(permission.finance) },
];

/** The page an address belongs to and what opening it needs — the most specific match wins. */
export function pageAt(pathname: string): { name: string; requires?: Requirement } | null {
  const pages = [...subPages, ...navGroups.flatMap((group) => group.items)];
  return pages.filter((page) => isActiveHref(pathname, page.href)).sort((left, right) => right.href.length - left.href.length)[0] ?? null;
}

/** The nav as this person may use it: entries they cannot open are left out, and so are groups left empty. */
export function visibleGroups(granted: Granted): NavGroup[] {
  return navGroups
    .map((group) => ({ ...group, items: group.items.filter((item) => allows(granted, item.requires)) }))
    .filter((group) => group.items.length > 0);
}

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
