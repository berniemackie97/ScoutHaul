#!/usr/bin/env node
// Builds the extension into `dist/`, the same path every time, so loading it in a browser is a
// matter of picking a file you can find.
//
// `.output/` is WXT's working directory: it is wiped and rewritten on every build, and its layout is
// WXT's business. `dist/` is ours — stable, git-ignored, and the place to point a browser at.
//
//   pnpm --filter @overhaul/extension release           Firefox and Chrome
//   pnpm --filter @overhaul/extension release --firefox  Firefox only

import { execFileSync } from 'node:child_process';
import { cpSync, mkdirSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const extensionRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = join(extensionRoot, '.output');
const dist = join(extensionRoot, 'dist');
const { version } = JSON.parse(readFileSync(join(extensionRoot, 'package.json'), 'utf8'));
const firefoxOnly = process.argv.includes('--firefox');

const run = (args) => execFileSync('npx', args, { cwd: extensionRoot, stdio: 'inherit', env: process.env });

run(['wxt', 'build', '-b', 'firefox']);
run(['wxt', 'zip', '-b', 'firefox']);
if (!firefoxOnly) {
  run(['wxt', 'build', '-b', 'chrome']);
}

rmSync(dist, { recursive: true, force: true });
mkdirSync(dist, { recursive: true });
cpSync(join(output, 'firefox-mv3'), join(dist, 'firefox'), { recursive: true });
if (!firefoxOnly) {
  cpSync(join(output, 'chrome-mv3'), join(dist, 'chrome'), { recursive: true });
}

// An .xpi is a zip; Firefox takes either when loading a temporary add-on, and only the signed kind
// when installing one for keeps.
const zipped = readdirSync(output).find((file) => file.endsWith('-firefox.zip'));
if (!zipped) {
  console.error('✖ wxt zip produced no Firefox archive.');
  process.exit(1);
}

const xpi = `haulscout-sync-${version}.xpi`;
cpSync(join(output, zipped), join(dist, xpi));

console.log('');
console.log(`HaulScout Sync ${version} → apps/extension/dist/`);
console.log(`  firefox/manifest.json   ← pick this in about:debugging → Load Temporary Add-on…`);
console.log(`  ${xpi}${firefoxOnly ? '' : '\n  chrome/                 ← chrome://extensions → Load unpacked'}`);
console.log('');
console.log(`  ${join(dist, 'firefox', 'manifest.json')}`);
console.log('');
