package nl.amolenk.keycloak.email;

import java.io.IOException;
import java.time.Instant;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import org.keycloak.email.EmailException;
import org.keycloak.email.EmailSenderProvider;
import org.keycloak.email.EmailTemplateProvider;
import org.keycloak.events.Event;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.OrganizationModel;
import org.keycloak.models.RealmModel;
import org.keycloak.models.UserModel;
import org.keycloak.sessions.AuthenticationSessionModel;
import org.keycloak.util.JsonSerialization;

/** Captures the email's intent as JSON instead of rendering a Keycloak template. */
public final class JsonEmailTemplateProvider implements EmailTemplateProvider {
    private final KeycloakSession session;
    private final Map<String, Object> providerAttributes = new LinkedHashMap<>();
    private RealmModel realm;
    private UserModel user;
    private AuthenticationSessionModel authenticationSession;

    JsonEmailTemplateProvider(KeycloakSession session) {
        this.session = session;
    }

    @Override
    public EmailTemplateProvider setRealm(RealmModel realm) {
        this.realm = realm;
        return this;
    }

    @Override
    public EmailTemplateProvider setUser(UserModel user) {
        this.user = user;
        return this;
    }

    @Override
    public EmailTemplateProvider setAuthenticationSession(AuthenticationSessionModel authenticationSession) {
        this.authenticationSession = authenticationSession;
        return this;
    }

    @Override
    public EmailTemplateProvider setAttribute(String name, Object value) {
        providerAttributes.put(name, value);
        return this;
    }

    @Override
    public void sendPasswordReset(String link, long minutes) throws EmailException {
        sendLink("password-reset", "passwordResetSubject", "password-reset.ftl", link, minutes);
    }

    @Override
    public void sendVerifyEmail(String link, long minutes) throws EmailException {
        sendLink("verify-email", "emailVerificationSubject", "email-verification.ftl", link, minutes);
    }

    @Override
    public void sendVerifiableCredentialOffer(String link, long minutes) throws EmailException {
        sendLink("credential-offer", "verifiableCredentialOfferSubject", "verifiable-credential-offer.ftl", link, minutes);
    }

    @Override
    public void sendExecuteActions(String link, long minutes) throws EmailException {
        sendLink("execute-actions", "executeActionsSubject", "executeActions.ftl", link, minutes);
    }

    @Override
    public void sendConfirmIdentityBrokerLink(String link, long minutes) throws EmailException {
        sendLink("identity-provider-link", "identityProviderLinkSubject", "identity-provider-link.ftl", link, minutes);
    }

    @Override
    public void sendOrgInviteEmail(OrganizationModel organization, String link, long minutes) throws EmailException {
        var attributes = linkAttributes(link, minutes);
        attributes.put("organizationName", organization.getName());
        deliver("organization-invite", "orgInviteSubject", List.of(organization.getName()),
            "org-invite.ftl", attributes, null);
    }

    @Override
    public void sendEmailUpdateConfirmation(String link, long minutes, String address) throws EmailException {
        deliver("email-update-confirmation", "emailUpdateConfirmationSubject", List.of(),
            "email-update-confirmation.ftl", linkAttributes(link, minutes), address);
    }

    @Override
    public void sendSmtpTestEmail(Map<String, String> config, UserModel user) throws EmailException {
        setRealm(session.getContext().getRealm());
        setUser(user);
        deliver("smtp-test", "emailTestSubject", List.of(), "email-test.ftl", Map.of(), null);
    }

    @Override
    public void sendEvent(Event event) throws EmailException {
        deliver("event", "eventSubject", List.of(), "event.ftl", Map.of("eventType", event.getType().name()), null);
    }

    @Override
    public void send(String subject, String template, Map<String, Object> attributes) throws EmailException {
        send(subject, List.of(), template, attributes, null);
    }

    @Override
    public void send(String subject, List<Object> subjectAttributes, String template, Map<String, Object> attributes)
        throws EmailException {
        send(subject, subjectAttributes, template, attributes, null);
    }

    @Override
    public void send(String subject, String template, Map<String, Object> attributes, String address)
        throws EmailException {
        send(subject, List.of(), template, attributes, address);
    }

    @Override
    public void send(String subject, List<Object> subjectAttributes, String template,
        Map<String, Object> attributes, String address) throws EmailException {
        deliver("custom", subject, subjectAttributes, template, attributes, address);
    }

    @Override
    public void close() {
    }

    private void sendLink(String type, String subject, String template, String link, long minutes) throws EmailException {
        deliver(type, subject, List.of(), template, linkAttributes(link, minutes), null);
    }

    private static Map<String, Object> linkAttributes(String link, long minutes) {
        var attributes = new LinkedHashMap<String, Object>();
        attributes.put("link", link);
        attributes.put("linkExpirationMinutes", minutes);
        return attributes;
    }

    private void deliver(String type, String subject, List<Object> subjectAttributes, String template,
        Map<String, Object> attributes, String address) throws EmailException {
        var destination = address != null ? address : user.getEmail();
        var payload = new LinkedHashMap<String, Object>();
        payload.put("generatedAtUtc", Instant.now().toString());
        payload.put("emailType", type);
        payload.put("destinationEmail", destination);
        payload.put("subjectFormatKey", subject);
        payload.put("subjectAttributes", subjectAttributes);
        payload.put("bodyTemplate", template);
        payload.put("bodyAttributes", attributes);
        // The communication templates only need the required action names.
        payload.put("providerAttributes", Map.of("requiredActions",
            providerAttributes.getOrDefault("requiredActions", List.of())));
        payload.put("user", userProfile());
        payload.put("realm", Map.of("name", realm.getName()));
        payload.put("clientId", authenticationSession == null ? null : authenticationSession.getClient().getClientId());

        try {
            var json = JsonSerialization.writeValueAsString(payload);
            // The sender SPI treats the body as JSON; the API renders the actual email.
            session.getProvider(EmailSenderProvider.class).send(realm.getSmtpConfig(), destination, subject, json, json);
        } catch (IOException e) {
            throw new EmailException("Could not serialize the email payload.", e);
        }
    }

    private Map<String, Object> userProfile() {
        var profile = new LinkedHashMap<String, Object>();
        profile.put("id", user.getId());
        profile.put("username", user.getUsername());
        profile.put("email", user.getEmail());
        profile.put("firstName", user.getFirstName());
        return profile;
    }
}
