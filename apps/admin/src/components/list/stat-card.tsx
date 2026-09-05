import type { LucideIcon } from "lucide-react";
import Link from "next/link";
import { Card } from "@overhaul/ui";

/**
 * One headline figure.
 *
 * The same markup was written out by hand on eight pages, each with slightly different spacing and
 * a slightly different way of saying "nothing yet". This is that card, once.
 *
 * `detail` is where the figure's scope belongs — "across every lot", "paid orders" — because a number
 * with no stated scope is how a page ends up reporting a page of rows as the whole business.
 */
export function StatCard({
  label,
  value,
  detail,
  icon: Icon,
  href,
  tone = "default",
}: {
  label: string;
  value: string;
  detail?: string;
  icon?: LucideIcon;
  /** Makes the whole card a link. Use it when the figure is a queue someone can go and work. */
  href?: string;
  tone?: "default" | "warning" | "danger";
}) {
  const valueTone =
    tone === "danger" ? "text-destructive" : tone === "warning" ? "text-amber-600 dark:text-amber-500" : "";

  const body = (
    <>
      <div className="text-muted-foreground flex items-center justify-between text-sm">
        <span>{label}</span>
        {Icon ? <Icon className="h-4 w-4" aria-hidden /> : null}
      </div>
      <p className={`mt-3 text-3xl font-semibold tabular-nums ${valueTone}`}>{value}</p>
      {detail ? <p className="text-muted-foreground mt-1 text-xs">{detail}</p> : null}
    </>
  );

  if (href) {
    return (
      <Card className="hover:border-brand-300 p-5 transition-colors">
        <Link href={href} className="block">
          {body}
        </Link>
      </Card>
    );
  }

  return <Card className="p-5">{body}</Card>;
}

/** The row those cards sit in, so the grid does not get re-guessed on every page. */
export function StatRow({ children, columns = 3 }: { children: React.ReactNode; columns?: 2 | 3 | 4 }) {
  const grid = columns === 2 ? "sm:grid-cols-2" : columns === 4 ? "sm:grid-cols-2 xl:grid-cols-4" : "sm:grid-cols-3";
  return <div className={`grid gap-4 ${grid}`}>{children}</div>;
}
