namespace Kue.Api.Extensions;

public static class CorsExtensions
{
    public const string AllowFrontendPolicy = "AllowFrontend";

    private static readonly string[] DefaultOrigins =
    [
        "http://localhost:3000",
        "https://localhost:3000",
        "http://localhost:5173",
        "https://localhost:5173"
    ];

    public static IServiceCollection AddCorsPolicy(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Production origins come from configuration so the same image works in
        // every environment. Set `Cors__AllowedOrigins` to a comma-separated
        // list, e.g. "https://kue.vercel.app,https://kue-git-main.vercel.app".
        var configured = configuration["Cors:AllowedOrigins"];
        var origins = string.IsNullOrWhiteSpace(configured)
            ? DefaultOrigins
            : configured
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        services.AddCors(options =>
        {
            options.AddPolicy(AllowFrontendPolicy, policy =>
            {
                policy.WithOrigins(origins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        return services;
    }
}
