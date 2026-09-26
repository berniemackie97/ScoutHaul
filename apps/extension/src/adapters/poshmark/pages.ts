import { AdapterError, type PageResponse } from "../types";

/** What a Poshmark response turned out to be, before anything reads it. */
export function assertReadable(response: PageResponse): void {
  if (response.status === 0) {
    throw new AdapterError("Network", "Poshmark could not be reached.");
  }

  if (/poshmark\.com\/login/i.test(response.url)) {
    throw new AdapterError("SignedOut", "The browser is signed out of Poshmark.");
  }

  if (response.status === 429) {
    throw new AdapterError("RateLimited", "Poshmark asked for a slower pace.");
  }

  if (response.status === 403 || /captcha|unusual activity|are you a robot/i.test(response.body.slice(0, 20_000))) {
    throw new AdapterError("Challenge", "Poshmark showed a security check.");
  }

  if (response.status >= 500) {
    throw new AdapterError("Network", `Poshmark had a problem (${response.status}).`);
  }

  if (response.status !== 200) {
    throw new AdapterError("Error", `Poshmark answered ${response.status}.`);
  }
}

export function siteChanged(page: string): AdapterError {
  return new AdapterError("SiteChanged", `Poshmark's ${page} page no longer carries its data where expected.`);
}
