import { Card } from "@overhaul/ui";
import { PageHeader } from "@/components/list/list-search";
import { getApiClient } from "@/lib/api";
import { errorMessage } from "@/lib/api-error";
import { refusal } from "@/lib/page-access";
import { WorkInbox } from "./work-inbox";

export const dynamic = "force-dynamic";

/** Every open task in the order a sale moves, each linked to the one place it is done. */
export default async function WorkPage({ searchParams }: { searchParams: Promise<{ lane?: string }> }) {
  const refused = await refusal("/work");
  if (refused) return refused;

  const { lane } = await searchParams;
  const api = await getApiClient();
  const { data: inbox, error } = await api.GET("/api/work", {});

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Control"
        title="Work"
        description="Everything that needs a person, in the order a sale moves. Finishing the work clears the task; anything set aside says what it waits on."
      />
      {inbox ? <WorkInbox inbox={inbox} initialLane={lane} /> : <Card className="p-6 text-sm">{errorMessage(error, "Work could not be loaded.")}</Card>}
    </div>
  );
}
