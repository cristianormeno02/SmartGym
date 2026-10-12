namespace SmartGym.Application.Modules.Operations.Services;

public record ClassReminderBatchResult(
    int CandidatesEvaluated,
    int RemindersSent,
    int DeduplicatedCount
);

public interface IClassReminderService
{
    Task<ClassReminderBatchResult> ProcessUpcomingClassRemindersAsync(
        int? leadHours = null,
        CancellationToken cancellationToken = default);
}
