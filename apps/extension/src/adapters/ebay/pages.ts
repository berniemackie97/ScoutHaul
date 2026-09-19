import { AdapterError, type PageResponse } from "../types";

/**
 * What an eBay response turned out to be, before anything reads it. A sign-in page, a security
 * check or a throttle is a different failure from a page whose layout changed, and each is handled
 * differently — so they are told apart here, once, for every request.
 */
export function assertReadable(response: PageResponse): void {
  if (response.status === 0) {
    throw new AdapterError("Network", "eBay could not be reached.");
  }

  if (/signin\.ebay\./i.test(response.url)) {
    throw new AdapterError("SignedOut", "The browser is signed out of eBay.");
  }

  if (response.status === 429) {
    throw new AdapterError("RateLimited", "eBay asked for a slower pace.");
  }

  if (/\/splashui\/challenge|\/splashui\/captcha/i.test(response.url)
      || /Pardon Our Interruption|Checking your browser before you access eBay/i.test(response.body.slice(0, 20_000))) {
    throw new AdapterError("Challenge", "eBay showed a security check.");
  }

  if (response.status === 403) {
    throw new AdapterError("Challenge", "eBay refused the request.");
  }

  if (response.status >= 500) {
    throw new AdapterError("Network", `eBay had a problem (${response.status}).`);
  }

  if (response.status !== 200) {
    throw new AdapterError("Error", `eBay answered ${response.status}.`);
  }
}

/** The page loaded but its data was not where the adapter expects it: eBay changed the page. */
export function siteChanged(page: string): AdapterError {
  return new AdapterError("SiteChanged", `eBay's ${page} page no longer carries its data where expected.`);
}
