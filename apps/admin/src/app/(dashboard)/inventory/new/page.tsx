import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { ItemCreator } from "./item-creator";

export const dynamic = "force-dynamic";

export default function NewInventoryItemPage() {
  return (
    <div className="space-y-6">
      <div>
        <Link href="/inventory" className="text-muted-foreground inline-flex items-center gap-1 text-sm hover:text-foreground">
          <ArrowLeft className="h-4 w-4" /> Inventory
        </Link>
        <h1 className="mt-2 text-2xl font-semibold tracking-tight">Add inventory item</h1>
        <p className="text-muted-foreground mt-1 text-sm">Record a single item you bought or acquired. It enters inventory immediately and can be listed from its detail page.</p>
      </div>
      <ItemCreator />
    </div>
  );
}
