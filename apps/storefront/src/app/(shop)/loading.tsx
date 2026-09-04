/**
 * Shown while a shop page fetches. The catalog routes are dynamic and wait on the API, so without
 * this a shopper sat on the previous page with nothing to say a navigation had happened.
 */
export default function ShopLoading() {
  return (
    <div className="mx-auto max-w-6xl px-6 py-16" role="status" aria-label="Loading">
      <div className="h-4 w-32 animate-pulse rounded bg-ink/10" />
      <div className="mt-6 h-10 w-2/3 animate-pulse rounded bg-ink/10" />
      <div className="mt-12 grid gap-6 sm:grid-cols-2 lg:grid-cols-4">
        {[0, 1, 2, 3, 4, 5, 6, 7].map((index) => (
          <div key={index} className="aspect-[3/4] animate-pulse rounded bg-ink/10" />
        ))}
      </div>
      <span className="sr-only">Loading…</span>
    </div>
  );
}
