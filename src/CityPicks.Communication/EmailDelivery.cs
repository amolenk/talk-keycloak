using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace CityPicks.Communication;

public sealed class EmailDelivery(IConfiguration configuration)
{
    public async Task SendAsync(string recipient, RenderedEmail rendered)
    {
        using var message = new MailMessage("no-reply@citypicks.test", recipient)
        {
            Subject = rendered.Subject,
            Body = rendered.Text,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(rendered.Text, Encoding.UTF8, MediaTypeNames.Text.Plain));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(rendered.Html, Encoding.UTF8, MediaTypeNames.Text.Html));
        using var smtp = new SmtpClient(configuration["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host is required."),
            configuration.GetValue<int>("Smtp:Port")) { Timeout = 10000 };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await smtp.SendMailAsync(message, timeout.Token);
    }
}
