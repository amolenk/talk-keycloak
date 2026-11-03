using System.Net.Mail;
using System.Text.Json;
using CityPicks.Communication;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddSingleton<EmailDelivery>();
var app = builder.Build();
app.MapHealthChecks("/health");

app.MapPost("/api/keycloak-email", async (IdentityEmail email, EmailDelivery delivery, ILogger<Program> logger) =>
{
    if (!email.TryRender(out var rendered, out var error))
        return Results.UnprocessableEntity(new { error });

    // This local PoC logs the contract for the live demo; action links are demo credentials.
    logger.LogInformation("Received Keycloak email payload: {Payload}",
        JsonSerializer.Serialize(email, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    try
    {
        await delivery.SendAsync(email.DestinationEmail!, rendered!);
        return Results.Ok(new { email.EmailType, delivered = true });
    }
    catch (SmtpException exception)
    {
        logger.LogError(exception, "Could not deliver {EmailType} email to Mailpit", email.EmailType);
        return Results.Problem("Local email delivery failed.", statusCode: 502);
    }
    catch (OperationCanceledException)
    {
        return Results.Problem("Local email delivery timed out.", statusCode: 502);
    }
});

app.Run();
