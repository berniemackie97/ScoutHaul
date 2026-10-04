#!/usr/bin/env node
// Fails the build on any high or critical advisory that we have not explicitly deferred.
//
// `npm audit --audit-level=high` cannot express "this one is known and blocked on other work", so
// it goes red for something nobody can act on and the whole step stops meaning anything. This keeps
// the same gate but records the exceptions, and — because an unused entry is also an error — the
// list can only shrink.

import { execFile } from 'node:child_process';
import { promisify } from 'node:util';

/** Advisories we knowingly ship with. Each needs a reason and the work that removes it. */
const deferred = {
  // GHSA-w3rx-r6r6-pgpr / GHSA-5p2g-fcmc-qvqq — `image-size` loops forever on a malformed ICNS,
  // JXL or HEIF file. It is reached only through the Metro bundler, which parses image assets at
  // build time from this repo, so there is no untrusted input and nothing in the shipped app.
  //
  // The fix is image-size 2.0.3, a major version Metro does not accept (`metro-transform-worker`
  // asks for ^1.0.2), so forcing it through `overrides` would change the parser Metro bundles our
  // assets with. It goes when Expo ships a Metro that asks for a patched one.
  'image-size': 'Expo/Metro releasing a build that asks for image-size >= 2.0.3',

  // GHSA-vfj7-8cjw-p6xm — `braces` overflows the stack on deeply nested glob patterns. Reached only
  // through micromatch in Jest, with patterns written in this repo's config. No release fixes it yet.
  braces: 'braces publishing a release above 3.0.3',

  // GHSA-86w9-cpqp-85rv — `node-forge` accepts malformed RSA PKCS#1 v1.5 signatures. Used only by the
  // Expo CLI and expo-updates' Node tooling for code-signing certificates, which this app does not
  // configure; the shipped app verifies nothing with it. No release fixes it yet.
  'node-forge': 'node-forge publishing a release above 1.4.0',
};

const run = promisify(execFile);

/**
 * One `npm audit --json`, returning the report only if we actually got one.
 *
 * npm exits non-zero whenever it finds anything, so a real report arrives on the error — but it also
 * answers with a small `{ "error": ... }` object when it cannot reach the registry, and that is valid
 * JSON with no `vulnerabilities` in it. Reading that as an empty report is how a network blip turned
 * into "every deferred advisory has been fixed, delete them all".
 */
async function audit() {
  const { stdout } = await run('npm', ['audit', '--json'], { maxBuffer: 32 * 1024 * 1024 }).catch(
    (error) => {
      if (error.stdout) return error;
      throw error;
    },
  );

  const report = JSON.parse(stdout);
  if (report.error || !report.vulnerabilities) {
    throw new Error(report.error?.summary ?? report.error?.detail ?? 'npm audit returned no report.');
  }
  return report;
}

// The registry is reachable often enough to be worth asking twice before calling it a failure. Same
// reasoning as the image pre-pull in CI: a transient blip should cost seconds, not a red build.
let report;
for (let attempt = 1; ; attempt += 1) {
  try {
    report = await audit();
    break;
  } catch (error) {
    if (attempt === 3) {
      console.error(`✖ Could not audit: ${error.message}`);
      console.error('  This is npm failing to produce a report, not a finding. Re-run the job.');
      process.exit(1);
    }
    await new Promise((resolve) => setTimeout(resolve, attempt * 2000));
  }
}

// Only packages that carry an advisory themselves. Everything that merely depends on one is listed
// by npm too (jest, expo, metro…), restating the same advisory once per package in the chain.
const found = Object.entries(report.vulnerabilities).filter(
  ([, advisory]) =>
    (advisory.severity === 'high' || advisory.severity === 'critical') &&
    advisory.via.some((source) => typeof source === 'object'),
);

const undeclared = found.filter(([name]) => !(name in deferred));
const stale = Object.keys(deferred).filter((name) => !found.some(([found]) => found === name));

for (const [name, advisory] of undeclared) {
  console.error(`✖ ${advisory.severity}: ${name} — not in the deferred list`);
}
for (const name of stale) {
  console.error(`✖ ${name} is listed as deferred but no longer reports an advisory — remove it`);
}

if (undeclared.length || stale.length) {
  console.error(
    '\nFix the advisory, or add it to `deferred` in scripts/audit.mjs with the reason and the work that clears it.',
  );
  process.exit(1);
}

console.log(
  `npm audit: no undeclared high or critical advisories (${found.length} deferred: ${[
    ...new Set(Object.values(deferred)),
  ].join(', ')}).`,
);
