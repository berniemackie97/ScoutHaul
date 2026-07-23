import type { MetadataRoute } from "next";
import { STORE } from "@/lib/store";

/**
 * Crawlers get the whole storefront except the shopper's private surfaces (cart, checkout, account, offers) and the
 * proxy routes — none of which are indexable content, and some of which are per-session. The sitemap is advertised
 * from the canonical www origin.
 */
export default function robots(): MetadataRoute.Robots {
  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: ["/api/", "/cart", "/checkout", "/account", "/offers", "/sign-in"],
    },
    sitemap: `${STORE.siteUrl}/sitemap.xml`,
    host: STORE.siteUrl,
  };
}
