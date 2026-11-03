package nl.amolenk.keycloak.email;

import org.keycloak.Config;
import org.keycloak.email.EmailSenderProvider;
import org.keycloak.email.EmailSenderProviderFactory;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;

public final class ApiWebhookEmailSenderProviderFactory implements EmailSenderProviderFactory {
    private String webhookUrl;

    @Override
    public EmailSenderProvider create(KeycloakSession session) {
        return new ApiWebhookEmailSenderProvider(webhookUrl);
    }

    @Override
    public String getId() {
        return "api-webhook";
    }

    @Override
    public void init(Config.Scope config) {
        webhookUrl = config.get("webhook-url", System.getenv("KEYCLOAK_EMAIL_WEBHOOK_URL"));
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
    }

    @Override
    public void close() {
    }
}
