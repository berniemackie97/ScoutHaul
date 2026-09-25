import tseslint from "typescript-eslint";
import reactHooks from "eslint-plugin-react-hooks";

/**
 * The extension is not a Next app, so it does not take the portal's Next-based config; it holds the
 * same line on TypeScript and hooks, and the same zero-warning bar.
 */
export default tseslint.config(
  // Built extensions (`firefox-mv3/` and the like, wherever they are unpacked) are output, not source.
  { ignores: [".output/**", ".wxt/**", "dist/**", "node_modules/**", "coverage/**", "*-mv2/**", "*-mv3/**"] },
  ...tseslint.configs.recommended,
  {
    plugins: { "react-hooks": reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,
      "@typescript-eslint/no-unused-vars": [
        "warn",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_", caughtErrorsIgnorePattern: "^_" },
      ],
    },
  },
);
