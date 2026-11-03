import { useEffect } from "react";
import { clsx } from "keycloakify/tools/clsx";
import { kcSanitize } from "keycloakify/lib/kcSanitize";
import type { TemplateProps } from "keycloakify/login/TemplateProps";
import { getKcClsx } from "keycloakify/login/lib/kcClsx";
import { useSetClassName } from "keycloakify/tools/useSetClassName";
import { useInitialize } from "keycloakify/login/Template.useInitialize";
import type { I18n } from "./i18n";
import type { KcContext } from "./KcContext";

/**
 * City Picks layout shared by every login page.
 * Ejected from keycloakify/login/Template and rebuilt without PatternFly:
 * a brand column on the left and a single form card on the right.
 * Element ids that Keycloak scripts and the integration tests rely on are kept
 * (#kc-header-wrapper, #kc-page-title, #kc-content, #kc-info, #try-another-way, ...).
 */
export default function Template(props: TemplateProps<KcContext, I18n>) {
    const {
        displayInfo = false,
        displayMessage = true,
        displayRequiredFields = false,
        headerNode,
        socialProvidersNode = null,
        infoNode = null,
        documentTitle,
        bodyClassName,
        kcContext,
        i18n,
        doUseDefaultCss,
        classes,
        children
    } = props;

    const { kcClsx } = getKcClsx({ doUseDefaultCss, classes });

    const { msg, msgStr, currentLanguage, enabledLanguages } = i18n;

    const { realm, auth, url, message, isAppInitiatedAction } = kcContext;

    const realmDisplayName = realm.displayName || realm.name;

    useEffect(() => {
        document.title = documentTitle ?? msgStr("loginTitle", realmDisplayName);
    }, []);

    useSetClassName({
        qualifiedName: "html",
        className: kcClsx("kcHtmlClass")
    });

    useSetClassName({
        qualifiedName: "body",
        className: bodyClassName ?? kcClsx("kcBodyClass")
    });

    const { isReadyToRender } = useInitialize({ kcContext, doUseDefaultCss });

    if (!isReadyToRender) {
        return null;
    }

    return (
        <div className={clsx("cp-page", kcClsx("kcLoginClass"))}>
            <aside className="cp-brand">
                <div id="kc-header" className={kcClsx("kcHeaderClass")}>
                    <span className="cp-mark" aria-hidden="true">
                        <svg viewBox="0 0 24 24" width="32" height="32" focusable="false">
                            {/* Same "+" mark as the email header (src/email/html/template.ftl). */}
                            <path fill="currentColor" d="M10.5 4h3v6.5H20v3h-6.5V20h-3v-6.5H4v-3h6.5Z" />
                        </svg>
                    </span>
                    <div id="kc-header-wrapper" className={kcClsx("kcHeaderWrapperClass")}>
                        {realmDisplayName}
                    </div>
                </div>
                <p className="cp-tagline">{msg("cityPicksTagline")}</p>
            </aside>

            <main className={clsx("cp-card", kcClsx("kcFormCardClass"))}>
                <header className={clsx("cp-card__header", kcClsx("kcFormHeaderClass"))}>
                    {enabledLanguages.length > 1 && (
                        <div className={clsx("cp-locale", kcClsx("kcLocaleMainClass"))} id="kc-locale">
                            <div id="kc-locale-wrapper" className={kcClsx("kcLocaleWrapperClass")}>
                                <div id="kc-locale-dropdown" className={clsx("menu-button-links", kcClsx("kcLocaleDropDownClass"))}>
                                    <button
                                        tabIndex={1}
                                        id="kc-current-locale-link"
                                        aria-label={msgStr("languages")}
                                        aria-haspopup="true"
                                        aria-expanded="false"
                                        aria-controls="language-switch1"
                                    >
                                        {currentLanguage.label}
                                    </button>
                                    <ul
                                        role="menu"
                                        tabIndex={-1}
                                        aria-labelledby="kc-current-locale-link"
                                        aria-activedescendant=""
                                        id="language-switch1"
                                        className={kcClsx("kcLocaleListClass")}
                                    >
                                        {enabledLanguages.map(({ languageTag, label, href }, i) => (
                                            <li key={languageTag} className={kcClsx("kcLocaleListItemClass")} role="none">
                                                <a role="menuitem" id={`language-${i + 1}`} className={kcClsx("kcLocaleItemClass")} href={href}>
                                                    {label}
                                                </a>
                                            </li>
                                        ))}
                                    </ul>
                                </div>
                            </div>
                        </div>
                    )}

                    {auth !== undefined && auth.showUsername && !auth.showResetCredentials ? (
                        <div id="kc-username" className="cp-attempted-user">
                            <label id="kc-attempted-username">{auth.attemptedUsername}</label>
                            <a id="reset-login" href={url.loginRestartFlowUrl} aria-label={msgStr("restartLoginTooltip")}>
                                {msg("restartLoginTooltip")}
                            </a>
                        </div>
                    ) : (
                        <h1 id="kc-page-title">{headerNode}</h1>
                    )}

                    {displayRequiredFields && (
                        <p className="cp-required-hint subtitle">
                            <span className="required">*</span> {msg("requiredFields")}
                        </p>
                    )}
                </header>

                <div id="kc-content">
                    <div id="kc-content-wrapper">
                        {/* App-initiated actions should not see warning messages about the need to complete the action during login. */}
                        {displayMessage && message !== undefined && (message.type !== "warning" || !isAppInitiatedAction) && (
                            <div
                                role={message.type === "error" ? "alert" : "status"}
                                className={clsx("cp-alert", `cp-alert--${message.type}`, `alert-${message.type}`, kcClsx("kcAlertClass"))}
                            >
                                <span
                                    className={kcClsx("kcAlertTitleClass")}
                                    dangerouslySetInnerHTML={{
                                        __html: kcSanitize(message.summary)
                                    }}
                                />
                            </div>
                        )}

                        {children}

                        {auth !== undefined && auth.showTryAnotherWayLink && (
                            <form id="kc-select-try-another-way-form" action={url.loginAction} method="post">
                                <div className={kcClsx("kcFormGroupClass")}>
                                    <input type="hidden" name="tryAnotherWay" value="on" />
                                    <a
                                        href="#"
                                        id="try-another-way"
                                        onClick={event => {
                                            document.forms["kc-select-try-another-way-form" as never].requestSubmit();
                                            event.preventDefault();
                                            return false;
                                        }}
                                    >
                                        {msg("doTryAnotherWay")}
                                    </a>
                                </div>
                            </form>
                        )}

                        {socialProvidersNode}

                        {displayInfo && (
                            <div id="kc-info" className={clsx("cp-info", kcClsx("kcSignUpClass"))}>
                                <div id="kc-info-wrapper" className={kcClsx("kcInfoAreaWrapperClass")}>
                                    {infoNode}
                                </div>
                            </div>
                        )}
                    </div>
                </div>
            </main>

            <footer className="cp-footer">{msg("cityPicksFooter")}</footer>
        </div>
    );
}
