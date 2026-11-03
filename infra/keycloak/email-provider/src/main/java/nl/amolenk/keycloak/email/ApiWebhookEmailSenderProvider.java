package nl.amolenk.keycloak.email;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.Map;

import org.keycloak.email.EmailException;
import org.keycloak.email.EmailSenderProvider;
import org.keycloak.models.UserModel;

/** Sends the JSON produced by the template provider to the communication API. */
public final class ApiWebhookEmailSenderProvider implements EmailSenderProvider {
    private static final HttpClient HTTP_CLIENT = HttpClient.newBuilder()
        .connectTimeout(Duration.ofSeconds(10))
        .build();

    private final String webhookUrl;

    ApiWebhookEmailSenderProvider(String webhookUrl) {
        this.webhookUrl = webhookUrl;
    }

    @Override
    public void send(Map<String, String> config, UserModel user, String subject, String textBody, String htmlBody)
        throws EmailException {
        send(config, user.getEmail(), subject, textBody, htmlBody);
    }

    @Override
    public void send(Map<String, String> config, String address, String subject, String textBody, String htmlBody)
        throws EmailException {
        validate(config);
        var request = HttpRequest.newBuilder(URI.create(webhookUrl))
            .timeout(Duration.ofSeconds(30))
            .header("Content-Type", "application/json")
            .POST(HttpRequest.BodyPublishers.ofString(textBody))
            .build();

        try {
            var response = HTTP_CLIENT.send(request, HttpResponse.BodyHandlers.discarding());
            if (response.statusCode() < 200 || response.statusCode() >= 300) {
                throw new EmailException("Email webhook returned HTTP " + response.statusCode());
            }
        } catch (IOException e) {
            throw new EmailException("Failed to call email webhook.", e);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new EmailException("Interrupted while calling email webhook.", e);
        }
    }

    @Override
    public void validate(Map<String, String> config) throws EmailException {
        if (webhookUrl == null || webhookUrl.isBlank()) {
            throw new EmailException("KEYCLOAK_EMAIL_WEBHOOK_URL is not configured.");
        }
    }

    @Override
    public void close() {
    }
}
