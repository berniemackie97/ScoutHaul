"use client";

import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@overhaul/ui";
import { Package2 } from "lucide-react";
import { createSupabaseBrowserClient } from "@/lib/supabase/client";

const inputClass =
  "flex h-11 w-full rounded-md border border-input bg-background px-3 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring";

export default function SignInPage() {
  const router = useRouter();
  const [stage, setStage] = useState<"email" | "code">("email");
  const [email, setEmail] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function sendCode(event: FormEvent) {
    event.preventDefault();
    const addr = email.trim().toLowerCase();
    if (!addr.includes("@")) {
      setError("Enter the email on your operator account.");
      return;
    }
    setLoading(true);
    setError(null);
    const supabase = createSupabaseBrowserClient();
    const { error: otpError } = await supabase.auth.signInWithOtp({
      email: addr,
      options: { shouldCreateUser: false },
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
      setError("That code didn't work. Check it or request a new one.");
      return;
    }
    router.replace("/");
    router.refresh();
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <div className="w-full max-w-sm space-y-7">
        <div className="flex flex-col items-center gap-3 text-center">
          <div className="flex h-12 w-12 items-center justify-center rounded-xl bg-brand-600">
            <Package2 className="h-6 w-6 text-white" aria-hidden />
          </div>
          <div>
            <h1 className="text-xl font-bold">HaulScout</h1>
            <p className="text-sm text-muted-foreground">Sign in to the operator console</p>
          </div>
        </div>

        {stage === "email" ? (
          <form onSubmit={sendCode} className="space-y-4">
            <div className="space-y-2">
              <label htmlFor="email" className="text-sm font-medium">
                Operator email
              </label>
              <input
                id="email"
                type="email"
                autoComplete="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="you@email.com"
                className={inputClass}
              />
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" className="w-full" disabled={loading}>
              {loading ? "Sending code…" : "Email me a code"}
            </Button>
          </form>
        ) : (
          <form onSubmit={verifyCode} className="space-y-4">
            <p className="text-center text-sm text-muted-foreground">
              Enter the 6-digit code sent to <span className="font-medium text-foreground">{email}</span>.
            </p>
            <div className="space-y-2">
              <label htmlFor="code" className="text-sm font-medium">
                Sign-in code
              </label>
              <input
                id="code"
                inputMode="numeric"
                autoComplete="one-time-code"
                required
                value={code}
                onChange={(e) => setCode(e.target.value.replace(/\D/g, "").slice(0, 6))}
                placeholder="123456"
                className={`${inputClass} text-center text-lg tracking-[0.4em]`}
              />
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button type="submit" className="w-full" disabled={loading}>
              {loading ? "Verifying…" : "Verify & sign in"}
            </Button>
            <button
              type="button"
              onClick={() => {
                setStage("email");
                setCode("");
                setError(null);
              }}
              className="w-full text-center text-xs text-muted-foreground hover:text-foreground"
            >
              Use a different email
            </button>
          </form>
        )}
      </div>
    </div>
  );
}
