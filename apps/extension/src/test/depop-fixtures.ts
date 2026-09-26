/**
 * Depop pages in the shape it serves them. The structured data here carries the same fields a real listing page
 * does — name, description, image, brand, offers with price, availability and condition — and the size and
 * condition line is written the way the page writes it.
 */

export interface FixtureProduct {
  slug: string;
  title: string;
  price: string;
  brand?: string | null;
  size?: string | null;
  condition?: string | null;
  description?: string;
  images?: number;
  sold?: boolean;
}

export function shopPage(slugs: string[]): string {
  const tiles = slugs
    .map((slug) => `<li><a class="unstyledLink" href="/products/${slug}/"><img src="https://media-photos.depop.com/${slug}/P0.jpg"></a><p>$8.00</p></li>`)
    .join("");
  return `<!doctype html><html><body><ul>${tiles}</ul></body></html>`;
}

export function productPage(product: FixtureProduct): string {
  const images = Array.from({ length: product.images ?? 2 }, (_, index) =>
    `https://media-photos.depop.com/${product.slug}/P${index}.jpg`);
  const structured = {
    name: product.title,
    description: product.description ?? "Cotton blend. &amp; barely worn.",
    "@context": "https://schema.org",
    "@type": "Product",
    image: images,
    brand: product.brand === null ? undefined : { "@type": "Brand", name: product.brand ?? "Lane Bryant" },
    offers: {
      "@type": "Offer",
      url: `https://www.depop.com/products/${product.slug}/`,
      priceCurrency: "USD",
      price: product.price,
      availability: product.sold ? "https://schema.org/SoldOut" : "https://schema.org/InStock",
      itemCondition: "https://schema.org/UsedCondition",
    },
  };

  // Depop states size and condition on the page itself, not in its structured data.
  const attributes = [
    product.size === null ? "" : `Size ${product.size ?? "26"}`,
    product.condition === null ? "" : `${product.condition ?? "Excellent"} condition`,
    product.brand ?? "Lane Bryant",
  ].filter(Boolean).join("</span> • <span>");

  return `<!doctype html><html><head>`
    + `<script type="application/ld+json">{"@type":"BreadcrumbList"}</script>`
    + `<script type="application/ld+json">${JSON.stringify(structured)}</script>`
    + `</head><body><h1>${product.title}</h1><div><span>${attributes}</span></div></body></html>`;
}

/**
 * The home page, where the adapter asks who is signed in. A signed-in page carries the seller's own Likes link in
 * its header — and, like any Depop page, may also link to other people's shops, which is the whole point of the
 * test that follows it.
 */
export function homePage(username: string | null, alsoLinking: string[] = []): string {
  const others = alsoLinking.map((handle) => `<a href="/${handle}/">Another seller</a>`).join("");
  return username === null
    ? `<!doctype html><html><body>${others}<a href="/signup/?redirect=%2Fproducts%2Fcreate%2F">Sign up to sell</a></body></html>`
    : `<!doctype html><html><body><header>${others}<a aria-label="Likes" href="/${username}/likes/">Likes</a></header></body></html>`;
}

/** What Cloudflare answers with when it stops a request. */
export function challengePage(): string {
  return `<!doctype html><html><head><title>Just a moment...</title></head>`
    + `<body><script src="/cdn-cgi/challenge-platform/h/b/scripts/jsd/main.js"></script></body></html>`;
}
