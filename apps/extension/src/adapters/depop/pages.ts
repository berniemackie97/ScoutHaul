import { AdapterError, type PageResponse } from "../types";

/** What a Depop response turned out to be, before anything reads it. */
export function assertReadable(response: PageResponse): void {
  if (response.status === 0) {
    throw new AdapterError("Network", "Depop could not be reached.");
  }

  if (/depop\.com\/(login|signup)/i.test(response.url)) {
    throw new AdapterError("SignedOut", "The browser is signed out of Depop.");
  }

  if (response.status === 429) {
    throw new AdapterError("RateLimited", "Depop asked for a slower pace.");
  }

  // Depop sits behind Cloudflare, which answers a challenge as a page of its own rather than an error.
  if (response.status === 403 || challenged(response.body)) {
    throw new AdapterError("Challenge", "Depop showed a security check.");
  }

  if (response.status >= 500) {
    throw new AdapterError("Network", `Depop had a problem (${response.status}).`);
  }

  if (response.status !== 200) {
    throw new AdapterError("Error", `Depop answered ${response.status}.`);
  }
}

/**
 * A security check, told apart from a listing that merely mentions one. Matching the word "captcha" anywhere in a
 * page would stop a scan over someone's description of a puzzle book.
 */
function challenged(body: string): boolean {
  const head = body.slice(0, 20_000);
  return /cdn-cgi\/challenge-platform|cf-chl-|<title>[^<]*(just a moment|attention required|access denied)/i.test(head);
}

export function siteChanged(page: string): AdapterError {
  return new AdapterError("SiteChanged", `Depop's ${page} page no longer carries its data where expected.`);
}
