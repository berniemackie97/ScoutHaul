import Link from "next/link";
import { LockKeyhole } from "lucide-react";
import { Button, Card, CardContent } from "@overhaul/ui";
import { missing, permissionLabel, type Granted, type Requirement } from "@/lib/permissions";

function listed(names: string[], joiner: "and" | "or"): string {
  return names.length <= 1
    ? (names[0] ?? "")
    : `${names.slice(0, -1).join(", ")} ${joiner} ${names.at(-1)}`;
}

/** A page this person's role cannot open, said plainly — instead of a page of zeros from requests it refused. */
export function NoAccess({
  page,
  requires,
  granted,
}: {
  page: string;
  requires: Requirement;
  granted: Granted;
}) {
  const needed = missing(granted, requires).map(permissionLabel);
  const what =
    "all" in requires
      ? `the ${listed(needed, "and")} permission${needed.length === 1 ? "" : "s"}`
      : `one of ${listed(needed, "or")}`;

  return (
    <Card className="mx-auto mt-10 max-w-lg">
      <CardContent className="flex flex-col items-start gap-4 pt-6">
        <div className="bg-muted text-muted-foreground flex h-10 w-10 items-center justify-center rounded-full">
          <LockKeyhole className="h-5 w-5" aria-hidden />
        </div>
        <div className="space-y-1">
          <h2 className="text-lg font-semibold">Your role can&rsquo;t open {page}</h2>
          <p className="text-muted-foreground text-sm">
            It needs {what}. An owner can add {"any" in requires ? "one" : needed.length === 1 ? "it" : "them"} to your role in
            Settings → Team &amp; access.
          </p>
        </div>
        <Button variant="outline" asChild>
          <Link href="/">Back to the Command Center</Link>
        </Button>
      </CardContent>
    </Card>
  );
}
