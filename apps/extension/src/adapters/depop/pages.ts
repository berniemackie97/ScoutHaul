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
  if (response.status === 403 || /cf-challenge|captcha|attention required|are you a robot/i.test(response.body.slice(0, 20_000))) {
    throw new AdapterError("Challenge", "Depop showed a security check.");
  }

  if (response.status >= 500) {
    throw new AdapterError("Network", `Depop had a problem (${response.status}).`);
  }

  if (response.status !== 200) {
    throw new AdapterError("Error", `Depop answered ${response.status}.`);
  }
}

export function siteChanged(page: string): AdapterError {
  return new AdapterError("SiteChanged", `Depop's ${page} page no longer carries its data where expected.`);
}
