import { useEffect, useState, type FormEvent, type ReactNode } from "react";
import { browser } from "wxt/browser";
import { adapterFor, adapters } from "../adapters/registry";
import type { MarketplaceAdapter } from "../adapters/types";
import { portalOrigins } from "../agent/config";
import { describeBrowser } from "../agent/environment";
import type { PopupRequest } from "../agent/messages";
import {
  activityItem,
  authProblemItem,
  connectionItem,
  governorItem,
  heartbeatItem,
  lastRunsItem,
  pairingItem,
  portalItem,
  queuedItem,
  runningItem,
  sessionsItem,
  workKey,
  type LastRun,
  type RunningWork,
  type StoredPairing,
} from "../agent/storage";
import type { AgentAccount, LoopKind } from "../agent/types";
import { ask, useAccess, useNow, useStored } from "./hooks";
import { ago, clock, headline, loopRow, marketplaceStatus, scanBlocker, type Headline, type MarketplaceStatus } from "./model";

type Send = (request: PopupRequest) => Promise<void>;

/** Opens a page in a new tab, in front — the popup closes as it goes. */
function openTab(url: string) {
  void browser.tabs.create({ url, active: true });
  window.close();
}

export function App() {
  const pairing = useStored(pairingItem);
  const problem = useStored(authProblemItem);
  const portal = useStored(portalItem);
  const version = browser.runtime.getManifest().version;
  const home = portal ?? portalOrigins[0] ?? null;

  return (
    <main className="popup">
      <header className="masthead">
        <span className={`dot ${pairing ? "dot-on" : "dot-off"}`} aria-hidden />
        <h1>HaulScout Sync</h1>
        <span className="version">v{version}</span>
      </header>
      {pairing === undefined ? null : pairing ? <Connected pairing={pairing} home={home} /> : <Connect problem={problem?.message ?? null} home={home} />}
    </main>
  );
}

function Connect({ problem, home }: { problem: string | null; home: string | null }) {
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
        In HaulScout, open <strong>Sync → Browsers</strong>, choose <strong>Connect a browser</strong>, and paste the code it shows here.
      </p>
      {home ? (
        <p className="muted">
          <button className="link" type="button" onClick={() => openTab(`${home}/sync/browsers?connect=1`)}>
            Get a code in HaulScout
          </button>
        </p>
      ) : null}
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

function Connected({ pairing, home }: { pairing: StoredPairing; home: string | null }) {
  const heartbeat = useStored(heartbeatItem);
  const sessions = useStored(sessionsItem);
  const governors = useStored(governorItem);
  const running = useStored(runningItem);
  const queued = useStored(queuedItem);
  const lastRuns = useStored(lastRunsItem);
  const connection = useStored(connectionItem);
  const activity = useStored(activityItem);
  const access = useAccess();
  const now = useNow();
  const [error, setError] = useState<string | null>(null);

  // Opening the popup is a good moment to look again at a sign-in that has aged or shows a problem;
  // the agent decides whether it needs to.
  useEffect(() => {
    void ask({ type: "popup-opened" });
  }, []);

  const send: Send = async (request) => {
    setError(null);
    const answer = await ask(request);
    if (!answer.ok) {
      setError(answer.error);
    }
  };

  const accounts = heartbeat?.accounts ?? [];
  // Only the marketplaces this organization sells on: no nagging about one it has no account for.
  const marketplaces = adapters.filter((adapter) => accounts.some((account) => adapterFor(account.platform) === adapter)).map((adapter) => ({
    adapter,
    status: marketplaceStatus(adapter, {
      policy: heartbeat?.platforms?.find((item) => item.platform === adapter.platform),
      access: access?.[adapter.platform],
      session: sessions?.[adapter.platform],
      governor: governors?.[adapter.platform],
      now: now.getTime(),
    }),
  }));
  const top = headline({ connection, running, marketplaces, accounts, now });

  return (
    <>
      <section className="section identity">
        <div>
          <p className="org">{pairing.organizationName}</p>
          <p className="muted">
            This browser is <strong>{pairing.agentName}</strong> · checked in {ago(heartbeat?.receivedAt, now)}
          </p>
        </div>
        <button
          className="button button-small"
          onClick={() => void send({ type: "refresh" })}
          disabled={Boolean(running)}
          title="Look at the sign-in again and check in with HaulScout now"
        >
          Refresh
        </button>
      </section>

      <HeadlineBar headline={top} />
      {error ? <p className="section error">{error}</p> : null}

      {marketplaces.length > 0 ? (
        <section className="section">
          <h2>Marketplaces</h2>
          {marketplaces.map(({ adapter, status }) => (
            <Marketplace key={adapter.platform} adapter={adapter} status={status} now={now} send={send} />
          ))}
        </section>
      ) : null}

      <section className="section">
        <h2>Accounts</h2>
        {accounts.length === 0 ? (
          <p className="muted">No marketplace accounts to sync yet. Add them in HaulScout under Channels.</p>
        ) : (
          accounts.map((account) => (
            <Account
              key={account.channelAccountId}
              account={account}
              status={marketplaces.find((item) => item.adapter.platform === adapterFor(account.platform)?.platform)?.status}
              lastRuns={lastRuns ?? {}}
              offline={connection?.ok === false}
              running={running}
              queued={queued ?? []}
              now={now}
              home={home}
              send={send}
            />
          ))
        )}
      </section>

      <section className="section">
        <h2>Activity</h2>
        {!activity || activity.length === 0 ? (
          <p className="muted">Nothing yet. Every scan you ask for shows up here, with anything that needs you.</p>
        ) : (
          <ul className="activity">
            {activity.slice(0, 8).map((entry) => (
              <li key={`${entry.at}-${entry.message}`} className={`activity-${entry.tone}`}>
                <span className="when">{ago(entry.at, now)}</span>
                <span>
                  {entry.message}
                  {entry.count && entry.count > 1 ? <span className="count"> ×{entry.count}</span> : null}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>

      <footer className="footer">
        {home ? (
          <button className="link" onClick={() => openTab(`${home}/sync`)}>
            Open HaulScout
          </button>
        ) : (
          <span />
        )}
        <button
          className="link-danger"
          onClick={() => {
            if (window.confirm(`Disconnect this browser from ${pairing.organizationName}? It stops syncing until you connect it again.`)) {
              void send({ type: "disconnect" });
            }
          }}
        >
          Disconnect this browser
        </button>
      </footer>
    </>
  );
}

function HeadlineBar({ headline: line }: { headline: Headline }) {
  return (
    <section className={`headline headline-${line.tone}`} role="status" aria-live="polite">
      <span className={line.tone === "busy" ? "spinner" : "headline-mark"} aria-hidden />
      <div>
        <p className="headline-title">{line.title}</p>
        {line.text ? <p className="headline-text">{line.text}</p> : null}
      </div>
    </section>
  );
}

function Marketplace(props: { adapter: MarketplaceAdapter; status: MarketplaceStatus; now: Date; send: Send }) {
  const { adapter, status, now, send } = props;
  const [accessProblem, setAccessProblem] = useState<string | null>(null);

  const askForAccess = () => {
    // Asked from the click itself: browsers only show the prompt for a person's gesture.
    browser.permissions.request({ origins: adapter.origins }).then(
      (granted) => setAccessProblem(granted ? null : `Access to ${adapter.platform} was not allowed, so it cannot be synced from this browser.`),
      () => setAccessProblem(`This build of HaulScout Sync cannot ask for access to ${adapter.platform}. Install the latest build.`),
    );
  };

  let body: ReactNode;
  switch (status.kind) {
    case "disabled":
      body = <p className="notice notice-warning">{status.text}</p>;
      break;
    case "no-access":
      body = (
        <>
          <p className="muted">
            HaulScout Sync needs your permission to read your {adapter.platform} seller pages in this browser. It only reads — it never changes
            anything on {adapter.platform}.
          </p>
          <button className="button button-primary" onClick={askForAccess}>
            Allow access to {adapter.platform}
          </button>
          {accessProblem ? <p className="notice notice-warning">{accessProblem}</p> : null}
        </>
      );
      break;
    case "checking":
      body = (
        <p className="muted">
          <span className="spinner spinner-inline" aria-hidden /> Checking who this browser is signed in as…
        </p>
      );
      break;
    case "paused":
      body = (
        <>
          <p className="notice notice-warning">{status.reason}</p>
          <div className="row">
            {status.challenge ? (
              <button className="button button-primary" onClick={() => openTab(adapter.checkUrl)}>
                Open {adapter.platform}
              </button>
            ) : null}
            <button className="button" onClick={() => void send({ type: "resume", platform: adapter.platform })}>
              Resume now
            </button>
          </div>
          <p className="hint">
            {status.challenge ? "Syncing picks up by itself once the check is done." : `Resumes by itself at ${clock(status.until, now)}.`}
          </p>
        </>
      );
      break;
    case "signed-out":
      body = (
        <>
          <p className="notice notice-warning">Not signed in to {adapter.platform} in this browser.</p>
          <button className="button button-primary" onClick={() => openTab(adapter.signInUrl)}>
            Sign in to {adapter.platform}
          </button>
          <p className="hint">Syncing picks up by itself once you have signed in.</p>
        </>
      );
      break;
    case "signed-in":
      body = (
        <p className="muted">
          Signed in as <strong>{status.username}</strong> · checked {ago(status.checkedAt, now)}
        </p>
      );
      break;
  }

  return (
    <div className="card">
      <p className="card-title">{adapter.platform}</p>
      {body}
    </div>
  );
}

function Account(props: {
  account: AgentAccount;
  status: MarketplaceStatus | undefined;
  lastRuns: Record<string, LastRun>;
  offline: boolean;
  running: RunningWork | null | undefined;
  queued: string[];
  now: Date;
  home: string | null;
  send: Send;
}) {
  const { account, status, lastRuns, offline, running, queued, now, home, send } = props;
  const adapter = adapterFor(account.platform);
  // A scan with HaulScout out of reach would read the marketplace and have nowhere to put it.
  const blocker = offline ? "HaulScout can't be reached right now." : scanBlocker(adapter, account, status);
  const loops: LoopKind[] = account.loops?.length ? account.loops.map((loop) => loop.loop!) : ["Sales", "Listings"];

  // A click shows at once, before the agent has queued the scan and said so — until it has.
  const [asked, setAsked] = useState<string[]>([]);
  useEffect(() => {
    setAsked((current) => {
      const waiting = current.filter((key) => running?.key !== key && !queued.includes(key));
      return waiting.length === current.length ? current : waiting;
    });
  }, [running, queued]);
  useEffect(() => {
    if (asked.length === 0) {
      return;
    }

    const timer = setTimeout(() => setAsked([]), 4000);
    return () => clearTimeout(timer);
  }, [asked]);

  return (
    <div className="card">
      <p className="card-title">{account.displayName}</p>
      {account.boundUsername ? (
        <p className="card-sub">
          Syncs <strong>{account.boundUsername}</strong> on {account.platform}
        </p>
      ) : null}
      {!account.boundUsername ? (
        <>
          <p className="muted">Not bound to its {account.platform} seller account yet. In HaulScout, open Sync and choose Bind.</p>
          {home ? (
            <button className="button" onClick={() => openTab(`${home}/sync`)}>
              Open HaulScout
            </button>
          ) : null}
        </>
      ) : (
        <ul className="loops">
          {loops.map((loop) => {
            const key = workKey(account.channelAccountId!, loop);
            const row = loopRow(account, loop, lastRuns[key], running, queued, now);
            const state = row.state === "idle" && asked.includes(key) ? "queued" : row.state;
            return (
              <li key={loop} className="loop">
                <div className="loop-main">
                  <p className="loop-name">
                    {loop === "Listings" ? "Listings" : "Sales"}
                    <span className="small muted-inline">
                      {" "}
                      · {row.cadence}
                      {row.next ? ` · ${row.next}` : ""}
                    </span>
                  </p>
                  {row.last ? <p className={`loop-last loop-last-${row.last.tone}`}>{row.last.text}</p> : null}
                </div>
                <button
                  className="button button-small"
                  disabled={state !== "idle" || blocker !== null}
                  title={blocker ?? `Scan ${account.displayName} ${loop === "Listings" ? "listings" : "sales"} now`}
                  onClick={() => {
                    setAsked((current) => [...current, key]);
                    void send({ type: "scan", channelAccountId: account.channelAccountId!, loop });
                  }}
                >
                  {state === "running" ? "Scanning…" : state === "queued" ? "Queued" : "Scan"}
                </button>
              </li>
            );
          })}
        </ul>
      )}
      {account.boundUsername && blocker ? <p className="hint">{blocker}</p> : null}
    </div>
  );
}
