/**
 * A listing's pictures, read from its own item page.
 *
 * Manage active listings carries one picture per listing, which is not a gallery. The item page carries all of
 * them, on the carousel buttons as `data-zoom-src`. That attribute is the only reliable marker: the same page also
 * holds the seller's avatar and ten recommended listings' images, and a listing must never show a stranger's photo.
 * The structured data block looks tempting and is not — eBay truncates it to the first five.
 */

/** eBay names a picture's size in its address; `s-l1600` is the one worth keeping. */
export function fullSize(url: string): string {
  return url.replace(/\/s-l\d+(\.\w+)(?=$|\?)/i, "/s-l1600$1");
}

const zoomable = /data-zoom-src=["']?(https:\/\/i\.ebayimg\.com\/images\/g\/[A-Za-z0-9~_-]+\/s-l\d+\.(?:jpg|jpeg|png|webp))/gi;

/** The listing's pictures in the order it shows them, or empty when the page carries none. */
export function readGallery(html: string): string[] {
  const urls = new Set<string>();
  for (const match of html.matchAll(zoomable)) {
    urls.add(fullSize(match[1]!));
  }

  return [...urls];
}
