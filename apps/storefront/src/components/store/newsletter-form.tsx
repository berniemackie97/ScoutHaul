import { ArrowRight } from "lucide-react";

const CONTACT_EMAIL = "overhaultradingco@gmail.com";

/**
 * First dibs, without pretending there is a mailing list.
 *
 * This used to be an email field whose submit handler did nothing but `setDone(true)` behind a
 * `// TODO: wire to the list provider once chosen`. Every shopper who signed up was told "You're on
 * the list — watch your inbox", no request was ever made, and every one of those leads was lost. A
 * false confirmation is worse than no form: it costs the sale *and* the trust.
 *
 * No list provider is configured and picking one is the owner's decision, not something to guess at
 * here. So this asks for the same thing through the address the site already publishes on its contact
 * page and in its footer — which actually reaches someone. When a provider is chosen, this becomes a
 * real form again.
 */
export function NewsletterForm() {
  return (
    <div className="mt-8 flex flex-col items-center gap-3">
      <a
        href={`mailto:${CONTACT_EMAIL}?subject=${encodeURIComponent("First dibs")}&body=${encodeURIComponent(
          "Add me to the list for new finds.",
        )}`}
        className="group inline-flex items-center justify-center gap-2 bg-ink px-6 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-background transition-colors hover:bg-copper"
      >
        Email us for first dibs
        <ArrowRight className="h-4 w-4 transition-transform duration-300 group-hover:translate-x-1" aria-hidden />
      </a>
      <p className="font-mono text-[11px] uppercase tracking-[0.16em] text-slate/70">{CONTACT_EMAIL}</p>
    </div>
  );
}
