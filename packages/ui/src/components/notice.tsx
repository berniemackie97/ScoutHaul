import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "../lib/utils";

const noticeVariants = cva("rounded-lg border px-4 py-3 text-sm", {
  variants: {
    tone: {
      neutral: "border-border bg-muted/40 text-foreground",
      info: "border-info-border bg-info-surface text-info",
      success: "border-success-border bg-success-surface text-success",
      warning: "border-warning-border bg-warning-surface text-warning",
      destructive: "border-destructive-border bg-destructive-surface text-destructive",
    },
  },
  defaultVariants: { tone: "neutral" },
});

export interface NoticeProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "title">, VariantProps<typeof noticeVariants> {
  title?: React.ReactNode;
  icon?: React.ReactNode;
}

/** A status message inside a page: what happened or what needs attention, in one of the status tones. */
function Notice({ className, tone, title, icon, children, role, ...props }: NoticeProps) {
  return (
    <div role={role ?? (tone === "destructive" ? "alert" : "status")} className={cn(noticeVariants({ tone }), className)} {...props}>
      <div className="flex gap-3">
        {icon && <span className="mt-0.5 shrink-0" aria-hidden>{icon}</span>}
        <div className="min-w-0 space-y-1">
          {title && <p className="font-semibold">{title}</p>}
          {children && <div className="text-foreground/85">{children}</div>}
        </div>
      </div>
    </div>
  );
}

export { Notice, noticeVariants };
