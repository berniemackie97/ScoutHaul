import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { browser } from "wxt/browser";
import { adapters } from "../adapters/registry";
import type { MarketplaceAdapter } from "../adapters/types";
import { sessionFreshMs } from "../agent/config";
import { describeBrowser } from "../agent/environment";
import {
  activityItem,
  authProblemItem,
  governorItem,
  heartbeatItem,
  pairingItem,
  runningItem,
  sessionsItem,
  type StoredPairing,
} from "../agent/storage";
import type { AgentAccount, LoopKind, PlatformPolicy } from "../agent/types";
import { ago, ask, cadence, useNow, useStored } from "./hooks";

export function App() {
  const pairing = useStored(pairingItem);
  const problem = useStored(authProblemItem);
  const version = browser.runtime.getManifest().version;

  return (
    <main className="popup">
      <header className="masthead">
        <span className={`dot ${pairing ? "dot-on" : "dot-off"}`} aria-hidden />
        <h1>HaulScout Sync</h1>
        <span className="version">v{version}</span>
      </header>
      {pairing === undefined ? null : pairing ? <Connected pairing={pairing} /> : <Connect problem={problem?.message ?? null} />}
    </main>
  );
}

function Connect({ problem }: { problem: string | null }) {
  const [code, setCode] = useState("");
  const [name, setName] = useState(() => {
    const { browser: browserName, operatingSystem } = describeBrowser();
    return operatingSystem ? `${browserName} on ${operatingSystem}` : browserName;
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    const answer = await ask({ type: "pair", code, name: name.trim() || null });
    setBusy(false);
    if (!answer.ok) {
      setError(answer.error);
    }
  };

  return (
    <section className="section">
      {problem ? <p className="notice notice-warning">{problem}</p> : null}
      <h2>Connect this browser</h2>
      <p className="muted">
        In HaulScout, open <strong>Sync</strong>, choose <strong>Connect a browser</strong>, and paste the code it shows here.
      </p>
      <form className="stack" onSubmit={submit}>
        <label className="field">
          <span>Code</span>
          <input
            value={code}
            onChange={(event) => setCode(event.target.value.toUpperCase())}
            placeholder="7KQ2-MX9D"
            autoComplete="off"
            spellCheck={false}
            maxLength={12}
            required
            autoFocus
          />
        </label>
        <label className="field">
          <span>Name this browser</span>
          <input value={name} onChange={(event) => setName(event.target.value)} maxLength={80} />
        </label>
        {error ? <p className="error">{error}</p> : null}
        <button className="button button-primary" type="submit" disabled={busy || code.trim().length < 8}>
          {busy ? "Connecting…" : "Connect"}
        </button>
      </form>
    </section>
  );
}

function Connected({ pairing }: { pairing: StoredPairing }) {
  const heartbeat = useStored(heartbeatItem);
  const sessions = useStored(sessionsItem);
  const running = useStored(runningItem);
  const activity = useStored(activityItem);
  const now = useNow();
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Opening the popup is a good moment to confirm who this browser is signed in as, if that answer
  // has gone stale; the check is cheap, and it is what the portal offers for binding.
  useEffect(() => {
    if (sessions === undefined) {
      return;
    }

    const stale = adapters.some((adapter) => {
      const checked = sessions[adapter.platform]?.checkedAt;
      return !checked || Date.now() - Date.parse(checked) > sessionFreshMs;
    });
    if (stale) {
      void ask({ type: "check-sessions" });
    }
    // Once per popup opening: rechecking on every storage change would loop.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sessions === undefined]);

  const run = async (key: string, request: Parameters<typeof ask>[0]) => {
    setBusy(key);
    setError(null);
    const answer = await ask(request);
    setBusy(null);
    if (!answer.ok) {
      setError(answer.error);
    }
  };

  const accounts = heartbeat?.accounts ?? [];
  return (
    <>
      <section className="section">
        <p className="org">{pairing.organizationName}</p>
        <p className="muted">
          This browser is <strong>{pairing.agentName}</strong> · checked in {ago(heartbeat?.receivedAt, now)}
        </p>
        <div className="row">
          <button className="button" onClick={() => run("check-in", { type: "check-in" })} disabled={busy !== null}>
            {busy === "check-in" ? "Checking in…" : "Check in now"}
          </button>
          {running ? <span className="running">{running.label}…</span> : null}
        </div>
        {error ? <p className="error">{error}</p> : null}
      </section>

      <section className="section">
        <h2>Marketplaces</h2>
        {adapters.map((adapter) => (
          <Marketplace
            key={adapter.platform}
            adapter={adapter}
            policy={heartbeat?.platforms?.find((item) => item.platform === adapter.platform)}
            session={sessions?.[adapter.platform]}
            now={now}
            busy={busy}
            onRun={run}
          />
        ))}
      </section>

      <section className="section">
        <h2>Accounts</h2>
        {accounts.length === 0 ? (
          <p className="muted">No marketplace accounts to sync yet. Add them in HaulScout under Channels.</p>
        ) : (
          accounts.map((account) => <Account key={account.channelAccountId} account={account} busy={busy} onRun={run} />)
        )}
      </section>

      <section className="section">
        <h2>Activity</h2>
        {!activity || activity.length === 0 ? (
          <p className="muted">Nothing yet. Scans that find something new, and anything that goes wrong, show up here.</p>
        ) : (
          <ul className="activity">
            {activity.slice(0, 8).map((entry) => (
              <li key={`${entry.at}-${entry.message}`} className={`activity-${entry.tone}`}>
                <span className="when">{ago(entry.at, now)}</span>
                <span>{entry.message}</span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <footer className="footer">
        <button
          className="link-danger"
          onClick={() => {
            if (window.confirm(`Disconnect this browser from ${pairing.organizationName}? It stops syncing until you connect it again.`)) {
              void run("disconnect", { type: "disconnect" });
            }
          }}
          disabled={busy !== null}
        >
          Disconnect this browser
        </button>
      </footer>
    </>
  );
}

function Marketplace(props: {
  adapter: MarketplaceAdapter;
  policy: PlatformPolicy | undefined;
  session: { signedIn: boolean; username: string | null; checkedAt: string } | undefined;
  now: Date;
  busy: string | null;
  onRun: (key: string, request: Parameters<typeof ask>[0]) => Promise<void>;
}) {
  const { adapter, policy, session, now, busy, onRun } = props;
  const governors = useStored(governorItem);
  const [access, setAccess] = useState<boolean>();
  useEffect(() => {
    void browser.permissions.contains({ origins: adapter.origins }).then(setAccess);
  }, [adapter]);

  const governor = governors?.[adapter.platform];
  const paused = governor?.pausedUntil && governor.pausedUntil > now.getTime() ? governor : null;

  let status: ReactNode;
  if (policy && policy.enabled === false) {
    status = <p className="notice notice-warning">{policy.disabledReason ?? `Sync for ${adapter.platform} is switched off for now.`}</p>;
  } else if (access === false) {
    status = (
      <>
        <p className="muted">HaulScout Sync needs your permission to read your {adapter.platform} seller pages in this browser.</p>
        <button
          className="button button-primary"
          onClick={() => {
            // Must be asked from the click itself: browsers only show the prompt for a user gesture.
            void browser.permissions.request({ origins: adapter.origins }).then(setAccess);
          }}
        >
          Allow access to {adapter.platform}
        </button>
      </>
    );
  } else if (paused) {
    status = (
      <>
        <p className="notice notice-warning">{paused.pauseReason}</p>
        <button className="button" onClick={() => onRun(`resume-${adapter.platform}`, { type: "resume", platform: adapter.platform })} disabled={busy !== null}>
          Resume now
        </button>
      </>
    );
  } else if (!session) {
    status = <p className="muted">Checking who this browser is signed in as…</p>;
  } else if (!session.signedIn) {
    status = (
      <>
        <p className="notice notice-warning">Not signed in to {adapter.platform} in this browser.</p>
        <div className="row">
          <button className="button" onClick={() => void browser.tabs.create({ url: adapter.signInUrl })}>
            Sign in to {adapter.platform}
          </button>
          <button className="button" onClick={() => onRun(`check-${adapter.platform}`, { type: "check-sessions" })} disabled={busy !== null}>
            Check again
          </button>
        </div>
      </>
    );
  } else {
    status = (
      <p className="muted">
        Signed in as <strong>{session.username}</strong> · checked {ago(session.checkedAt, now)}
      </p>
    );
  }

  return (
    <div className="card">
      <p className="card-title">{adapter.platform}</p>
      {status}
    </div>
  );
}

function Account(props: {
  account: AgentAccount;
  busy: string | null;
  onRun: (key: string, request: Parameters<typeof ask>[0]) => Promise<void>;
}) {
  const { account, busy, onRun } = props;
  const scan = (loop: LoopKind) => onRun(`${account.channelAccountId}-${loop}`, { type: "scan", channelAccountId: account.channelAccountId!, loop });

  return (
    <div className="card">
      <p className="card-title">{account.displayName}</p>
      {account.boundUsername ? (
        <>
          <p className="muted">
            Syncs <strong>{account.boundUsername}</strong> · sales {cadence(account.salesEveryMinutes)} · listings {cadence(account.listingsEveryMinutes)}
          </p>
          <div className="row">
            <button className="button" onClick={() => scan("Sales")} disabled={busy !== null}>
              {busy === `${account.channelAccountId}-Sales` ? "Scanning…" : "Scan sales"}
            </button>
            <button className="button" onClick={() => scan("Listings")} disabled={busy !== null}>
              {busy === `${account.channelAccountId}-Listings` ? "Scanning…" : "Scan listings"}
            </button>
          </div>
        </>
      ) : (
        <p className="muted">Not bound to its {account.platform} seller account yet. Bind it in HaulScout under Sync.</p>
      )}
    </div>
  );
}
