using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;

namespace Kue.Api.Services.Email;

/// <summary>
/// Sends transactional email through the Brevo (formerly Sendinblue) REST API.
/// Uses the HTTPS API rather than the SMTP relay so outbound port 25/465/587 blocks
/// on common cloud hosts do not apply.
/// </summary>
public class BrevoEmailService : IEmailService
{
    private const string SendEndpoint = "https://api.brevo.com/v3/smtp/email";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BrevoEmailService> _logger;

    public BrevoEmailService(HttpClient httpClient, IConfiguration configuration, ILogger<BrevoEmailService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendEmailVerificationCodeAsync(string toEmail, string toName, string code, CancellationToken ct = default)
        => SendCodeEmailAsync(
            toEmail,
            toName,
            code,
            "Verify your Kue email",
            "Verify your email",
            "Enter this code in Kue to finish creating your account.",
            ct);

    public Task SendPasswordResetCodeEmailAsync(string toEmail, string toName, string code, CancellationToken ct = default)
        => SendCodeEmailAsync(
            toEmail,
            toName,
            code,
            "Reset your Kue password",
            "Reset your password",
            "Enter this code in Kue to choose a new password.",
            ct);

    private async Task SendCodeEmailAsync(
        string toEmail,
        string toName,
        string code,
        string subject,
        string heading,
        string intro,
        CancellationToken ct)
    {
        var apiKey = _configuration["Brevo:ApiKey"];
        var senderEmail = _configuration["Brevo:SenderEmail"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(senderEmail))
        {
            _logger.LogWarning("Brevo is not configured. Set 'Brevo:ApiKey' and 'Brevo:SenderEmail' via dotnet user-secrets.");
            return;
        }

        var senderName = _configuration["Brevo:SenderName"];
        if (string.IsNullOrWhiteSpace(senderName)) senderName = "Kue";

        var displayName = string.IsNullOrWhiteSpace(toName) ? "there" : toName;
        var greeting = HtmlEncoder.Default.Encode(displayName);
        var safeCode = HtmlEncoder.Default.Encode(code);
        var safeEmail = HtmlEncoder.Default.Encode(toEmail);

        var htmlContent = BuildCodeEmailHtml(greeting, safeCode, safeEmail, heading, intro);
        var textContent =
            $"Hi {displayName},\n\n" +
            $"{intro}\n\n" +
            $"Your code: {code}\n\n" +
            "This code is valid for 15 minutes and can be used once.\n\n" +
            "If you did not request this, you can safely ignore this email.";

        var recipient = new Dictionary<string, string> { ["email"] = toEmail };
        if (!string.IsNullOrWhiteSpace(toName)) recipient["name"] = toName;

        var payload = new Dictionary<string, object?>
        {
            ["sender"] = new Dictionary<string, string> { ["email"] = senderEmail, ["name"] = senderName },
            ["to"] = new[] { recipient },
            ["subject"] = subject,
            ["htmlContent"] = htmlContent,
            ["textContent"] = textContent
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("api-key", apiKey);

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Brevo rejected a {Subject} email with {StatusCode}: {Error}", subject, response.StatusCode, error);
            }
        }
        catch (Exception ex)
        {
            // Deliberately swallowed. The caller must not be able to tell whether a
            // signup or account exists based on whether the mail provider accepted the message.
            _logger.LogError(ex, "Failed to send {Subject} email via Brevo", subject);
        }
    }

    /// <summary>
    /// Code email in the app's "Index" design: white card on #F6F6F3,
    /// hairline dividers, Instrument Serif display heading with the blue accent
    /// period, Fragment Mono microtype labels. Table layout + inline styles only
    /// (no style block) so Gmail and Outlook render it correctly.
    /// </summary>
    private static string BuildCodeEmailHtml(
        string greeting,
        string safeCode,
        string recipientEmail,
        string heading,
        string intro) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="light">
        <meta name="supported-color-schemes" content="light">
        <title>{HtmlEncoder.Default.Encode(heading)}</title>
        <link rel="preconnect" href="https://fonts.googleapis.com">
        <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
        <link href="https://fonts.googleapis.com/css2?family=Instrument+Sans:wght@400;600&family=Instrument+Serif:ital@0;1&family=Fragment+Mono&display=swap" rel="stylesheet">
        </head>
        <body style="margin:0;padding:0;background-color:#F6F6F3;">
        <div style="display:none;max-height:0;overflow:hidden;opacity:0;">Your code is valid for 15 minutes — single use.</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#F6F6F3" style="background-color:#F6F6F3;">
          <tr>
            <td align="center" style="padding:40px 16px;">
              <table role="presentation" width="560" cellpadding="0" cellspacing="0" border="0" style="width:100%;max-width:560px;background-color:#FFFFFF;border:1px solid #E8E8E3;border-radius:10px;">

                <tr>
                  <td style="padding:30px 32px 0;">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                      <td width="10" height="10" bgcolor="#1E36C8" style="width:10px;height:10px;background-color:#1E36C8;border-radius:3px;font-size:1px;line-height:1px;">&nbsp;</td>
                      <td style="padding-left:8px;font-family:'Instrument Sans',-apple-system,'Segoe UI',Helvetica,Arial,sans-serif;font-size:14px;font-weight:600;letter-spacing:-0.2px;color:#161613;">Kue</td>
                    </tr></table>
                  </td>
                </tr>

                <tr>
                  <td style="padding:34px 32px 0;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                      <tr><td style="border-bottom:1px solid #E8E8E3;padding-bottom:10px;font-family:'Fragment Mono','Courier New',monospace;font-size:10px;letter-spacing:2px;text-transform:uppercase;color:#9A9A91;">Account security</td></tr>
                    </table>
                  </td>
                </tr>

                <tr>
                  <td style="padding:20px 32px 0;font-family:'Instrument Serif',Georgia,'Times New Roman',serif;font-size:33px;line-height:1.12;font-weight:400;letter-spacing:-0.3px;color:#161613;">
                    {HtmlEncoder.Default.Encode(heading)}<span style="color:#1E36C8;">.</span>
                  </td>
                </tr>

                <tr>
                  <td style="padding:12px 32px 0;font-family:'Instrument Sans',-apple-system,'Segoe UI',Helvetica,Arial,sans-serif;font-size:15px;line-height:24px;color:#63635D;">
                    Hi <strong style="font-weight:600;color:#161613;">{greeting}</strong> — {HtmlEncoder.Default.Encode(intro)}
                  </td>
                </tr>

                <tr>
                  <td style="padding:24px 32px 0;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#EEF0FC;border:1px solid #D6DAF8;border-radius:10px;">
                      <tr>
                        <td align="center" style="padding:20px 16px;font-family:'Fragment Mono','Courier New',monospace;font-size:26px;line-height:1;letter-spacing:5px;font-weight:700;color:#1E36C8;">
                          {safeCode}
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>

                <tr>
                  <td style="padding:14px 32px 0;font-family:'Fragment Mono','Courier New',monospace;font-size:10px;letter-spacing:1.5px;text-transform:uppercase;color:#9A9A91;">
                    Valid for 15 minutes &middot; single use
                  </td>
                </tr>

                <tr>
                  <td style="padding:28px 32px 30px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                      <tr><td style="border-top:1px solid #E8E8E3;padding-top:14px;font-family:'Fragment Mono','Courier New',monospace;font-size:10px;line-height:18px;color:#9A9A91;">
                        If you did not request this, you can safely ignore this email.<br>
                        Sent to {recipientEmail}
                      </td></tr>
                      <tr><td style="padding-top:12px;font-family:'Instrument Serif',Georgia,'Times New Roman',serif;font-style:italic;font-size:13px;color:#9A9A91;">
                        One catalog for everything you watch, play &amp; read.
                      </td></tr>
                    </table>
                  </td>
                </tr>

              </table>
            </td>
          </tr>
        </table>
        </body>
        </html>
        """;
}
