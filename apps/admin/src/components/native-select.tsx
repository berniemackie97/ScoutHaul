import type { SelectHTMLAttributes } from "react";
import { ChevronsUpDown } from "lucide-react";
import { cn } from "@overhaul/ui";

/** A native select drawn like the searchable pickers, so a form's dropdowns read as one set. */
export function NativeSelect({ className, ...props }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <div className="relative">
      <select
        {...props}
        className={cn(
          "border-input bg-background focus:ring-ring h-10 w-full appearance-none rounded-lg border pl-3 pr-9 text-sm focus:outline-none focus:ring-2 disabled:cursor-not-allowed disabled:opacity-50",
          className,
        )}
      />
      <ChevronsUpDown className="text-muted-foreground pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2" aria-hidden />
    </div>
  );
}
