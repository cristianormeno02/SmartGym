using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public static class DefaultNotificationTemplates
{
    public static NotificationTemplate GetFallback(NotificationType type, NotificationChannel channel, string locale = "es-AR")
    {
        var (code, subject, inAppBody, emailHtmlBody, allowedVariables) = GetTemplateDefinition(type);

        return new NotificationTemplate
        {
            Id = Guid.Empty,
            Code = code,
            Type = type,
            Channel = channel,
            Locale = string.IsNullOrWhiteSpace(locale) ? "es-AR" : locale.Trim(),
            Version = 1,
            Status = NotificationTemplateStatus.Active,
            SubjectTemplate = channel == NotificationChannel.Email ? $"SmartGym - {subject}" : subject,
            BodyTemplate = channel == NotificationChannel.Email ? emailHtmlBody : inAppBody,
            AllowedVariables = allowedVariables,
            CreatedAtUtc = DateTime.UnixEpoch,
            IsActive = true
        };
    }

    public static (string Code, string Subject, string InAppBody, string EmailHtmlBody, string AllowedVariables) GetTemplateDefinition(NotificationType type) =>
        type switch
        {
            NotificationType.ClassSuspended => (
                "CLASS_SUSPENDED",
                "Clase suspendida: {{activityName}}",
                "La sesión de {{activityName}} del {{sessionDate}} fue suspendida. {{reason}}",
                "<p>Hola {{recipientName}},</p><p>La sesión de <strong>{{activityName}}</strong> del {{sessionDate}} fue suspendida. {{reason}}</p>",
                "recipientName,studentName,activityName,sessionDate,roomName,reason"),

            NotificationType.ClassReminder => (
                "CLASS_REMINDER",
                "Recordatorio de clase: {{activityName}}",
                "Tenés una reserva confirmada para {{activityName}} el {{sessionDate}} en {{roomName}}.",
                "<p>Hola {{recipientName}},</p><p>Te recordamos tu reserva para <strong>{{activityName}}</strong> el {{sessionDate}} en {{roomName}}.</p>",
                "recipientName,studentName,activityName,sessionDate,roomName"),

            NotificationType.MembershipExpiring => (
                "MEMBERSHIP_EXPIRING",
                "Tu membresía vence pronto",
                "Tu membresía {{planName}} vence el {{endDate}}.",
                "<p>Hola {{recipientName}},</p><p>Tu membresía <strong>{{planName}}</strong> vence el {{endDate}}.</p>",
                "recipientName,studentName,planName,endDate"),

            NotificationType.PromotionAvailable => (
                "PROMOTIONAL_ALERT",
                "Promoción disponible: {{promotionName}}",
                "{{promotionDescription}}",
                "<p>Hola {{recipientName}},</p><p>{{promotionDescription}}</p>",
                "recipientName,promotionName,promotionDescription,validTo"),

            NotificationType.PaymentConfirmed => (
                "PAYMENT_CONFIRMED",
                "Pago confirmado",
                "Se registró tu pago por {{amount}} {{currency}}.",
                "<p>Hola {{recipientName}},</p><p>Se confirmó tu pago por <strong>{{amount}} {{currency}}</strong>.</p>",
                "recipientName,studentName,amount,currency,paymentMethod"),

            NotificationType.MembershipActivated => (
                "MEMBERSHIP_ACTIVATED",
                "Membresía activada",
                "Tu membresía {{planName}} fue activada con vigencia del {{startDate}} al {{endDate}}.",
                "<p>Hola {{recipientName}},</p><p>Tu membresía <strong>{{planName}}</strong> fue activada con vigencia del {{startDate}} al {{endDate}}.</p>",
                "recipientName,studentName,planName,startDate,endDate"),

            NotificationType.MembershipSuspended => (
                "MEMBERSHIP_SUSPENDED",
                "Membresía suspendida",
                "Tu membresía {{planName}} fue suspendida desde el {{startDate}} hasta el {{endDate}}. Motivo: {{reason}}.",
                "<p>Hola {{recipientName}},</p><p>Tu membresía <strong>{{planName}}</strong> fue suspendida desde el {{startDate}} hasta el {{endDate}}. Motivo: {{reason}}.</p>",
                "recipientName,studentName,planName,startDate,endDate,reason"),

            NotificationType.MembershipExpired => (
                "MEMBERSHIP_EXPIRED",
                "Membresía vencida",
                "Tu membresía {{planName}} venció el {{endDate}}.",
                "<p>Hola {{recipientName}},</p><p>Tu membresía <strong>{{planName}}</strong> venció el {{endDate}}.</p>",
                "recipientName,studentName,planName,endDate"),

            NotificationType.ReservationConfirmed => (
                "RESERVATION_CONFIRMED",
                "Reserva confirmada: {{activityName}}",
                "Tu reserva para {{activityName}} el {{sessionDate}} quedó confirmada.",
                "<p>Hola {{recipientName}},</p><p>Tu reserva para <strong>{{activityName}}</strong> el {{sessionDate}} quedó confirmada.</p>",
                "recipientName,studentName,activityName,sessionDate,roomName"),

            NotificationType.ReservationCancelled => (
                "RESERVATION_CANCELLED",
                "Reserva cancelada: {{activityName}}",
                "La reserva para {{activityName}} ({{sessionDate}}) fue cancelada. {{creditOutcome}}",
                "<p>Hola {{recipientName}},</p><p>La reserva para <strong>{{activityName}}</strong> ({{sessionDate}}) fue cancelada. {{creditOutcome}}</p>",
                "recipientName,studentName,activityName,sessionDate,cancellationReason,creditOutcome"),

            NotificationType.ReservationWaitlisted => (
                "RESERVATION_WAITLISTED",
                "En lista de espera: {{activityName}}",
                "Quedaste en la posición #{{waitlistPosition}} de la lista de espera para {{activityName}} ({{sessionDate}}).",
                "<p>Hola {{recipientName}},</p><p>Quedaste en la posición #{{waitlistPosition}} de la lista de espera para <strong>{{activityName}}</strong> ({{sessionDate}}).</p>",
                "recipientName,studentName,activityName,sessionDate,waitlistPosition"),

            NotificationType.ReservationPromotedFromWaitlist => (
                "WAITLIST_PROMOTED",
                "¡Tenés lugar en {{activityName}}!",
                "Se liberó un cupo y tu reserva para {{activityName}} ({{sessionDate}}) quedó confirmada.",
                "<p>Hola {{recipientName}},</p><p>Se liberó un cupo y tu reserva para <strong>{{activityName}}</strong> ({{sessionDate}}) quedó confirmada.</p>",
                "recipientName,studentName,activityName,sessionDate,roomName"),

            NotificationType.WaitlistSpotOffered => (
                "WAITLIST_OFFER_AVAILABLE",
                "Cupo disponible en {{activityName}}",
                "Tenés una oferta de cupo para {{activityName}} ({{sessionDate}}) disponible hasta {{offerExpiresAt}}.",
                "<p>Hola {{recipientName}},</p><p>Tenés una oferta de cupo para <strong>{{activityName}}</strong> ({{sessionDate}}) disponible hasta {{offerExpiresAt}}.</p>",
                "recipientName,studentName,activityName,sessionDate,offerExpiresAt"),

            NotificationType.WaitlistOfferExpired => (
                "WAITLIST_OFFER_EXPIRED",
                "Oferta de lista de espera vencida",
                "Venció el plazo para confirmar el cupo en {{activityName}} ({{sessionDate}}).",
                "<p>Hola {{recipientName}},</p><p>Venció el plazo para confirmar el cupo en <strong>{{activityName}}</strong> ({{sessionDate}}).</p>",
                "recipientName,studentName,activityName,sessionDate"),

            NotificationType.ClassCancelled => (
                "CLASS_CANCELLED",
                "Clase cancelada: {{activityName}}",
                "La clase de {{activityName}} del {{sessionDate}} fue cancelada. {{reason}}",
                "<p>Hola {{recipientName}},</p><p>La clase de <strong>{{activityName}}</strong> del {{sessionDate}} fue cancelada. {{reason}}</p>",
                "recipientName,studentName,activityName,sessionDate,roomName,reason"),

            NotificationType.ClassRescheduled => (
                "CLASS_RESCHEDULED",
                "Cambio de horario: {{activityName}}",
                "La clase de {{activityName}} fue reprogramada para el {{sessionDate}}.",
                "<p>Hola {{recipientName}},</p><p>La clase de <strong>{{activityName}}</strong> fue reprogramada para el {{sessionDate}}.</p>",
                "recipientName,studentName,activityName,sessionDate,roomName,reason"),

            NotificationType.ClassRoomChanged => (
                "CLASS_ROOM_CHANGED",
                "Cambio de sala: {{activityName}}",
                "La clase de {{activityName}} del {{sessionDate}} se dictará en {{roomName}}.",
                "<p>Hola {{recipientName}},</p><p>La clase de <strong>{{activityName}}</strong> del {{sessionDate}} se dictará en {{roomName}}.</p>",
                "recipientName,studentName,activityName,sessionDate,roomName,reason"),

            NotificationType.ClassInstructorChanged => (
                "CLASS_INSTRUCTOR_CHANGED",
                "Cambio de instructor: {{activityName}}",
                "La clase de {{activityName}} del {{sessionDate}} estará a cargo de {{instructorName}}.",
                "<p>Hola {{recipientName}},</p><p>La clase de <strong>{{activityName}}</strong> del {{sessionDate}} estará a cargo de {{instructorName}}.</p>",
                "recipientName,studentName,activityName,sessionDate,instructorName"),

            NotificationType.MembershipReactivated => (
                "MEMBERSHIP_REACTIVATED",
                "Membresía reactivada",
                "Tu membresía {{planName}} fue reactivada. Nueva fecha de fin: {{endDate}}.",
                "<p>Hola {{recipientName}},</p><p>Tu membresía <strong>{{planName}}</strong> fue reactivada. Nueva fecha de fin: {{endDate}}.</p>",
                "recipientName,studentName,planName,endDate"),

            NotificationType.CreditsCompensated => (
                "CREDITS_ADJUSTED",
                "Actualización de créditos",
                "Se actualizaron los créditos de tu membresía {{planName}} ({{creditsDelta}}). Saldo disponible: {{availableCredits}}.",
                "<p>Hola {{recipientName}},</p><p>Se actualizaron los créditos de tu membresía <strong>{{planName}}</strong> ({{creditsDelta}}). Saldo disponible: {{availableCredits}}.</p>",
                "recipientName,studentName,planName,creditsDelta,availableCredits,reason"),

            NotificationType.PaymentFailed => (
                "PAYMENT_REJECTED",
                "Pago rechazado",
                "Tu pago por {{amount}} {{currency}} fue rechazado. Motivo: {{reason}}.",
                "<p>Hola {{recipientName}},</p><p>Tu pago por <strong>{{amount}} {{currency}}</strong> fue rechazado. Motivo: {{reason}}.</p>",
                "recipientName,studentName,amount,currency,reason"),

            NotificationType.PaymentRefunded => (
                "REFUND_CONFIRMED",
                "Reembolso confirmado",
                "Se procesó un reembolso por {{amount}} {{currency}}.",
                "<p>Hola {{recipientName}},</p><p>Se procesó un reembolso por <strong>{{amount}} {{currency}}</strong>.</p>",
                "recipientName,studentName,amount,currency,reason"),

            NotificationType.AdministrativeNotice => (
                "OPERATIONAL_NOTICE",
                "{{subject}}",
                "{{message}}",
                "<p>Hola {{recipientName}},</p><p>{{message}}</p>",
                "recipientName,subject,message"),

            NotificationType.SecurityAlert => (
                "SECURITY_ALERT",
                "Alerta de seguridad: {{subject}}",
                "{{message}}",
                "<p>Hola {{recipientName}},</p><p>{{message}}</p>",
                "recipientName,subject,message"),

            _ => (
                "GENERAL_NOTICE",
                "{{subject}}",
                "{{message}}",
                "<p>Hola {{recipientName}},</p><p>{{message}}</p>",
                "recipientName,subject,message")
        };
}
