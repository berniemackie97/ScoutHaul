"use client";

import { useSearchParams } from "next/navigation";
import { useState, type FormEvent } from "react";
import { ArrowRight, Loader2 } from "lucide-react";
import { createSupabaseBrowserClient } from "@/lib/supabase/client";

const inputClass =
  "mt-2 w-full border border-rule bg-background px-4 py-3 text-base text-ink outline-none transition-colors focus:border-copper";

export default function Page() {
  const searchParams = useSearchParams();
  const next = safeNext(searchParams.get("next"));
  const [stage, setStage] = useState<"email" | "code">("email");
  const [email, setEmail] = useState("");
  const [code, setCode] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function sendCode(event: FormEvent) {
    event.preventDefault();
    const addr = email.trim().toLowerCase();
    if (!addr.includes("@")) {
      setError("Enter a valid email.");
      return;
    }

    setLoading(true);
    setError(null);
    const supabase = createSupabaseBrowserClient();
    const { error: otpError } = await supabase.auth.signInWithOtp({
      email: addr,
      options: {
        shouldCreateUser: true,
        emailRedirectTo: `${window.location.origin}/auth/callback?next=${encodeURIComponent(next)}`,
      },
    });
    setLoading(false);

    if (otpError) {
      setError(otpError.message);
      return;
    }

    setEmail(addr);
    setStage("code");
  }

  async function verifyCode(event: FormEvent) {
    event.preventDefault();
    const token = code.trim();
    if (token.length < 6) {
      setError("Enter the 6-digit code from your email.");
      return;
    }

    setLoading(true);
    setError(null);
    const supabase = createSupabaseBrowserClient();
    const { error: verifyError } = await supabase.auth.verifyOtp({ email, token, type: "email" });
    setLoading(false);

    if (verifyError) {
      setError("That code did not work. Check it or request a new one.");
      return;
    }

    window.location.assign(next);
  }

  return (
    <main className="mx-auto flex min-h-screen max-w-md flex-col justify-center px-6 py-16">
      <p className="font-mono text-xs uppercase tracking-[0.25em] text-copper">Customer account</p>
      <h1 className="mt-3 font-heading text-4xl font-semibold text-ink">Sign in</h1>
      <p className="mt-3 text-sm leading-6 text-slate">
        Use your email to manage offers and order history. We send a one-time code; no password required.
      </p>

      {stage === "email" ? (
        <form onSubmit={sendCode} className="mt-8 space-y-5">
          <label className="block text-sm font-semibold text-ink">
            Email
            <input
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              autoComplete="email"
              required
              className={inputClass}
            />
          </label>
          {error ? <p className="text-sm text-red-700">{error}</p> : null}
          <button
            type="submit"
            disabled={loading}
            className="flex w-full items-center justify-center gap-2 bg-copper px-6 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-ink disabled:opacity-60"
          >
            {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
            Email me a code <ArrowRight className="h-4 w-4" aria-hidden />
          </button>
        </form>
      ) : (
        <form onSubmit={verifyCode} className="mt-8 space-y-5">
          <p className="text-sm text-slate">
            Enter the 6-digit code sent to <span className="font-semibold text-ink">{email}</span>.
          </p>
          <label className="block text-sm font-semibold text-ink">
            Sign-in code
            <input
              inputMode="numeric"
              autoComplete="one-time-code"
              value={code}
              onChange={(event) => setCode(event.target.value.replace(/\D/g, "").slice(0, 6))}
              required
              className={`${inputClass} text-center text-lg tracking-[0.35em]`}
            />
          </label>
          {error ? <p className="text-sm text-red-700">{error}</p> : null}
          <button
            type="submit"
            disabled={loading}
            className="flex w-full items-center justify-center gap-2 bg-copper px-6 py-3 text-sm font-semibold uppercase tracking-[0.12em] text-ink disabled:opacity-60"
          >
            {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : null}
            Verify and continue
          </button>
          <button
            type="button"
            onClick={() => {
              setStage("email");
              setCode("");
              setError(null);
            }}
            className="w-full text-sm text-slate underline underline-offset-4 hover:text-ink"
          >
            Use a different email
          </button>
        </form>
      )}
    </main>
  );
}

function safeNext(value: string | null) {
  return value?.startsWith("/") && !value.startsWith("//") ? value : "/account";
}
