"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Archive, Trash2, TriangleAlert } from "lucide-react";
import { Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label } from "@overhaul/ui";
import { deleteInventoryItem, type InventoryDeleteResult } from "./actions";

export function DeleteInventoryCard({ id, title }: { id: string; title: string }) {
  const router = useRouter();
  const [confirmTitle, setConfirmTitle] = useState("");
  const [result, setResult] = useState<InventoryDeleteResult | null>(null);
  const [isPending, startTransition] = useTransition();
  const confirmed = confirmTitle.trim() === title.trim();

  function run(hard: boolean) {
    if (!confirmed) {
      setResult({ ok: false, message: "Type the item title exactly before deleting or archiving." });
      return;
    }

    startTransition(async () => {
      const next = await deleteInventoryItem(id, hard);
      setResult(next);
      if (next.ok && next.deleted) {
        router.push("/inventory");
        router.refresh();
        return;
      }
      if (next.ok) router.refresh();
    });
  }

  return (
    <Card className="border-destructive/30">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg">
          <TriangleAlert className="text-destructive h-4 w-4" aria-hidden />
          Delete / archive
        </CardTitle>
        <CardDescription>
          Archive hides normal mistakes and drafts. Permanent delete is allowed only when no order, sale, or bundle dependency exists.
        </CardDescription>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="space-y-2">
          <Label htmlFor="delete-confirm-title">Type the exact title to confirm</Label>
          <Input
            id="delete-confirm-title"
            value={confirmTitle}
            onChange={(event) => setConfirmTitle(event.target.value)}
            placeholder={title}
            autoComplete="off"
          />
        </div>

        {result?.message && (
          <div className={`rounded-lg border p-3 text-sm ${result.ok ? "border-green-200 bg-green-50 text-green-800" : "border-destructive/30 bg-destructive/5 text-destructive"}`}>
            {result.message}
          </div>
        )}

        <div className="grid gap-2">
          <Button type="button" variant="outline" className="w-full justify-center" disabled={isPending || !confirmed} onClick={() => run(false)}>
            <Archive className="mr-2 h-4 w-4" aria-hidden />
            Archive item
          </Button>
          <Button type="button" variant="destructive" className="w-full justify-center" disabled={isPending || !confirmed} onClick={() => run(true)}>
            <Trash2 className="mr-2 h-4 w-4" aria-hidden />
            Delete permanently
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
