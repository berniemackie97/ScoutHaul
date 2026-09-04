/**
 * Shown while a page's server components fetch.
 *
 * Every page here is `force-dynamic` and awaits several API calls, so without this the console sat
 * on the previous page with nothing indicating a navigation had happened.
 */
export default function DashboardLoading() {
  return (
    <div className="space-y-4 p-1" role="status" aria-label="Loading">
      <div className="bg-muted h-8 w-56 animate-pulse rounded" />
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {[0, 1, 2, 3, 4, 5].map((index) => (
          <div key={index} className="bg-muted h-28 animate-pulse rounded-lg" />
        ))}
      </div>
      <div className="bg-muted h-64 animate-pulse rounded-lg" />
      <span className="sr-only">Loading…</span>
    </div>
  );
}
