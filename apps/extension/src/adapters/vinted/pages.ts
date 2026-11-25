import { AdapterError, type PageLook, type PageResponse, type Session } from "../types";

export function signInPath(path: string): boolean {
  return /^\/member\/(?:login|signup|register|verif[^/]*|two_factor|password|reset_password)(?:\/|$)/i.test(path);
}

export type MissingResource = "SiteChanged" | "Error";

/**
 * What a Vinted response turned out to be, before anything reads it. Vinted guards itself with
 * DataDome: a request it blocks answers 403 with a pointer to its captcha page — the only place
 * `captcha-delivery.com` appears, since every ordinary page loads DataDome's own script too.
 */
export function assertReadable(response: PageResponse, what: string, missing: MissingResource = "SiteChanged"): void {
  if (response.status === 0) {
    throw new AdapterError("Network", "Vinted could not be reached.");
  }

  if (challenged(response)) {
    throw new AdapterError("Challenge", "Vinted showed a security check.");
  }

  if (response.status === 429) {
    throw new AdapterError("RateLimited", "Vinted asked for a slower pace.");
  }

  if (response.status === 403) {
    throw new AdapterError("Challenge", "Vinted refused the request.");
  }

  if (response.status === 401) {
    throw signedOut();
  }

  if (response.status >= 500) {
    throw new AdapterError("Network", `Vinted returned HTTP ${response.status} while reading ${what}.`);
  }

  if (response.status !== 200) {
    const kind = response.status === 404 || response.status === 410 ? missing : "Error";
    throw new AdapterError(kind, `Vinted returned HTTP ${response.status} while reading ${what}.`);
  }
}

/**
 * Whether a response is a security check rather than what was asked for: DataDome's (a pointer to its captcha page),
 * or Cloudflare's, which Vinted also sits behind. Cloudflare's script loads on ordinary pages too, so only what its
 * challenge page alone carries counts.
 */
export function challenged(response: PageResponse): boolean {
  const head = response.body.slice(0, 20_000);
  return /captcha-delivery\.com/i.test(head) || /_cf_chl_opt|cf-chl-widget|<title>Just a moment\.\.\.<\/title>/i.test(head);
}

/** A response that is not the JSON Vinted's own pages read: Vinted changed its API. */
export function siteChanged(what: string): AdapterError {
  return new AdapterError("SiteChanged", `Vinted returned an unexpected response for ${what}.`);
}

export function signedOut(): AdapterError {
  return new AdapterError("SignedOut", "The browser is signed out of Vinted.");
}

/** Whether a loaded Vinted page is one a signed-out visitor sees. The page says so in the data it embeds. */
export function signedOutPage(page: PageResponse): boolean {
  return signInPath(new URL(page.url, "https://www.vinted.com").pathname)
    || /\\?"isLoggedIn\\?":\s*false/.test(page.body);
}

/** Whether a loaded Vinted page shows someone signed in — it says so in the data it embeds, though not who. */
export function signedInPage(page: PageResponse): boolean {
  return !signedOutPage(page) && /\\?"isLoggedIn\\?":\s*true/.test(page.body);
}

/** A Vinted page as it stands, at no cost: a security check, and whether someone is signed in. */
export function lookOn(page: PageResponse): PageLook {
  if (challenged(page) || page.status === 403) {
    return { challenge: true, signedIn: null };
  }

  return { challenge: false, signedIn: signedOutPage(page) ? false : signedInPage(page) ? true : null };
}

/**
 * Sign-in as a Vinted page already shows it, at no cost to anyone. Signed out is certain from the page; signed in is
 * not enough on its own, because the page does not name the account — so that answer is left to a check that may ask.
 */
export function sessionOnPage(page: PageResponse): Session | null {
  return signedOutPage(page) ? { signedIn: false, username: null, marketplaceUserId: null } : null;
}
