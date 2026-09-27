namespace Kue.Api.Services.Email;

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetLink, CancellationToken ct = default);
}
