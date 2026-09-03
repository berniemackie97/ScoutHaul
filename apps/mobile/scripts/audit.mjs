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
  // build time from this repo, so there is no untrusted input and nothing in the shipped app. The
  // only remedy npm offers is expo@57, a major SDK upgrade (we are on 54) that needs its own slice.
  'image-size': 'Expo SDK 54 → 57 upgrade',
  '@expo/cli': 'Expo SDK 54 → 57 upgrade',
  '@expo/metro': 'Expo SDK 54 → 57 upgrade',
  '@expo/metro-config': 'Expo SDK 54 → 57 upgrade',
  expo: 'Expo SDK 54 → 57 upgrade',
  metro: 'Expo SDK 54 → 57 upgrade',
  'metro-config': 'Expo SDK 54 → 57 upgrade',
  'metro-transform-worker': 'Expo SDK 54 → 57 upgrade',
};

const run = promisify(execFile);
// `npm audit` exits non-zero whenever it finds anything, so the report comes off the error too.
const { stdout } = await run('npm', ['audit', '--json'], { maxBuffer: 32 * 1024 * 1024 }).catch(
  (error) => {
    if (error.stdout) return error;
    throw error;
  },
);

const found = Object.entries(JSON.parse(stdout).vulnerabilities ?? {}).filter(
  ([, advisory]) => advisory.severity === 'high' || advisory.severity === 'critical',
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
