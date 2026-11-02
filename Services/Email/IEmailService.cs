namespace Kue.Api.Services.Email;

public interface IEmailService
{
    Task SendEmailVerificationCodeAsync(string toEmail, string toName, string code, CancellationToken ct = default);
    Task SendPasswordResetCodeEmailAsync(string toEmail, string toName, string code, CancellationToken ct = default);
}
