import { redirect } from "next/navigation";

// Single-item add is now one mode of the unified Add inventory flow.
export default function NewInventoryItemRedirect() {
  redirect("/inventory/add?mode=single");
}
