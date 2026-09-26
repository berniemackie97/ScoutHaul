import { redirect } from "next/navigation";
import { refusal } from "@/lib/page-access";

// Single-item add is now one mode of the unified Add inventory flow.
export default async function NewInventoryItemRedirect() {
  const refused = await refusal("/inventory/new");
  if (refused) return refused;

  redirect("/inventory/add?mode=single");
}
