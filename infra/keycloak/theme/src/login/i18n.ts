/* eslint-disable @typescript-eslint/no-unused-vars */
import { i18nBuilder } from "keycloakify/login";
import type { ThemeName } from "../kc.gen";

/** @see: https://docs.keycloakify.dev/features/i18n */
const { useI18n, ofTypeI18n } = i18nBuilder
    .withThemeName<ThemeName>()
    .withCustomTranslations({
        en: {
            "webauthn-registration-init-label": "Passkey",
            cityPicksTagline: "Hand-picked places, kept with the people who love them.",
            cityPicksFooter:
                "Your City Picks account is managed by our own identity service.",
            cityPicksLoginSubtitle: "Sign in to see your favourites and curated picks.",
            cityPicksRegisterSubtitle: "Create an account to save the places you love.",
            cityPicksPasskeyDivider: "or",
            cityPicksWebauthnRegisterIntro:
                "Use your device's screen lock, fingerprint or face to sign in to City Picks without a password."
        }
    })
    .build();

type I18n = typeof ofTypeI18n;

export { useI18n, type I18n };
