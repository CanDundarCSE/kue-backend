using Kue.Api.Services.Email;

namespace Kue.Api.Extensions;

public static class EmailExtensions
{
    public static IServiceCollection AddBrevoEmail(this IServiceCollection services)
    {
        // Brevo credentials themselves are not validated here. An unconfigured account must
        // not prevent the API from starting; the email service logs and skips instead.
        // A typed client is used so the handler lifetime is managed by IHttpClientFactory.
        services.AddHttpClient<IEmailService, BrevoEmailService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}
