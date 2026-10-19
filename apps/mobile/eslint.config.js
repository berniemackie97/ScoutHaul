// Flat config. Expo's shared config already wires the React, React Native, import and TypeScript
// plugins for this runtime, so this file only records where we deliberately differ.
const { defineConfig } = require('eslint/config');
const expoConfig = require('eslint-config-expo/flat');
const reactHooks = require('eslint-plugin-react-hooks');

// Files that still trip a rule we otherwise treat as an error. This list is SHRINK-ONLY: adding a
// new entry needs a reason in review, and each one names the slice that deletes it. Same idea as
// the backend's architecture ratchet in services/api/tests/HaulScout.Tests/ArchitectureTests.cs.
const knownDebt = {
  // Fetch-on-mount: the effect calls a loader that flips a loading flag synchronously. There is no
  // data-fetching layer yet, so every screen hand-rolls this. Removed wholesale when the shared
  // query layer lands, not screen by screen.
  fetchOnMount: ['app/marketplace-links.tsx', 'src/features/workspaceAdmin/InviteCodeManager.tsx'],
  // The two biggest screens. Their effects are entangled with form state; they are scheduled for
  // dedicated decomposition slices and get fixed there.
  bigScreens: ['app/inventory/edit.tsx', 'app/record-sale.tsx'],
};

// eslint-config-expo is pinned to the version that matches Expo SDK 54 (`expo install --check`
// enforces that), and it carries react-hooks v5. v6 adds the compiler-era rules — reading refs
// during render, setState inside an effect, manual memoization that cannot be preserved — which
// catch a class of bug the classic rules do not, so the plugin is upgraded on its own below. Flat
// config refuses to redefine a plugin, so Expo's copy is dropped from its config first; keeping the
// `react-hooks/` prefix rather than namespacing it means rule names still match the React docs.
const expoWithoutReactHooks = expoConfig.map((entry) =>
  entry.plugins?.['react-hooks']
    ? {
        ...entry,
        plugins: Object.fromEntries(
          Object.entries(entry.plugins).filter(([name]) => name !== 'react-hooks'),
        ),
      }
    : entry,
);

module.exports = defineConfig([
  expoWithoutReactHooks,
  {
    ignores: ['dist/*', 'android/*', '.expo/*', 'node_modules/*'],
  },
  {
    plugins: { 'react-hooks': reactHooks },
    rules: {
      // `recommended-latest` is the compiler-era set; plain `recommended` is only
      // rules-of-hooks + exhaustive-deps and would miss everything this upgrade is for.
      ...reactHooks.configs['recommended-latest'][0].rules,
      // `useRef(new Animated.Value(0)).current` is the documented React Native animation idiom and
      // it appears at 63 sites. The rule is right in general — the ref is read during render — but
      // an Animated.Value is driven by the animation system and never feeds render output, so the
      // failure it warns about cannot happen here. Migrating those sites is its own slice; until
      // then this stays visible as a warning rather than blocking every build.
      'react-hooks/refs': 'warn',
      // Fires when the React Compiler would bail out of a hand-written useMemo/useCallback. The
      // compiler is not enabled (babel.config.js runs plain babel-preset-expo), so this reports
      // nothing about how the app behaves today — it is the pre-work list for turning the compiler
      // on later, which is worth seeing but not worth failing a build over.
      'react-hooks/preserve-manual-memoization': 'warn',
    },
  },
  {
    files: [...knownDebt.fetchOnMount, ...knownDebt.bigScreens],
    rules: {
      'react-hooks/set-state-in-effect': 'warn',
      'react-hooks/immutability': 'warn',
    },
  },
]);
