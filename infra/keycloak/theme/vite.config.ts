import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { keycloakify } from "keycloakify/vite-plugin";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

// https://vitejs.dev/config/
export default defineConfig({
    plugins: [
        react(),
        keycloakify({
            themeName: "city-picks",
            accountThemeImplementation: "none",
            // Own only the naming step; retain Keycloakify's WebAuthn protocol handling.
            postBuild: async ({ themeNames }) => {
                const promptBlock = [
                    "    let labelResult = window.prompt(initLabelPrompt, initLabel);",
                    "    if (labelResult === null) {",
                    "        labelResult = initLabel;",
                    "    }",
                    '    document.getElementById("authenticatorLabel").value = labelResult;'
                ].join("\n");

                for (const themeName of themeNames) {
                    // Keycloakify runs postBuild in the generated resources directory.
                    const scriptPath = join(
                        "theme",
                        themeName,
                        "login",
                        "resources",
                        "js",
                        "webauthnRegister.js"
                    );
                    const script = await readFile(scriptPath, "utf8");
                    const occurrences = script.split(promptBlock).length - 1;
                    if (occurrences !== 1) {
                        throw new Error(
                            `WebAuthn label prompt changed in ${scriptPath}; review the automatic-label override.`
                        );
                    }
                    await writeFile(
                        scriptPath,
                        script.replace(
                            promptBlock,
                            '    document.getElementById("authenticatorLabel").value = `${initLabel} ${crypto.randomUUID()}`;'
                        )
                    );
                }
            },
            keycloakVersionTargets: {
                "22-to-25": false,
                "all-other-versions": "city-picks.jar"
            }
        })
    ]
});
