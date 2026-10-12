using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Infrastructure.Services.Notifications;

public class ConfigurableEmailNotificationProvider : IEmailNotificationProvider
{
    private readonly EmailNotificationOptions _options;
    private readonly ILogger<ConfigurableEmailNotificationProvider>? _logger;

    public ConfigurableEmailNotificationProvider(
        IOptions<EmailNotificationOptions>? options = null,
        ILogger<ConfigurableEmailNotificationProvider>? logger = null)
    {
        _options = options?.Value ?? new EmailNotificationOptions();
        _logger = logger;
    }

    public NotificationChannel Channel => NotificationChannel.Email;
    public string ProviderName => string.IsNullOrWhiteSpace(_options.ProviderName) ? "Smtp" : _options.ProviderName.Trim();
    public bool IsConfigured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.SmtpHost);

    public async Task<ChannelDeliveryResult> SendAsync(
        ChannelDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return ChannelDeliveryResult.PermanentFailure(
                ProviderName,
                NotificationErrorCodes.ProviderNotConfigured,
                "El proveedor de correo electrónico no se encuentra habilitado o configurado.");
        }

        if (string.IsNullOrWhiteSpace(request.Destination) ||
            !MailAddress.TryCreate(request.Destination.Trim(), out var toAddress))
        {
            return ChannelDeliveryResult.PermanentFailure(
                ProviderName,
                "INVALID_RECIPIENT_EMAIL",
                "La dirección de correo del destinatario no es válida.");
        }

        if (!MailAddress.TryCreate(_options.FromAddress, _options.FromName, out var fromAddress))
        {
            return ChannelDeliveryResult.PermanentFailure(
                ProviderName,
                NotificationErrorCodes.ProviderNotConfigured,
                "La dirección remitente configurada no es válida.");
        }

        try
        {
            using var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = request.Subject,
                SubjectEncoding = Encoding.UTF8,
                Body = request.Body,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = true
            };

            if (!string.IsNullOrWhiteSpace(request.PlainTextBody))
            {
                var plainView = AlternateView.CreateAlternateViewFromString(
                    request.PlainTextBody,
                    Encoding.UTF8,
                    MediaTypeNames.Text.Plain);
                message.AlternateViews.Add(plainView);
            }

            using var smtpClient = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
            {
                EnableSsl = _options.UseSsl
            };

            if (!string.IsNullOrWhiteSpace(_options.SmtpUsername))
            {
                smtpClient.Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword);
            }

            await smtpClient.SendMailAsync(message, cancellationToken);

            var providerMessageId = $"smtp:{request.DeliveryId:N}";
            return ChannelDeliveryResult.Success(ProviderName, providerMessageId);
        }
        catch (SmtpFailedRecipientException ex)
        {
            var isPermanent = ex.StatusCode is SmtpStatusCode.MailboxUnavailable
                or SmtpStatusCode.UserNotLocalWillForward
                or SmtpStatusCode.SyntaxError
                or SmtpStatusCode.MailboxNameNotAllowed;

            var sanitized = NotificationDelivery.SanitizeErrorMessage(ex.Message);
            return isPermanent
                ? ChannelDeliveryResult.PermanentFailure(ProviderName, $"SMTP_{(int)ex.StatusCode}", sanitized)
                : ChannelDeliveryResult.TransientFailure(ProviderName, $"SMTP_{(int)ex.StatusCode}", sanitized);
        }
        catch (SmtpException ex)
        {
            var sanitized = NotificationDelivery.SanitizeErrorMessage(ex.Message);
            _logger?.LogWarning("Fallo SMTP temporal para DeliveryId={DeliveryId}: {Error}", request.DeliveryId, sanitized);
            return ChannelDeliveryResult.TransientFailure(ProviderName, $"SMTP_{(int)ex.StatusCode}", sanitized);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var sanitized = NotificationDelivery.SanitizeErrorMessage(ex.Message);
            _logger?.LogWarning("Fallo de envío de email para DeliveryId={DeliveryId}: {Error}", request.DeliveryId, sanitized);
            return ChannelDeliveryResult.TransientFailure(ProviderName, "EMAIL_SEND_ERROR", sanitized);
        }
    }
}
