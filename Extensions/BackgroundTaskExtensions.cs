using Kue.Api.Services.Background;

namespace Kue.Api.Extensions;

public static class BackgroundTaskExtensions
{
    public static IServiceCollection AddBackgroundTaskQueue(this IServiceCollection services)
    {
        // Registered once as a concrete singleton and exposed under both its own type and
        // the queue interface, so the hosted worker and request handlers share one channel.
        services.AddSingleton<BackgroundTaskQueue>();
        services.AddSingleton<IBackgroundTaskQueue>(sp => sp.GetRequiredService<BackgroundTaskQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundTaskQueue>());

        return services;
    }
}
