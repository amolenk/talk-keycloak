import typescriptEslint from "typescript-eslint";
import reactRefresh from "eslint-plugin-react-refresh";
import reactHooks from "eslint-plugin-react-hooks";
import eslintConfigPrettier from "eslint-config-prettier";
import globals from "globals";
import js from "@eslint/js";
import storybook from "eslint-plugin-storybook";

export default typescriptEslint.config(
    {
        ignores: [
            "dist/**",
            "dist_keycloak/**",
            "storybook-static/**",
            "public/**",
            "src/kc.gen.tsx"
        ]
    },
    js.configs.recommended,
    ...typescriptEslint.configs.recommended,
    eslintConfigPrettier,
    ...storybook.configs["flat/recommended"],
    {
        plugins: {
            "react-refresh": reactRefresh,
            "react-hooks": reactHooks
        },
        languageOptions: {
            globals: {
                ...globals.browser
            }
        },
        rules: {
            "react-hooks/rules-of-hooks": "error",
            "react-refresh/only-export-components": [
                "warn",
                { allowConstantExport: true }
            ],
            "@typescript-eslint/no-redeclare": "off",
            "no-labels": "off"
        }
    }
);
