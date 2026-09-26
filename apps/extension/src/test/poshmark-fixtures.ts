/**
 * Poshmark pages in the shape it serves them: a state object written into the HTML, with each closet listing whole
 * inside it. The field names and values here were taken from a real closet page, not invented.
 */

export interface FixtureListing {
  id: string;
  title: string;
  price: number;
  brand?: string | null;
  size?: string;
  colors?: string[];
  condition?: string;
  description?: string;
  category?: string;
  available?: number;
  sold?: number;
  inventoryStatus?: string;
  status?: string;
}

export function closetPage(listings: FixtureListing[], more: boolean | string = false): string {
  const data = listings.map((listing) => ({
    id: listing.id,
    title: listing.title,
    status: listing.status ?? "published",
    brand: listing.brand === undefined ? "Ripple Junction" : listing.brand,
    size: listing.size ?? "XL",
    condition: listing.condition ?? "uf",
    description: listing.description ?? "Short-sleeve tee with a large print.",
    category: "Other",
    category_v2: listing.category ?? "Shirts",
    department: { display: "Men", id: "01008c10d97b4e1245005764", message_id: "men", slug: "Men" },
    colors: (listing.colors ?? ["Blue", "White"]).map((name) => ({ message_id: name.toLowerCase(), name, rgb: "#137fc1" })),
    price_amount: listing.price,
    price: listing.price,
    original_price_amount: 0,
    first_published_at: "2026-07-05T10:13:05-07:00",
    created_at: "2026-07-05T10:13:05-07:00",
    cover_shot: {
      url: `https://di2ponv0v5otw.cloudfront.net/posts/2026/07/05/${listing.id}/m_cover.jpg`,
      url_large: `https://di2ponv0v5otw.cloudfront.net/posts/2026/07/05/${listing.id}/l_cover.jpg`,
    },
    picture_url: `https://di2ponv0v5otw.cloudfront.net/posts/2026/07/05/${listing.id}/m_cover.jpg`,
    inventory: {
      status: listing.inventoryStatus ?? "available",
      multi_item: false,
      size_quantities: [
        {
          size_id: listing.size ?? "XL",
          quantity_available: listing.available ?? 1,
          quantity_reserved: 0,
          quantity_sold: listing.sold ?? 0,
        },
      ],
    },
  }));

  return statePage({
    auth: { isUserLoggedIn: true, identity: "6a28968fe948982883b29ccf", isGuestLoggedIn: false },
    userCookies: { userInfo: { dh: "overhaultrading", uid: "6a28968fe948982883b29ccf" } },
    $_closet: {
      closetUserInfo: { data: { id: "6a28968fe948982883b29ccf", username: "overhaultrading" } },
      listingsPostData: {
        data,
        more: {
          index: "posts",
          is_next_max_id_present: more !== false,
          // Poshmark sends its own cursor only when there is a next page.
          ...(typeof more === "string" ? { next_max_id: more } : {}),
          total: data.length,
        },
      },
    },
  });
}

/** A closet page whose state carries no list at all — what a changed site looks like. */
export function closetPageWithoutListings(): string {
  return statePage({ auth: { isUserLoggedIn: true }, $_closet: { listingsPostData: { more: {} } } });
}

/** The home page, which is where the adapter asks who is signed in. */
export function homePage(handle: string | null): string {
  return statePage({
    auth: handle === null
      ? { isUserLoggedIn: false, identity: null, isGuestLoggedIn: true }
      : { isUserLoggedIn: true, identity: "6a28968fe948982883b29ccf", isGuestLoggedIn: false },
    userCookies: handle === null ? {} : { userInfo: { dh: handle, uid: "6a28968fe948982883b29ccf" } },
  });
}

/** Poshmark writes its state into a script tag, with the page's markup around it. */
function statePage(state: unknown): string {
  return `<!doctype html><html><body><div id="app"></div>`
    + `<script>window.__INITIAL_STATE__ = ${JSON.stringify(state)};window.__OTHER__={"a":"}"};</script>`
    + `</body></html>`;
}
