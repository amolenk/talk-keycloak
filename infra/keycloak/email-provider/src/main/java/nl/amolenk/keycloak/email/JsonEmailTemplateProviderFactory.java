package nl.amolenk.keycloak.email;

import org.keycloak.Config;
import org.keycloak.email.EmailTemplateProvider;
import org.keycloak.email.EmailTemplateProviderFactory;
import org.keycloak.models.KeycloakSession;
import org.keycloak.models.KeycloakSessionFactory;

public final class JsonEmailTemplateProviderFactory implements EmailTemplateProviderFactory {
    @Override
    public EmailTemplateProvider create(KeycloakSession session) {
        return new JsonEmailTemplateProvider(session);
    }

    @Override
    public String getId() {
        return "json";
    }

    @Override
    public void init(Config.Scope config) {
    }

    @Override
    public void postInit(KeycloakSessionFactory factory) {
    }

    @Override
    public void close() {
    }
}
