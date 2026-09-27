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

    public async Task SendPasswordResetEmailAsync(string toEmail, string toName, string resetLink, CancellationToken ct = default)
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

        // Anything interpolated into the HTML body is encoded, so a display name can
        // never inject markup into the outgoing message. The plain-text body uses the
        // raw name — HTML entities would be shown literally to the reader.
        var greeting = HtmlEncoder.Default.Encode(displayName);
        var safeLink = HtmlEncoder.Default.Encode(resetLink);
        var safeEmail = HtmlEncoder.Default.Encode(toEmail);

        var htmlContent = BuildPasswordResetHtml(greeting, safeLink, safeEmail);
        var textContent =
            $"Hi {displayName},\n\n" +
            "We received a request to reset the password for your Kue account.\n" +
            "This link is valid for 1 hour and can be used once:\n\n" +
            $"{resetLink}\n\n" +
            "If you did not request this, you can safely ignore this email. Your password will not change.";

        var recipient = new Dictionary<string, string> { ["email"] = toEmail };
        if (!string.IsNullOrWhiteSpace(toName)) recipient["name"] = toName;

        var payload = new Dictionary<string, object?>
        {
            ["sender"] = new Dictionary<string, string> { ["email"] = senderEmail, ["name"] = senderName },
            ["to"] = new[] { recipient },
            ["subject"] = "Reset your Kue password"
        };

        // A Brevo templateId takes precedence over the inline bodies, so only the
        // unconfigured path uses the HTML above.
        if (int.TryParse(_configuration["Brevo:PasswordResetTemplateId"], out var templateId) && templateId > 0)
        {
            payload["templateId"] = templateId;
            payload["params"] = new Dictionary<string, string>
            {
                ["reset_link"] = resetLink,
                ["display_name"] = displayName
            };
        }
        else
        {
            payload["htmlContent"] = htmlContent;
            payload["textContent"] = textContent;
        }

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
                _logger.LogError("Brevo rejected password reset email with {StatusCode}: {Error}", response.StatusCode, error);
            }
        }
        catch (Exception ex)
        {
            // Deliberately swallowed. The caller must not be able to tell whether an
            // account exists based on whether the mail provider accepted the message.
            _logger.LogError(ex, "Failed to send password reset email via Brevo");
        }
    }

    /// <summary>
    /// Password reset email in the app's "Index" design: white card on #F6F6F3,
    /// hairline dividers, Instrument Serif display heading with the blue accent
    /// period, Fragment Mono microtype labels, Klein-blue primary button.
    /// Table layout + inline styles only (no style block) so Gmail and Outlook
    /// render it correctly. Google Fonts load in Apple Mail / iOS / Outlook Mac;
    /// Gmail falls back to Georgia / Helvetica / Courier, which keeps the look.
    /// </summary>
    private static string BuildPasswordResetHtml(string greeting, string safeLink, string recipientEmail) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="light">
        <meta name="supported-color-schemes" content="light">
        <title>Reset your Kue password</title>
        <link rel="preconnect" href="https://fonts.googleapis.com">
        <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
        <link href="https://fonts.googleapis.com/css2?family=Instrument+Sans:wght@400;600&family=Instrument+Serif:ital@0;1&family=Fragment+Mono&display=swap" rel="stylesheet">
        </head>
        <body style="margin:0;padding:0;background-color:#F6F6F3;">
        <div style="display:none;max-height:0;overflow:hidden;opacity:0;">Reset link valid for 1 hour — single use.</div>
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
                    Reset your password<span style="color:#1E36C8;">.</span>
                  </td>
                </tr>

                <tr>
                  <td style="padding:12px 32px 0;font-family:'Instrument Sans',-apple-system,'Segoe UI',Helvetica,Arial,sans-serif;font-size:15px;line-height:24px;color:#63635D;">
                    Hi <strong style="font-weight:600;color:#161613;">{greeting}</strong> — we received a request to reset the password for your Kue account. Use the button below to choose a new one.
                  </td>
                </tr>

                <tr>
                  <td style="padding:20px 32px 0;">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                      <td bgcolor="#EEF0FC" style="background-color:#EEF0FC;border:1px solid #1E36C8;border-radius:999px;">
                        <span style="display:inline-block;padding:6px 14px;font-family:'Fragment Mono','Courier New',monospace;font-size:10px;letter-spacing:1.5px;text-transform:uppercase;color:#1E36C8;">Valid for 1 hour &middot; single use</span>
                      </td>
                    </tr></table>
                  </td>
                </tr>

                <tr>
                  <td style="padding:26px 32px 0;">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                      <td bgcolor="#1E36C8" style="background-color:#1E36C8;border-radius:9px;">
                        <a href="{safeLink}" style="display:inline-block;padding:13px 22px;font-family:'Instrument Sans',-apple-system,'Segoe UI',Helvetica,Arial,sans-serif;font-size:14px;font-weight:600;color:#FFFFFF;text-decoration:none;border-radius:9px;">Reset password</a>
                      </td>
                    </tr></table>
                  </td>
                </tr>

                <tr>
                  <td style="padding:30px 32px 0;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                      <tr><td style="border-top:1px solid #E8E8E3;padding:14px 0 8px;font-family:'Fragment Mono','Courier New',monospace;font-size:10px;letter-spacing:2px;text-transform:uppercase;color:#9A9A91;">Button not working?</td></tr>
                      <tr><td style="font-family:'Fragment Mono','Courier New',monospace;font-size:12px;line-height:20px;color:#1E36C8;word-break:break-all;">{safeLink}</td></tr>
                    </table>
                  </td>
                </tr>

                <tr>
                  <td style="padding:28px 32px 30px;">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
                      <tr><td style="border-top:1px solid #E8E8E3;padding-top:14px;font-family:'Fragment Mono','Courier New',monospace;font-size:10px;line-height:18px;color:#9A9A91;">
                        If you did not request this, you can safely ignore this email — your password will not change.<br>
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
