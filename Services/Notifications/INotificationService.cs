namespace Kue.Api.Services.Notifications;

public interface INotificationService
{
    Task CreateNotificationAsync(
        int userId,
        int actorId,
        string type,
        string title,
        string message,
        int? referenceId = null,
        string? referenceType = null,
        CancellationToken ct = default);
}
