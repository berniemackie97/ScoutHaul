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

/** Depop's header, which links to the seller's Likes and to /products/create/ — the Sell button, not a listing. */
function header(username = "overhaultrading"): string {
  return `<header data-testid="header"><a aria-label="Likes" href="/${username}/likes/">Likes</a><a href="/products/create/">Sell now</a></header>`;
}

/** A shop as it is once its grid has been drawn in: an ordered list of tiles, each linking to its listing. */
export function shopPage(slugs: string[]): string {
  const tiles = slugs
    .map((slug) => `<li class="styles-module__Xq1r2a__listItem"><a aria-label="item listed by overhaultrading" class="styles-module__BewhRW__unstyledLink" href="/products/${slug}/"><img src="https://media-photos.depop.com/${slug}/P0.jpg"></a><p>$8.00</p></li>`)
    .join("");
  return `<!doctype html><html><body>${header()}<main><ol class="styles-module__Xq1r2a__grid">${tiles}</ol></main></body></html>`;
}

export interface FixtureSale {
  id: string;
  buyer?: string;
  soldOn?: string;
  status?: string;
  total?: string;
  method?: string;
  carrier?: string | null;
  items?: number;
}

const location = (text: string, show = false) =>
  `<span class="styles-module__RzKrGa__locationDetails print-module__9bTIlq__textOverflowVisible"${show ? ' data-print="show"' : ""}>${text}</span>`;

function soldRow(sale: FixtureSale): string {
  const buyer = sale.buyer ?? "floralfan";
  const items = sale.items ?? 1;
  const label = `View receipt ${sale.id} to buyer ${buyer}`;
  const carrier = sale.carrier === null ? "" : `<p class="_caption-lg-regular_r8lx6_16 styles-module__PiTQza__subtitle">${sale.carrier ?? "USPS"}</p>`;
  return `<li class="styles-module__ednNKG__receiptsListWrapper"><div class="styles-module__ednNKG__rowWrapper"><div class="styles-module__IOys8G__row">`
    + `<div class="styles-module__ednNKG__pictureWrapper"><a aria-label="${label}" class="styles-module__BewhRW__unstyledLink" href="/sellinghub/sold-items/${sale.id}/"><img loading="lazy" alt="Product Image" src="https://media-photos.depop.com/P0.jpg"></a></div>`
    + `<div class="styles-module__ednNKG__wrapper"><a aria-label="${label}" class="styles-module__BewhRW__unstyledLink" href="/sellinghub/sold-items/${sale.id}/">`
    + `<div class="styles-module__RzKrGa__wrapper styles-module__RzKrGa__seller"><p class="_body-sm-regular_r8lx6_12"><span class="_body-sm-bold_r8lx6_13">${items}</span> item${items === 1 ? "" : "s"} sold to <span class="_body-sm-bold_r8lx6_13">@${buyer}</span></p>`
    + `<p class="_body-sm-regular_r8lx6_12 styles-module__RzKrGa__location">${location("12 Elm St", true)}${location("Springfield, IL")}${location("62701-1234")}${location("US", true)}</p></div>`
    + `<div class="styles-module__ednNKG__hideOnMobile"><div><div class="styles-module__PiTQza__container"><div><p class="_body-sm-regular_r8lx6_12"><span class="styles-module__2Qgiea__soldOnText">Sold on </span><span>${sale.soldOn ?? "09/20/2026"} </span></p><p class="_caption-lg-regular_r8lx6_16"><span>7 days ago</span></p></div></div></div></div>`
    + `<div class="styles-module__3nbyCW__wrapper"><div class="styles-module__PiTQza__container"><div><p class="_body-sm-regular_r8lx6_12">${sale.method ?? "Depop shipping"}</p>${carrier}</div></div></div>`
    + `<div class="styles-module__ednNKG__hideOnMobile" data-print="hide"><div><div class="styles-module__PiTQza__container"><div><p class="_body-sm-regular_r8lx6_12">${sale.status ?? "Shipped"}</p></div></div></div></div>`
    + `<div class="styles-module__uFcHHG__wrapper"><div class="styles-module__PiTQza__container"><div><p class="_body-sm-bold_r8lx6_13">${sale.total ?? "US$9.15"}</p><p class="_caption-lg-regular_r8lx6_16">Depop Payments</p></div></div></div></a>`
    + `<div class="styles-module__dXmIQq__container" data-print="hide"><a class="styles-module__BewhRW__unstyledLink" aria-label="Message user" href="/messages/create/?userId=1&amp;productId=2"><svg><path d="M1 1"></path></svg>Message buyer</a></div></div>`
    + `<div class="styles-module__ednNKG__arrowWrapper"><a aria-label="${label}" class="styles-module__BewhRW__unstyledLink" href="/sellinghub/sold-items/${sale.id}/"><svg><title>${label}</title></svg></a></div>`
    + `</div></div></li>`;
}

/** Seller Hub's sold list once its rows have been drawn in. Its column headings are there even with nothing sold. */
export function soldListPage(sales: FixtureSale[]): string {
  const tabs = ["All", "To ship", "In transit", "Canceled", "Completed"].map((tab) => `<li><button>${tab}</button></li>`).join("");
  const headings = ["Buyer info", "Sold on", "Shipping", "Shipping status", "Total", "Actions"].map((heading) => `<p>${heading}</p>`).join("");
  return `<!doctype html><html><body>${header()}<main><h1>Sold</h1><ul>${tabs}</ul><div>${headings}</div><ul>${sales.map(soldRow).join("")}</ul></main></body></html>`;
}

/** A sold list page before its rows are drawn in, or after Depop changed it: no headings, no rows. */
export function soldListShell(): string {
  return `<!doctype html><html><body>${header()}<main><h1>Sold</h1></main></body></html>`;
}

export interface FixtureReceiptProduct {
  slug: string;
  title: string;
  price: string;
  size?: string;
  brand?: string;
}

export interface FixtureReceipt {
  buyerName?: string | null;
  buyer?: string;
  soldOn?: string;
  status?: string;
  deliveredOn?: string | null;
  tracking?: string | null;
  itemsPrice?: string;
  fees?: [string, string][];
  total?: string;
  paidOut?: boolean;
  buyerShipping?: string | null;
  products?: FixtureReceiptProduct[];
}

/** A sale's own page: the sold list with the sale's receipt open over it, as Depop draws it. */
export function receiptPage(receipt: FixtureReceipt): string {
  const buyer = receipt.buyer ?? "floralfan";
  const name = receipt.buyerName === undefined ? "Jordan Rivera" : receipt.buyerName;
  const products = (receipt.products ?? [{ slug: "overhaultrading-old-navy-floral-5db8", title: "Old Navy floral shirt #floral #buttonup", price: "$10.00" }])
    .map((product) => `<div class="styles-module__wd3-qa__container"><a aria-label="View product details for ${product.title.slice(0, 20)}..." class="styles-module__BewhRW__unstyledLink" href="/products/${product.slug}/">`
      + `<div><div><img class="styles-module__wd3-qa__image" loading="lazy" alt="${product.title}" width="72" height="72" src="https://media-photos.depop.com/P0.jpg">`
      + `<div><p class="_caption-lg-regular_r8lx6_16" data-testid="receipt-product-description">${product.title.slice(0, 20)}...</p>`
      + `<div><p class="_body-sm-bold_r8lx6_13">${product.size ?? "XL"}</p><div><p class="_body-sm-bold_r8lx6_13" aria-description="Price">${product.price}</p></div><p class="_body-sm-bold_r8lx6_13">${product.brand ?? "Old Navy"}</p></div></div></div></div></a></div>`)
    .join("");
  const delivered = receipt.deliveredOn === null ? ""
    : `<div class="styles-module__y9XCtq__container"><div><div><svg></svg></div><p class="_body-sm-bold_r8lx6_13">Delivered on</p><p class="_body-sm-regular_r8lx6_12">${receipt.deliveredOn ?? "09/24/2026"}</p>`
      + (receipt.tracking === null ? "" : `<a href="https://tools.usps.com/go/TrackConfirmAction_input?origTrackNum=${receipt.tracking ?? "9400100000000000000001"}" target="_blank" rel="noopener"><span>Where is my package?</span></a><p class="_body-sm-bold_r8lx6_13">USPS Ref</p><p class="_body-sm-regular_r8lx6_12">${receipt.tracking ?? "9400100000000000000001"}</p>`)
      + `</div></div>`;
  const row = (label: string, value: string) =>
    `<tr class="styles-module__uGuzGq__row"><td class="styles-module__uGuzGq__label"><p class="_caption-lg-regular_r8lx6_16">${label}</p></td><td class="styles-module__uGuzGq__price"><p class="_caption-lg-regular_r8lx6_16">${value}</p></td></tr>`;
  const fees = (receipt.fees ?? [["Depop fee", "US$0.00"], ["Payment fee", "- US$0.85"]]).map(([label, value]) => row(label, value)).join("");
  const total = receipt.total ?? "US$9.15";
  const paid = receipt.paidOut === false
    ? `<p class="_body-sm-regular_r8lx6_12"><b>Payment pending</b></p>`
    : `<p class="_body-sm-regular_r8lx6_12"><b>Payment received</b></p><p class="_body-sm-regular_r8lx6_12">${total} sent to your bank account</p>`;
  const shipping = receipt.buyerShipping === null ? "" : `<p class="_caption-lg-regular_r8lx6_16">Buyer paid ${receipt.buyerShipping ?? "US$1.99"} for shipping</p>`;
  const aside = `<aside class="styles-module__yTRhGG__wrapper" data-testid="simpleDrawer-visible" role="dialog" aria-modal="true" aria-label="View Receipt modal" tabindex="-1">`
    + `<div><button aria-label="Close"><svg></svg></button></div><div>`
    + `<div><a aria-label="View @${buyer}&#x27;s shop" href="/${buyer}/"><div data-testid="avatar"><div data-testid="avatar__initials">JR</div></div>`
    + `<div>${name === null ? "" : `<h3 class="_heading-sm_r8lx6_8">${name}</h3>`}<p class="_caption-lg-regular_r8lx6_16">@${buyer}</p></div></a></div>`
    + `<div><h2 class="_heading-md_r8lx6_9">1 item sold</h2><p class="_caption-lg-regular_r8lx6_16">Sold on ${receipt.soldOn ?? "09/20/2026"}</p></div>`
    + products
    + `<div><h3 class="_heading-sm_r8lx6_8">Status</h3><p class="_body-sm-regular_r8lx6_12">${receipt.status ?? "Delivered"}</p></div>`
    + delivered
    + `<div><div>${paid}</div><p>Check the app to see your statement</p><div><table class="styles-module__uGuzGq__table"><tbody>`
    + row("Items Price", receipt.itemsPrice ?? "US$10.00") + fees + row("Total", total)
    + `</tbody></table></div><button disabled=""><span>Refund in the app</span></button>${shipping}</div>`
    + `</div></aside>`;
  return soldListPage([{ id: "475700000001", buyer }]).replace("</main>", `</main>${aside}`);
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
