import js from "@eslint/js";
import reactHooks from "eslint-plugin-react-hooks";
import reactRefresh from "eslint-plugin-react-refresh";
import globals from "globals";
import tseslint from "typescript-eslint";

export default tseslint.config(
  { ignores: ["dist", "src/api/schema.gen.ts"] },
  {
    files: ["**/*.{ts,tsx}"],
    extends: [js.configs.recommended, ...tseslint.configs.strict],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
    },
    plugins: {
      "react-hooks": reactHooks,
      "react-refresh": reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      "react-refresh/only-export-components": ["warn", { allowConstantExport: true }],
    },
  },
  {
    // The visitor's bundle never carries the authenticated app (DA-216, DA-222); the build's
    // bundle guard (build/visitorBundleGuard.ts) also catches what arrives indirectly.
    files: ["src/share/**/*.{ts,tsx}"],
    rules: {
      "no-restricted-imports": [
        "error",
        {
          patterns: [
            {
              regex: "^(@/|(\\.\\./)+)(api/client|app/|features/(session|account|agents|sharing)/)",
              message: "The visitor's page has its own client (visitorApi.ts) and no session (DA-216).",
            },
            {
              regex: "^(@/|(\\.\\./)+)features/graph/(localGraph|graphData|graphMessages|NodeInspector|recentChanges)$",
              message: "This module reads the API through the authenticated client; use graphView, graphShape or graphViewMessages (DA-222).",
            },
            {
              regex: "csrf|auth",
              message: "No auth or CSRF code in the visitor's page (DA-216).",
            },
          ],
        },
      ],
    },
  },
);
