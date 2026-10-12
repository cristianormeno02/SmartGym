namespace SmartGym.Application.Modules.Operations;

public static class NotificationErrorCodes
{
    public const string NotificationNotFound = "NOTIFICATION_NOT_FOUND";
    public const string DeliveryNotFound = "DELIVERY_NOT_FOUND";
    public const string TemplateNotFound = "TEMPLATE_NOT_FOUND";
    public const string RecipientNotFound = "RECIPIENT_NOT_FOUND";
    public const string PreferenceMandatoryChannelLocked = "PREFERENCE_MANDATORY_CHANNEL_LOCKED";
    public const string DeliveryInvalidRetryState = "DELIVERY_INVALID_RETRY_STATE";
    public const string TemplateDuplicateVersion = "TEMPLATE_DUPLICATE_VERSION";
    public const string TemplateInvalidVariables = "TEMPLATE_INVALID_VARIABLES";
    public const string TemplateUnsafeContent = "TEMPLATE_UNSAFE_CONTENT";
    public const string InvalidActionUrl = "INVALID_ACTION_URL";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string ProviderNotConfigured = "PROVIDER_NOT_CONFIGURED";
}
