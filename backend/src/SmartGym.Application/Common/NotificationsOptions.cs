namespace SmartGym.Application.Common;

public class NotificationsOptions
{
    public const string SectionName = "Notifications";

    public int DeliveryBatchSize { get; set; } = 50;
    public int DeliveryPollIntervalSeconds { get; set; } = 15;
    public int StaleProcessingTimeoutMinutes { get; set; } = 5;
    public int MaxDeliveryAttempts { get; set; } = 3;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public int MaxRetryDelaySeconds { get; set; } = 3600;
    public int MaxJitterSeconds { get; set; } = 15;
    public int ClassReminderLeadHours { get; set; } = 24;
    public int ClassReminderPollIntervalMinutes { get; set; } = 15;
}

public class EmailNotificationOptions
{
    public const string SectionName = "Notifications:Email";

    public bool Enabled { get; set; } = false;
    public string ProviderName { get; set; } = "Smtp";
    public string FromAddress { get; set; } = "no-reply@smartgym.local";
    public string FromName { get; set; } = "SmartGym";
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public bool UseSsl { get; set; } = true;
}
