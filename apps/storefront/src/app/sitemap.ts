import type { MetadataRoute } from "next";
import { getAllListings } from "@/lib/catalog";
import { STORE } from "@/lib/store";

// force-dynamic: the sitemap reflects the live shelf, which changes as items sell and list. A stale cached sitemap
// would advertise sold-out URLs and miss fresh ones.
export const dynamic = "force-dynamic";
export const revalidate = 0;

/** Public, indexable marketing/catalog routes. Private shopper surfaces are excluded (see robots.ts). */
const STATIC_ROUTES: { path: string; priority: number; changeFrequency: MetadataRoute.Sitemap[number]["changeFrequency"] }[] = [
  { path: "/", priority: 1, changeFrequency: "daily" },
  { path: "/products", priority: 0.9, changeFrequency: "daily" },
  { path: "/categories", priority: 0.7, changeFrequency: "weekly" },
  { path: "/about", priority: 0.5, changeFrequency: "monthly" },
  { path: "/grading", priority: 0.5, changeFrequency: "monthly" },
  { path: "/repairs", priority: 0.5, changeFrequency: "monthly" },
  { path: "/consignment", priority: 0.5, changeFrequency: "monthly" },
  { path: "/contact", priority: 0.4, changeFrequency: "monthly" },
  { path: "/policies/shipping", priority: 0.3, changeFrequency: "yearly" },
];

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  const base = STORE.siteUrl;
  const now = new Date();

  const listings = await getAllListings();

  const staticEntries: MetadataRoute.Sitemap = STATIC_ROUTES.map((route) => ({
    url: `${base}${route.path}`,
    lastModified: now,
    changeFrequency: route.changeFrequency,
    priority: route.priority,
  }));

  // No per-category entries. /categories/{slug} is nothing but a redirect to /products?category=…,
  // so every one of these advertised a URL that immediately sends the crawler somewhere else. The
  // /categories index is listed above and links to them.

  const productEntries: MetadataRoute.Sitemap = listings.map((listing) => ({
    url: `${base}/products/${listing.slug}`,
    lastModified: now,
    changeFrequency: "weekly",
    priority: 0.8,
    ...(listing.image ? { images: [listing.image] } : {}),
  }));

  return [...staticEntries, ...productEntries];
}
