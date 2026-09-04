import { ErrorPanel } from "@/components/feedback/error-panel";

/** `notFound()` is called on four detail pages; without this they fell back to Next's bare 404, which
 *  also loses the console shell. */
export default function DashboardNotFound() {
  return (
    <ErrorPanel
      title="Not found"
      description="This record does not exist, or it has been deleted. It may have been merged into another one."
    />
  );
}
