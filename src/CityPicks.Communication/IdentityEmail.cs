using System.Net;
using System.Net.Mail;
using System.Text.Json;

namespace CityPicks.Communication;

public sealed record IdentityEmail(
    string? EmailType,
    string? DestinationEmail,
    string? SubjectFormatKey,
    string? BodyTemplate,
    JsonElement? BodyAttributes,
    JsonElement? ProviderAttributes,
    JsonElement? User,
    JsonElement? Realm,
    string? ClientId,
    DateTimeOffset GeneratedAtUtc)
{
    public bool TryRender(out RenderedEmail? rendered, out string? error)
    {
        rendered = null;
        error = null;
        if (DestinationEmail is null || !MailAddress.TryCreate(DestinationEmail, out _))
        {
            error = "A valid destinationEmail is required.";
            return false;
        }

        (string Subject, string Heading, string Action, bool RequiresLink) content = EmailType switch
        {
            "password-reset" => ("Reset your City Picks password", "Choose a new password", "Reset my password", true),
            "verify-email" => ("Verify your City Picks email", "Confirm your email address", "Verify my email", true),
            "execute-actions" => ("Action needed on your City Picks account", "Update your account", "Update my account", true),
            "identity-provider-link" => ("Link your City Picks account", "Confirm the account link", "Link my account", true),
            "organization-invite" => ("Your City Picks invitation", "Accept your invitation", "Accept invitation", true),
            "email-update-confirmation" => ("Confirm your City Picks email change", "Confirm your new address", "Confirm address", true),
            "credential-offer" => ("Your City Picks credential", "Accept your credential", "Accept credential", true),
            "smtp-test" => ("City Picks communication test", "The communication API can deliver email", "", false),
            "event" => ("City Picks account activity", "There has been activity on your account", "", false),
            _ => default
        };
        if (content.Subject is null)
        {
            error = $"No local communication template for emailType '{EmailType}'.";
            return false;
        }

        string? link = null;
        long minutes = 0;
        var attributes = BodyAttributes.GetValueOrDefault();
        if (content.RequiresLink && (attributes.ValueKind != JsonValueKind.Object
            || !attributes.TryGetProperty("link", out var linkValue)
            || linkValue.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(link = linkValue.GetString(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !attributes.TryGetProperty("linkExpirationMinutes", out var expiry)
            || expiry.ValueKind != JsonValueKind.Number
            || !expiry.TryGetInt64(out minutes) || minutes <= 0))
        {
            error = "An action email requires an HTTP(S) link and positive linkExpirationMinutes.";
            return false;
        }

        var actionText = link is null ? "" : $"\n\n{content.Action}: {link}\nThis link expires in {minutes} minutes.";
        var actionHtml = link is null ? "" : $"""
            <p><a style="background:#23634c;color:white;padding:12px 20px;display:inline-block"
                  href="{WebUtility.HtmlEncode(link)}">{content.Action}</a></p>
            <p>This link expires in {minutes} minutes.</p>
            """;
        rendered = new(content.Subject, $"City Picks\n\n{content.Heading}{actionText}", $"""
            <!doctype html><html lang="en"><body style="font-family:Arial,sans-serif;background:#f3f1e9;padding:32px">
            <main style="max-width:560px;margin:auto;background:white;padding:32px;border-top:5px solid #23634c">
            <p style="color:#23634c;font-weight:bold">CITY PICKS</p><h1>{content.Heading}</h1>
            {actionHtml}<p>This is a local identity-email demo.</p></main></body></html>
            """);
        return true;
    }
}

public sealed record RenderedEmail(string Subject, string Text, string Html);
