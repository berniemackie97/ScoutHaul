import { Search } from "lucide-react";
import { Input } from "@overhaul/ui";

/**
 * The search box for a list page.
 *
 * A plain GET form, so the query lives in the URL and a filtered view can be bookmarked, shared and
 * reloaded. Paging resets on submit — searching and staying on page 4 of the previous results shows
 * an empty screen that reads as "no matches".
 */
export function ListSearch({
  defaultValue,
  placeholder,
  children,
}: {
  defaultValue?: string;
  placeholder: string;
  /** Extra filter controls; they submit with the form. */
  children?: React.ReactNode;
}) {
  return (
    <form className="bg-card grid gap-3 rounded-xl border p-4 sm:grid-cols-[minmax(0,1fr)_auto]">
      <label className="relative">
        <span className="sr-only">{placeholder}</span>
        <Search className="text-muted-foreground absolute left-3 top-3 h-4 w-4" aria-hidden />
        <Input name="q" defaultValue={defaultValue} placeholder={placeholder} className="pl-9" />
      </label>
      {children}
      <button
        type="submit"
        className="bg-brand-600 hover:bg-brand-700 h-10 rounded-lg px-5 text-sm font-medium text-white"
      >
        Apply
      </button>
    </form>
  );
}

/** The dashed panel a list shows when it has nothing, with somewhere to go next. */
export function ListEmpty({ children }: { children: React.ReactNode }) {
  return (
    <div className="bg-muted/20 text-muted-foreground rounded-xl border border-dashed p-12 text-center text-sm">
      {children}
    </div>
  );
}

/** A page's title block, so the heading hierarchy is the same on every screen. */
export function PageHeader({
  eyebrow,
  title,
  description,
  action,
}: {
  eyebrow: string;
  title: string;
  description?: string;
  action?: React.ReactNode;
}) {
  return (
    <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
      <div>
        <p className="text-muted-foreground text-sm">{eyebrow}</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">{title}</h1>
        {description ? <p className="text-muted-foreground mt-1 max-w-3xl text-sm">{description}</p> : null}
      </div>
      {action}
    </header>
  );
}
