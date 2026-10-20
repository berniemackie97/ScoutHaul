# Mobile remodel

Taking `apps/mobile` from a capable MVP to a product that holds up in daily business use and, later,
in other businesses' hands. One slice per PR, re-audited between slices.

## Decisions (2026-10-18)

- **Look:** the Overhaul brand system shared with the admin portal — ink navy, copper, bone paper,
  tight radius, light and dark. Retire the stock indigo palette, gradient heroes, decorative circles
  and staggered entrance animations.
- **Name:** HaulScout stays the visible name. Persisted identifiers (bundle `com.scouthaul.app`,
  scheme `scouthaul`, storage keys) stay as they are; renaming them breaks installs and the OAuth
  redirect allow-list.
- **Accounts:** sign-in is required. Local/no-account mode, the solo/team/join usage modes and the
  "starter" plan label go. Offline capture keeps working through the sync queue.
- **Tools kept:** sourcing trips and the active route, budget and expenses, tasks, deal analyzer,
  comps and marketplace links.

## Baseline (2026-10-18, `main` at 005a4f4)

Expo SDK 54 (58 is current) · 160 source files, 26.8k lines · tsc clean · 224 tests passing ·
79 lint warnings under a budget of 81 · 363 `useState` · one shared data hook used by two screens;
the rest fetch by hand in `useFocusEffect` · god screens: `inventory/edit` 1194, `scanner` 1133,
`account` 890, `onboarding` 791, `ScanResults` 790.

## Order

1. **Appraisal** — identify, comp, price, buy check. Its own design: `APPRAISAL.md`.
2. **Security and tenancy** — done in `bernie/MobileSecurityTenancy`
   - Queued offline writes do not record the workspace or user they were made in, so a write made
     in one workspace replays into whichever is active when it syncs. Bind each row to both.
   - The Supabase session (refresh token included) is persisted in plain AsyncStorage. Encrypt it
     with a key held in SecureStore.
   - OAuth uses the implicit flow (tokens in the URL fragment). Move to PKCE.
   - Sign-out leaves the previous user's queue, drafts, photos and route snapshots on the device.
   - `android.allowBackup` is on by default; turn it off.
   - `appConfig.env` reads a build profile nobody sets, so it is always `development`.
   - The theme choice is not persisted.
3. **Platform** — Expo SDK 54 → 58, React Compiler, lint warnings to zero and the budget to 0.
4. **Data layer** — TanStack Query (offline-aware, persisted cache) replaces hand-rolled fetching;
   API types generated from the OpenAPI schema replace ~1,200 hand-mirrored lines.
5. **Design system** — brand tokens, consolidated components, one toast/feedback system replacing
   22 `Alert.alert` calls and ad-hoc message banners.
6. **Screens** — onboarding and account, home, inventory, item editor, orders and record sale
   (including a tracking-number input), purchases, trips, settings and workspace admin.
7. **Production readiness** — crash reporting, Maestro smoke tests in CI, iOS privacy manifest and
   permission strings, OTA update policy, accessibility (labels, dynamic type, 44 pt targets),
   performance (virtualised lists, image sizing, parallel startup).
