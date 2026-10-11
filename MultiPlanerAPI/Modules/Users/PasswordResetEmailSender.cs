using System.Net;
using System.Net.Mail;

namespace MultiPlanerAPI.Modules.Users;

public interface IPasswordResetEmailSender
{
    Task SendAsync(string recipient, string resetUrl, CancellationToken cancellationToken);
}

public sealed class SmtpPasswordResetEmailSender(IConfiguration configuration) : IPasswordResetEmailSender
{
    public async Task SendAsync(string recipient, string resetUrl, CancellationToken cancellationToken)
    {
        var host = configuration["Email:Smtp:Host"];
        var sender = configuration["Email:Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(sender))
            throw new InvalidOperationException("Configure Email:Smtp:Host and Email:Smtp:From.");
        using var client = new SmtpClient(host, configuration.GetValue("Email:Smtp:Port", 25))
        {
            EnableSsl = configuration.GetValue("Email:Smtp:EnableSsl", false),
            UseDefaultCredentials = false
        };
        var user = configuration["Email:Smtp:User"];
        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new NetworkCredential(user, configuration["Email:Smtp:Password"]);
        using var message = new MailMessage(sender, recipient, "MultiPlaner password reset",
            $"Open this link to reset your password:\n{resetUrl}\nThe link expires in one hour.");
        await client.SendMailAsync(message, cancellationToken);
    }
}
