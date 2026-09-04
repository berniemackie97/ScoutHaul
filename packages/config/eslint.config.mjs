import { FlatCompat } from "@eslint/eslintrc";

const compat = new FlatCompat({ baseDirectory: import.meta.dirname });

/**
 * The lint rules both Next apps run.
 *
 * Neither app had a config, and `eslint` was not even installed — so `next lint` had never been
 * runnable and nothing had ever been checked. The scripts now call the ESLint CLI directly rather
 * than `next lint`, which Next 15 deprecates and Next 16 removes.
 *
 * Kept alongside the shared tsconfig and tailwind config, so the two apps cannot drift on what
 * counts as acceptable.
 */
export const webConfig = [
  {
    ignores: [
      "**/.next/**",
      "**/node_modules/**",
      "**/dist/**",
      "**/.turbo/**",
      "**/coverage/**",
      "next-env.d.ts",
    ],
  },
  ...compat.extends("next/core-web-vitals", "next/typescript"),
  {
    rules: {
      // Everything below is a warning on purpose. Turning a first-ever lint run into errors would
      // mean either a red build or a pile of blind autofixes; as warnings the findings are visible
      // and the count is held by --max-warnings, which only moves down.
      "@typescript-eslint/no-unused-vars": [
        "warn",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_", caughtErrorsIgnorePattern: "^_" },
      ],
      "@typescript-eslint/no-explicit-any": "warn",
      "react-hooks/exhaustive-deps": "warn",
    },
  },
];

export default webConfig;
