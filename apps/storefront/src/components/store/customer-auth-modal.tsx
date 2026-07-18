"use client";

import { useState, type FormEvent } from "react";
import { ArrowRight, Loader2, X } from "lucide-react";
import { createSupabaseBrowserClient } from "@/lib/supabase/client";

const inputClass =
  "mt-2 w-full border border-rule bg-background px-4 py-3 text-base text-ink outline-none transition-colors focus:border-copper";

type Props = {
  open: boolean;
  title?: string;
  body?: string;
  nextPath?: string;
  onClose: () => void;
  onAuthenticated?: () => void;
};

export function CustomerAuthModal({
  open,
  title = "Sign in or create an account",
  body = "Enter your email and we will send a one-time code. No password required.",
  nextPath,
  onClose,
  onAuthenticated,
}: Props) {
  const [stage, setStage] = useState<"email" | "code">("email");
  const [email, setEmail] = useState("");
  const [code, setCode] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!open) return null;

  async function sendCode(event: FormEvent) {
    event.preventDefault();
    const addr = email.trim().toLowerCase();
    if (!addr.includes("@")) {
      setError("Enter a valid email.");
      return;
    }

    setLoading(true);
    setError(null);
    try {
      const supabase = createSupabaseBrowserClient();
      const redirectPath = safeNext(nextPath ?? window.location.pathname);
      const { error: otpError } = await supabase.auth.signInWithOtp({
        email: addr,
        options: {
          shouldCreateUser: true,
          emailRedirectTo: `${window.location.origin}/auth/callback?next=${encodeURIComponent(redirectPath)}`,
        },
      });
      if (otpError) throw otpError;
      setEmail(addr);
      setStage("code");
    } catch (err) {
      setError(cleanError(err));
    } finally {
      setLoading(false);
    }
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
    try {
      const supabase = createSupabaseBrowserClient();
      const { error: verifyError } = await supabase.auth.verifyOtp({ email, token, type: "email" });
      if (verifyError) throw verifyError;
      onAuthenticated?.();
      onClose();
      reset();
    } catch {
      setError("That code did not work. Check it or request a new one.");
    } finally {
      setLoading(false);
    }
  }

  function reset() {
    setStage("email");
    setCode("");
    setError(null);
  }

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-ink/55 px-4 py-8 backdrop-blur-sm" role="dialog" aria-modal="true">
      <div className="relative w-full max-w-md border border-rule bg-background p-6 shadow-elev-lg">
        <button
          type="button"
          onClick={() => {
            onClose();
            reset();
          }}
          className="absolute right-4 top-4 p-1 text-slate transition-colors hover:text-ink"
          aria-label="Close sign-in"
        >
          <X className="h-5 w-5" aria-hidden />
        </button>

        <p className="font-mono text-[10px] uppercase tracking-[0.22em] text-copper">Customer account</p>
        <h2 className="mt-3 pr-8 font-heading text-3xl font-semibold text-ink">{title}</h2>
        <p className="mt-3 text-sm leading-6 text-slate">{body}</p>

        {stage === "email" ? (
          <form onSubmit={sendCode} className="mt-6 space-y-5">
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
              {loading ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : null}
              Email me a code <ArrowRight className="h-4 w-4" aria-hidden />
            </button>
          </form>
        ) : (
          <form onSubmit={verifyCode} className="mt-6 space-y-5">
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
              {loading ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : null}
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
      </div>
    </div>
  );
}

function safeNext(value: string) {
  return value.startsWith("/") && !value.startsWith("//") ? value : "/account";
}

function cleanError(error: unknown) {
  const raw = error instanceof Error ? error.message : "Sign-in failed.";
  return raw.replace(/^"|"$/g, "") || "Sign-in failed.";
}
