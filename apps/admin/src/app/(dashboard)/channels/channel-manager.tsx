"use client";

import { useMemo, useState, useTransition } from "react";
import { useActionState } from "react";
import { useFormStatus } from "react-dom";
import { useRouter } from "next/navigation";
import { Check, Pencil, Plug, Power, Trash2, X } from "lucide-react";
import type { components } from "@overhaul/api-client";
import { Badge, Button, Card, CardContent, CardDescription, CardHeader, CardTitle, Input, Label, Textarea } from "@overhaul/ui";
import { saveChannelAccount, setChannelActive, deleteChannelAccount, type ChannelActionResult } from "./actions";

type Account = components["schemas"]["ChannelAccountDto"];
type ChannelOption = components["schemas"]["SaleChannelOptionDto"];

const initial: ChannelActionResult = { ok: false, message: "" };
const selectClass = "h-10 w-full rounded-lg border border-input bg-background px-3 text-sm focus:outline-none focus:ring-2 focus:ring-ring";

function SubmitButton({ editing }: { editing: boolean }) {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Saving…" : editing ? "Save changes" : "Connect channel"}</Button>;
}

export function ChannelManager({ accounts, channels }: { accounts: Account[]; channels: ChannelOption[] }) {
  const router = useRouter();
  const [state, action] = useActionState(saveChannelAccount, initial);
  const [editing, setEditing] = useState<Account | null>(null);
  const [selectedChannel, setSelectedChannel] = useState("");
  const [pending, startTransition] = useTransition();
  const [rowMessage, setRowMessage] = useState<ChannelActionResult | null>(null);

  const listable = channels.filter((option) => option.value);
  const groups = useMemo(() => {
    const map = new Map<string, ChannelOption[]>();
    for (const option of listable) {
      const key = option.group ?? "Channels";
      map.set(key, [...(map.get(key) ?? []), option]);
    }
    return map;
  }, [listable]);

  const activeChannel = selectedChannel || editing?.channel || "";
  const feeProfiles = listable.find((option) => option.value === activeChannel)?.feeProfiles ?? [];

  function startEdit(account: Account) {
    setEditing(account);
    setSelectedChannel(account.channel ?? "");
    if (typeof window !== "undefined") window.scrollTo({ top: 0, behavior: "smooth" });
  }
  function cancelEdit() {
    setEditing(null);
    setSelectedChannel("");
  }
  function runRow(fn: () => Promise<ChannelActionResult>) {
    startTransition(async () => {
      const outcome = await fn();
      setRowMessage(outcome);
      if (outcome.ok) router.refresh();
    });
  }

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{editing ? `Edit ${editing.displayName}` : "Connect a channel"}</CardTitle>
          <CardDescription>
            A channel account represents one marketplace login (or your own storefront). Its default fee profile feeds accurate net-profit
            math, and it&rsquo;s where per-item marketplace presence is tracked.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form key={editing?.id ?? "new"} action={action} className="grid gap-4 sm:grid-cols-2">
            {editing?.id && <input type="hidden" name="id" value={editing.id} />}
            <div className="space-y-2">
              <Label htmlFor="channel">Channel</Label>
              <select
                id="channel"
                name="channel"
                aria-label="Channel"
                required
                defaultValue={editing?.channel ?? ""}
                onChange={(event) => setSelectedChannel(event.target.value)}
                className={selectClass}
              >
                <option value="">Select a channel…</option>
                {[...groups.entries()].map(([group, options]) => (
                  <optgroup key={group} label={group}>
                    {options.map((option) => (
                      <option key={option.value} value={option.value ?? ""}>
                        {option.label}
                      </option>
                    ))}
                  </optgroup>
                ))}
              </select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="displayName">Account name</Label>
              <Input id="displayName" name="displayName" required defaultValue={editing?.displayName ?? ""} placeholder="Overhaul eBay Store" />
            </div>
            <div className="space-y-2">
              <Label htmlFor="externalAccountId">Username / handle / store id</Label>
              <Input id="externalAccountId" name="externalAccountId" defaultValue={editing?.externalAccountId ?? ""} placeholder="@overhaultrading" />
            </div>
            <div className="space-y-2">
              <Label htmlFor="defaultFeeProfileId">Default fee profile</Label>
              <select id="defaultFeeProfileId" name="defaultFeeProfileId" aria-label="Default fee profile" defaultValue={editing?.defaultFeeProfileId ?? ""} className={selectClass}>
                <option value="">Channel default</option>
                {feeProfiles.map((profile) => (
                  <option key={profile.id} value={profile.id ?? ""}>
                    {profile.label}
                  </option>
                ))}
              </select>
            </div>
            <label className="flex items-center gap-2 text-sm sm:col-span-2">
              <input type="checkbox" name="isActive" value="true" defaultChecked={editing ? editing.isActive : true} />
              <span>Active — include in listing tracking and reporting</span>
            </label>
            <div className="space-y-2 sm:col-span-2">
              <Label htmlFor="notes">Notes</Label>
              <Textarea id="notes" name="notes" rows={2} defaultValue={editing?.notes ?? ""} placeholder="Subscription tier, store level, seller standing…" />
            </div>
            <div className="flex items-center gap-3 sm:col-span-2">
              <SubmitButton editing={Boolean(editing)} />
              {editing && (
                <button type="button" onClick={cancelEdit} className="text-muted-foreground inline-flex items-center gap-1 text-sm hover:text-foreground">
                  <X className="h-4 w-4" /> Cancel
                </button>
              )}
              {state.message && (
                <span className={`text-sm ${state.ok ? "text-success" : "text-destructive"}`}>{state.message}</span>
              )}
            </div>
          </form>
        </CardContent>
      </Card>

      <div className="space-y-3">
        <h2 className="text-lg font-semibold">Connected channels</h2>
        {rowMessage && <p className={`text-sm ${rowMessage.ok ? "text-success" : "text-destructive"}`}>{rowMessage.message}</p>}
        {accounts.length === 0 ? (
          <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-10 text-center text-sm">
            <Plug className="mx-auto mb-2 h-5 w-5" /> No channels connected yet. Add your storefront and marketplaces above.
          </div>
        ) : (
          <div className="grid gap-3 md:grid-cols-2">
            {accounts.map((account) => (
              <Card key={account.id} className={account.isActive ? "" : "opacity-60"}>
                <CardContent className="flex items-start justify-between gap-3 p-4">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium">{account.displayName}</span>
                      <Badge variant="secondary">{account.channel}</Badge>
                      {account.isNativelySynced ? (
                        <Badge variant="success">Native</Badge>
                      ) : (
                        <Badge variant="info">Manual</Badge>
                      )}
                      {!account.isActive && <Badge variant="outline">Inactive</Badge>}
                    </div>
                    <p className="text-muted-foreground mt-1 truncate text-xs">
                      {account.externalAccountId || "No handle"} · {account.activeListingCount ?? 0} active listing{account.activeListingCount === 1 ? "" : "s"}
                      {account.defaultFeeProfileId ? ` · fee: ${account.defaultFeeProfileId}` : ""}
                    </p>
                    {account.notes && <p className="text-muted-foreground mt-1 line-clamp-2 text-xs">{account.notes}</p>}
                  </div>
                  <div className="flex shrink-0 gap-1">
                    <button type="button" onClick={() => startEdit(account)} className="hover:bg-muted rounded-md p-2" title="Edit" aria-label="Edit">
                      <Pencil className="h-4 w-4" />
                    </button>
                    <button
                      type="button"
                      disabled={pending}
                      onClick={() => runRow(() => setChannelActive(account.id!, !account.isActive))}
                      className="hover:bg-muted rounded-md p-2 disabled:opacity-40"
                      title={account.isActive ? "Deactivate" : "Reactivate"}
                      aria-label="Toggle active"
                    >
                      {account.isActive ? <Power className="h-4 w-4" /> : <Check className="h-4 w-4" />}
                    </button>
                    <button
                      type="button"
                      disabled={pending}
                      onClick={() => {
                        if (confirm(`Remove ${account.displayName}? This can't be undone.`)) runRow(() => deleteChannelAccount(account.id!));
                      }}
                      className="text-muted-foreground hover:text-destructive rounded-md p-2 disabled:opacity-40"
                      title="Delete"
                      aria-label="Delete"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
