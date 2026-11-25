"use client";

import { useActionState, useEffect, useState } from "react";
import { useFormStatus } from "react-dom";
import { Pencil, X } from "lucide-react";
import { Button, Card, CardContent, CardHeader, CardTitle, Input, Label } from "@overhaul/ui";
import { updateCustomer, type CustomerActionResult } from "./actions";

const initial: CustomerActionResult = { ok: false, message: "" };

type EditableCustomer = {
  id: string;
  displayName?: string | null;
  email?: string | null;
  phone?: string | null;
  acceptsMarketing?: boolean;
};

function Submit() {
  const { pending } = useFormStatus();
  return <Button type="submit" disabled={pending}>{pending ? "Saving…" : "Save changes"}</Button>;
}

/**
 * Correcting a customer's details. Collapsed by default — most visits are to read the history, not to
 * change it — and the form keeps what was typed when the server refuses, so a rejected email is
 * corrected rather than re-entered.
 */
export function CustomerEditor({ customer }: { customer: EditableCustomer }) {
  const [open, setOpen] = useState(false);
  const [state, action] = useActionState(updateCustomer.bind(null, customer.id), initial);

  // Collapse after a successful save. Done in an effect rather than during render — setting state
  // while rendering is a React anti-pattern and would warn.
  useEffect(() => {
    if (state.ok) setOpen(false);
  }, [state.ok]);

  if (!open) {
    return (
      <div className="flex items-center gap-3">
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="text-brand-600 inline-flex items-center gap-1.5 text-sm hover:underline"
        >
          <Pencil className="h-3.5 w-3.5" /> Edit details
        </button>
        {state.message && (
          <span className={`text-xs ${state.ok ? "text-success" : "text-destructive"}`}>{state.message}</span>
        )}
      </div>
    );
  }

  return (
    <Card>
      <CardHeader className="flex-row items-center justify-between space-y-0">
        <CardTitle className="text-lg">Edit customer</CardTitle>
        <button type="button" onClick={() => setOpen(false)} className="hover:bg-muted rounded p-1" aria-label="Cancel">
          <X className="h-4 w-4" />
        </button>
      </CardHeader>
      <CardContent>
        <form action={action} className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="displayName">Name</Label>
              <Input id="displayName" name="displayName" defaultValue={customer.displayName ?? ""} placeholder="Full name" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="email">Email</Label>
              <Input id="email" name="email" type="email" defaultValue={customer.email ?? ""} placeholder="name@example.com" />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="phone">Phone</Label>
              <Input id="phone" name="phone" defaultValue={customer.phone ?? ""} placeholder="Optional" />
            </div>
          </div>

          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" name="acceptsMarketing" defaultChecked={customer.acceptsMarketing ?? false} />
            Accepts marketing
          </label>

          <div className="flex flex-wrap items-center gap-3">
            <Submit />
            <Button type="button" variant="ghost" onClick={() => setOpen(false)}>Cancel</Button>
            {state.message && !state.ok && <span className="text-destructive text-sm">{state.message}</span>}
          </div>
          <p className="text-muted-foreground text-xs">
            Store credit and the sign-in link are not edited here — credit moves only through a credit
            entry, and the sign-in link is what identifies the account.
          </p>
        </form>
      </CardContent>
    </Card>
  );
}
