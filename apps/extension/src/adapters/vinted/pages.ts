import { AdapterError, type PageResponse } from "../types";

/**
 * What a Vinted response turned out to be, before anything reads it. Vinted guards itself with
 * DataDome: a request it blocks answers 403 with a pointer to its captcha page — the only place
 * `captcha-delivery.com` appears, since every ordinary page loads DataDome's own script too.
 */
export function assertReadable(response: PageResponse): void {
  if (response.status === 0) {
    throw new AdapterError("Network", "Vinted could not be reached.");
  }

  if (/captcha-delivery\.com/i.test(response.body.slice(0, 20_000))) {
    throw new AdapterError("Challenge", "Vinted showed a security check.");
  }

  if (response.status === 401) {
    throw new AdapterError("SignedOut", "The browser is signed out of Vinted.");
  }

  if (response.status === 429) {
    throw new AdapterError("RateLimited", "Vinted asked for a slower pace.");
  }

  if (response.status === 403) {
    throw new AdapterError("Challenge", "Vinted refused the request.");
  }

  if (response.status >= 500) {
    throw new AdapterError("Network", `Vinted had a problem (${response.status}).`);
  }

  if (response.status !== 200) {
    throw new AdapterError("Error", `Vinted answered ${response.status}.`);
  }
}

/** A response that is not the JSON Vinted's own pages read: Vinted changed its API. */
export function siteChanged(what: string): AdapterError {
  return new AdapterError("SiteChanged", `Vinted's ${what} no longer come back in the shape expected.`);
}

/** Whether a loaded Vinted page is one a signed-out visitor sees. The page says so in the data it embeds. */
export function signedOutPage(page: PageResponse): boolean {
  return /\/member\/(login|signup)/i.test(new URL(page.url, "https://www.vinted.com").pathname)
    || /\\?"isLoggedIn\\?":\s*false/.test(page.body);
}
