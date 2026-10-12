using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartGym.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenNotificationsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationMessages_People_RecipientPersonId",
                table: "NotificationMessages");

            migrationBuilder.AlterColumn<string>(
                name: "Body",
                table: "NotificationMessages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<string>(
                name: "ActionUrl",
                table: "NotificationMessages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "NotificationMessages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "NotificationMessages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "NotificationMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "NotificationMessages",
                type: "character varying(180)",
                maxLength: 180,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "NotificationMessages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAtUtc",
                table: "NotificationMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedEntityId",
                table: "NotificationMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelatedEntityType",
                table: "NotificationMessages",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.Sql(@"
-- 1. Backfill Category from NotificationType
UPDATE ""NotificationMessages""
SET ""Category"" = CASE
    WHEN ""Type"" IN (1, 15, 16, 17, 18) THEN 2
    WHEN ""Type"" IN (2, 9, 10, 11, 12, 13, 14) THEN 1
    WHEN ""Type"" IN (3, 6, 7, 8, 19, 20) THEN 3
    WHEN ""Type"" IN (5, 21, 22, 23) THEN 4
    WHEN ""Type"" = 4 THEN 5
    WHEN ""Type"" = 24 THEN 7
    ELSE 1
END;

-- 2. Backfill ReadAtUtc for already read notifications
UPDATE ""NotificationMessages""
SET ""ReadAtUtc"" = COALESCE(""UpdatedAtUtc"", ""SentAtUtc"")
WHERE ""IsRead"" = TRUE AND ""ReadAtUtc"" IS NULL;
");

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    RecipientAddress = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RetriedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RetryReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.Id);
                    table.CheckConstraint("CK_NotificationDeliveries_Attempts", "\"AttemptCount\" >= 0 AND \"MaxAttempts\" >= 1");
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_NotificationMessages_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "NotificationMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "es-AR"),
                    SubjectTemplate = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BodyTemplate = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AllowedVariables = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationMessages_RecipientPersonId_Category_SentAtUtc",
                table: "NotificationMessages",
                columns: new[] { "RecipientPersonId", "Category", "SentAtUtc" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationMessages_RecipientPersonId_SentAtUtc",
                table: "NotificationMessages",
                columns: new[] { "RecipientPersonId", "SentAtUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_NotificationMessages_Recipient_IdempotencyKey",
                table: "NotificationMessages",
                columns: new[] { "RecipientPersonId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_NextAttemptAtUtc",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_NotificationDeliveries_Notification_Channel",
                table: "NotificationDeliveries",
                columns: new[] { "NotificationId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_NotificationPreferences_Person_Category_Channel",
                table: "NotificationPreferences",
                columns: new[] { "PersonId", "Category", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_NotificationTemplates_Active_Type_Channel_Locale",
                table: "NotificationTemplates",
                columns: new[] { "Type", "Channel", "Locale" },
                unique: true,
                filter: "\"Status\" = 2 AND \"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "UX_NotificationTemplates_Code_Channel_Locale_Version",
                table: "NotificationTemplates",
                columns: new[] { "Code", "Channel", "Locale", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationMessages_People_RecipientPersonId",
                table: "NotificationMessages",
                column: "RecipientPersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
INSERT INTO ""NotificationTemplates"" (""Id"", ""Code"", ""Type"", ""Channel"", ""Locale"", ""Version"", ""Status"", ""SubjectTemplate"", ""BodyTemplate"", ""AllowedVariables"", ""CreatedAtUtc"", ""IsActive"")
VALUES
    -- InApp (Channel = 1)
    ('00000000-0000-0000-0001-000000000001', 'CLASS_SUSPENDED', 1, 1, 'es-AR', 1, 2, 'Clase suspendida: {{activityName}}', 'La sesión de {{activityName}} del {{sessionDate}} fue suspendida. {{reason}}', 'recipientName,studentName,activityName,sessionDate,roomName,reason', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000002', 'CLASS_REMINDER', 2, 1, 'es-AR', 1, 2, 'Recordatorio de clase: {{activityName}}', 'Tenés una reserva confirmada para {{activityName}} el {{sessionDate}} en {{roomName}}.', 'recipientName,studentName,activityName,sessionDate,roomName', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000003', 'MEMBERSHIP_EXPIRING', 3, 1, 'es-AR', 1, 2, 'Tu membresía vence pronto', 'Tu membresía {{planName}} vence el {{endDate}}.', 'recipientName,studentName,planName,endDate', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000004', 'PROMOTIONAL_ALERT', 4, 1, 'es-AR', 1, 2, 'Promoción disponible: {{promotionName}}', '{{promotionDescription}}', 'recipientName,promotionName,promotionDescription,validTo', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000005', 'PAYMENT_CONFIRMED', 5, 1, 'es-AR', 1, 2, 'Pago confirmado', 'Se registró tu pago por {{amount}} {{currency}}.', 'recipientName,studentName,amount,currency,paymentMethod', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000006', 'MEMBERSHIP_ACTIVATED', 6, 1, 'es-AR', 1, 2, 'Membresía activada', 'Tu membresía {{planName}} fue activada con vigencia del {{startDate}} al {{endDate}}.', 'recipientName,studentName,planName,startDate,endDate', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000007', 'MEMBERSHIP_SUSPENDED', 7, 1, 'es-AR', 1, 2, 'Membresía suspendida', 'Tu membresía {{planName}} fue suspendida desde el {{startDate}} hasta el {{endDate}}. Motivo: {{reason}}.', 'recipientName,studentName,planName,startDate,endDate,reason', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000008', 'MEMBERSHIP_EXPIRED', 8, 1, 'es-AR', 1, 2, 'Membresía vencida', 'Tu membresía {{planName}} venció el {{endDate}}.', 'recipientName,studentName,planName,endDate', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000009', 'RESERVATION_CONFIRMED', 9, 1, 'es-AR', 1, 2, 'Reserva confirmada: {{activityName}}', 'Tu reserva para {{activityName}} el {{sessionDate}} quedó confirmada.', 'recipientName,studentName,activityName,sessionDate,roomName', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000010', 'RESERVATION_CANCELLED', 10, 1, 'es-AR', 1, 2, 'Reserva cancelada: {{activityName}}', 'La reserva para {{activityName}} ({{sessionDate}}) fue cancelada. {{creditOutcome}}', 'recipientName,studentName,activityName,sessionDate,cancellationReason,creditOutcome', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000011', 'RESERVATION_WAITLISTED', 11, 1, 'es-AR', 1, 2, 'En lista de espera: {{activityName}}', 'Quedaste en la posición #{{waitlistPosition}} de la lista de espera para {{activityName}} ({{sessionDate}}).', 'recipientName,studentName,activityName,sessionDate,waitlistPosition', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000012', 'WAITLIST_PROMOTED', 12, 1, 'es-AR', 1, 2, '¡Tenés lugar en {{activityName}}!', 'Se liberó un cupo y tu reserva para {{activityName}} ({{sessionDate}}) quedó confirmada.', 'recipientName,studentName,activityName,sessionDate,roomName', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000013', 'WAITLIST_OFFER_AVAILABLE', 13, 1, 'es-AR', 1, 2, 'Cupo disponible en {{activityName}}', 'Tenés una oferta de cupo para {{activityName}} ({{sessionDate}}) disponible hasta {{offerExpiresAt}}.', 'recipientName,studentName,activityName,sessionDate,offerExpiresAt', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000014', 'WAITLIST_OFFER_EXPIRED', 14, 1, 'es-AR', 1, 2, 'Oferta de lista de espera vencida', 'Venció el plazo para confirmar el cupo en {{activityName}} ({{sessionDate}}).', 'recipientName,studentName,activityName,sessionDate', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000015', 'CLASS_CANCELLED', 15, 1, 'es-AR', 1, 2, 'Clase cancelada: {{activityName}}', 'La clase de {{activityName}} del {{sessionDate}} fue cancelada. {{reason}}', 'recipientName,studentName,activityName,sessionDate,roomName,reason', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000016', 'CLASS_RESCHEDULED', 16, 1, 'es-AR', 1, 2, 'Cambio de horario: {{activityName}}', 'La clase de {{activityName}} fue reprogramada para el {{sessionDate}}.', 'recipientName,studentName,activityName,sessionDate,roomName,reason', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000017', 'CLASS_ROOM_CHANGED', 17, 1, 'es-AR', 1, 2, 'Cambio de sala: {{activityName}}', 'La clase de {{activityName}} del {{sessionDate}} se dictará en {{roomName}}.', 'recipientName,studentName,activityName,sessionDate,roomName,reason', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000018', 'CLASS_INSTRUCTOR_CHANGED', 18, 1, 'es-AR', 1, 2, 'Cambio de instructor: {{activityName}}', 'La clase de {{activityName}} del {{sessionDate}} estará a cargo de {{instructorName}}.', 'recipientName,studentName,activityName,sessionDate,instructorName', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000019', 'MEMBERSHIP_REACTIVATED', 19, 1, 'es-AR', 1, 2, 'Membresía reactivada', 'Tu membresía {{planName}} fue reactivada. Nueva fecha de fin: {{endDate}}.', 'recipientName,studentName,planName,endDate', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000020', 'CREDITS_ADJUSTED', 20, 1, 'es-AR', 1, 2, 'Actualización de créditos', 'Se actualizaron los créditos de tu membresía {{planName}} ({{creditsDelta}}). Saldo disponible: {{availableCredits}}.', 'recipientName,studentName,planName,creditsDelta,availableCredits,reason', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000021', 'PAYMENT_REJECTED', 21, 1, 'es-AR', 1, 2, 'Pago rechazado', 'Tu pago por {{amount}} {{currency}} fue rechazado. Motivo: {{reason}}.', 'recipientName,studentName,amount,currency,reason', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000022', 'REFUND_CONFIRMED', 22, 1, 'es-AR', 1, 2, 'Reembolso confirmado', 'Se procesó un reembolso por {{amount}} {{currency}}.', 'recipientName,studentName,amount,currency,reason', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000023', 'OPERATIONAL_NOTICE', 23, 1, 'es-AR', 1, 2, '{{subject}}', '{{message}}', 'recipientName,subject,message', NOW(), TRUE),
    ('00000000-0000-0000-0001-000000000024', 'SECURITY_ALERT', 24, 1, 'es-AR', 1, 2, 'Alerta de seguridad: {{subject}}', '{{message}}', 'recipientName,subject,message', NOW(), TRUE),

    -- Email (Channel = 2)
    ('00000000-0000-0000-0002-000000000001', 'CLASS_SUSPENDED', 1, 2, 'es-AR', 1, 2, 'SmartGym - Clase suspendida: {{activityName}}', '<p>Hola {{recipientName}},</p><p>La sesión de <strong>{{activityName}}</strong> del {{sessionDate}} fue suspendida. {{reason}}</p>', 'recipientName,studentName,activityName,sessionDate,roomName,reason', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000002', 'CLASS_REMINDER', 2, 2, 'es-AR', 1, 2, 'SmartGym - Recordatorio de clase: {{activityName}}', '<p>Hola {{recipientName}},</p><p>Te recordamos tu reserva para <strong>{{activityName}}</strong> el {{sessionDate}} en {{roomName}}.</p>', 'recipientName,studentName,activityName,sessionDate,roomName', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000003', 'MEMBERSHIP_EXPIRING', 3, 2, 'es-AR', 1, 2, 'SmartGym - Tu membresía vence pronto', '<p>Hola {{recipientName}},</p><p>Tu membresía <strong>{{planName}}</strong> vence el {{endDate}}.</p>', 'recipientName,studentName,planName,endDate', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000004', 'PROMOTIONAL_ALERT', 4, 2, 'es-AR', 1, 2, 'SmartGym - Promoción disponible: {{promotionName}}', '<p>Hola {{recipientName}},</p><p>{{promotionDescription}}</p>', 'recipientName,promotionName,promotionDescription,validTo', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000005', 'PAYMENT_CONFIRMED', 5, 2, 'es-AR', 1, 2, 'SmartGym - Pago confirmado', '<p>Hola {{recipientName}},</p><p>Se confirmó tu pago por <strong>{{amount}} {{currency}}</strong>.</p>', 'recipientName,studentName,amount,currency,paymentMethod', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000006', 'MEMBERSHIP_ACTIVATED', 6, 2, 'es-AR', 1, 2, 'SmartGym - Membresía activada', '<p>Hola {{recipientName}},</p><p>Tu membresía <strong>{{planName}}</strong> fue activada con vigencia del {{startDate}} al {{endDate}}.</p>', 'recipientName,studentName,planName,startDate,endDate', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000007', 'MEMBERSHIP_SUSPENDED', 7, 2, 'es-AR', 1, 2, 'SmartGym - Membresía suspendida', '<p>Hola {{recipientName}},</p><p>Tu membresía <strong>{{planName}}</strong> fue suspendida desde el {{startDate}} hasta el {{endDate}}. Motivo: {{reason}}.</p>', 'recipientName,studentName,planName,startDate,endDate,reason', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000008', 'MEMBERSHIP_EXPIRED', 8, 2, 'es-AR', 1, 2, 'SmartGym - Membresía vencida', '<p>Hola {{recipientName}},</p><p>Tu membresía <strong>{{planName}}</strong> venció el {{endDate}}.</p>', 'recipientName,studentName,planName,endDate', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000009', 'RESERVATION_CONFIRMED', 9, 2, 'es-AR', 1, 2, 'SmartGym - Reserva confirmada: {{activityName}}', '<p>Hola {{recipientName}},</p><p>Tu reserva para <strong>{{activityName}}</strong> el {{sessionDate}} quedó confirmada.</p>', 'recipientName,studentName,activityName,sessionDate,roomName', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000010', 'RESERVATION_CANCELLED', 10, 2, 'es-AR', 1, 2, 'SmartGym - Reserva cancelada: {{activityName}}', '<p>Hola {{recipientName}},</p><p>La reserva para <strong>{{activityName}}</strong> ({{sessionDate}}) fue cancelada. {{creditOutcome}}</p>', 'recipientName,studentName,activityName,sessionDate,cancellationReason,creditOutcome', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000011', 'RESERVATION_WAITLISTED', 11, 2, 'es-AR', 1, 2, 'SmartGym - Lista de espera: {{activityName}}', '<p>Hola {{recipientName}},</p><p>Quedaste en la posición #{{waitlistPosition}} de la lista de espera para <strong>{{activityName}}</strong> ({{sessionDate}}).</p>', 'recipientName,studentName,activityName,sessionDate,waitlistPosition', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000012', 'WAITLIST_PROMOTED', 12, 2, 'es-AR', 1, 2, 'SmartGym - ¡Tenés lugar en {{activityName}}!', '<p>Hola {{recipientName}},</p><p>Se liberó un cupo y tu reserva para <strong>{{activityName}}</strong> ({{sessionDate}}) quedó confirmada.</p>', 'recipientName,studentName,activityName,sessionDate,roomName', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000013', 'WAITLIST_OFFER_AVAILABLE', 13, 2, 'es-AR', 1, 2, 'SmartGym - Cupo disponible en {{activityName}}', '<p>Hola {{recipientName}},</p><p>Tenés una oferta de cupo para <strong>{{activityName}}</strong> ({{sessionDate}}) disponible hasta {{offerExpiresAt}}.</p>', 'recipientName,studentName,activityName,sessionDate,offerExpiresAt', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000014', 'WAITLIST_OFFER_EXPIRED', 14, 2, 'es-AR', 1, 2, 'SmartGym - Oferta de lista de espera vencida', '<p>Hola {{recipientName}},</p><p>Venció el plazo para confirmar el cupo en <strong>{{activityName}}</strong> ({{sessionDate}}).</p>', 'recipientName,studentName,activityName,sessionDate', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000015', 'CLASS_CANCELLED', 15, 2, 'es-AR', 1, 2, 'SmartGym - Clase cancelada: {{activityName}}', '<p>Hola {{recipientName}},</p><p>La clase de <strong>{{activityName}}</strong> del {{sessionDate}} fue cancelada. {{reason}}</p>', 'recipientName,studentName,activityName,sessionDate,roomName,reason', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000016', 'CLASS_RESCHEDULED', 16, 2, 'es-AR', 1, 2, 'SmartGym - Cambio de horario: {{activityName}}', '<p>Hola {{recipientName}},</p><p>La clase de <strong>{{activityName}}</strong> fue reprogramada para el {{sessionDate}}.</p>', 'recipientName,studentName,activityName,sessionDate,roomName,reason', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000017', 'CLASS_ROOM_CHANGED', 17, 2, 'es-AR', 1, 2, 'SmartGym - Cambio de sala: {{activityName}}', '<p>Hola {{recipientName}},</p><p>La clase de <strong>{{activityName}}</strong> del {{sessionDate}} se dictará en {{roomName}}.</p>', 'recipientName,studentName,activityName,sessionDate,roomName,reason', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000018', 'CLASS_INSTRUCTOR_CHANGED', 18, 2, 'es-AR', 1, 2, 'SmartGym - Cambio de instructor: {{activityName}}', '<p>Hola {{recipientName}},</p><p>La clase de <strong>{{activityName}}</strong> del {{sessionDate}} estará a cargo de {{instructorName}}.</p>', 'recipientName,studentName,activityName,sessionDate,instructorName', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000019', 'MEMBERSHIP_REACTIVATED', 19, 2, 'es-AR', 1, 2, 'SmartGym - Membresía reactivada', '<p>Hola {{recipientName}},</p><p>Tu membresía <strong>{{planName}}</strong> fue reactivada. Nueva fecha de fin: {{endDate}}.</p>', 'recipientName,studentName,planName,endDate', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000020', 'CREDITS_ADJUSTED', 20, 2, 'es-AR', 1, 2, 'SmartGym - Actualización de créditos', '<p>Hola {{recipientName}},</p><p>Se actualizaron los créditos de tu membresía <strong>{{planName}}</strong> ({{creditsDelta}}). Saldo disponible: {{availableCredits}}.</p>', 'recipientName,studentName,planName,creditsDelta,availableCredits,reason', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000021', 'PAYMENT_REJECTED', 21, 2, 'es-AR', 1, 2, 'SmartGym - Pago rechazado', '<p>Hola {{recipientName}},</p><p>Tu pago por <strong>{{amount}} {{currency}}</strong> fue rechazado. Motivo: {{reason}}.</p>', 'recipientName,studentName,amount,currency,reason', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000022', 'REFUND_CONFIRMED', 22, 2, 'es-AR', 1, 2, 'SmartGym - Reembolso confirmado', '<p>Hola {{recipientName}},</p><p>Se procesó un reembolso por <strong>{{amount}} {{currency}}</strong>.</p>', 'recipientName,studentName,amount,currency,reason', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000023', 'OPERATIONAL_NOTICE', 23, 2, 'es-AR', 1, 2, 'SmartGym - {{subject}}', '<p>Hola {{recipientName}},</p><p>{{message}}</p>', 'recipientName,subject,message', NOW(), TRUE),
    ('00000000-0000-0000-0002-000000000024', 'SECURITY_ALERT', 24, 2, 'es-AR', 1, 2, 'SmartGym - Alerta de seguridad: {{subject}}', '<p>Hola {{recipientName}},</p><p>{{message}}</p>', 'recipientName,subject,message', NOW(), TRUE)
ON CONFLICT (""Code"", ""Channel"", ""Locale"", ""Version"") DO NOTHING;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationMessages_People_RecipientPersonId",
                table: "NotificationMessages");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");

            migrationBuilder.DropIndex(
                name: "IX_NotificationMessages_RecipientPersonId_Category_SentAtUtc",
                table: "NotificationMessages");

            migrationBuilder.DropIndex(
                name: "IX_NotificationMessages_RecipientPersonId_SentAtUtc",
                table: "NotificationMessages");

            migrationBuilder.DropIndex(
                name: "UX_NotificationMessages_Recipient_IdempotencyKey",
                table: "NotificationMessages");

            migrationBuilder.DropColumn(
                name: "ActionUrl",
                table: "NotificationMessages");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "NotificationMessages");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "NotificationMessages");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "NotificationMessages");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "NotificationMessages");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "NotificationMessages");

            migrationBuilder.DropColumn(
                name: "ReadAtUtc",
                table: "NotificationMessages");

            migrationBuilder.DropColumn(
                name: "RelatedEntityId",
                table: "NotificationMessages");

            migrationBuilder.DropColumn(
                name: "RelatedEntityType",
                table: "NotificationMessages");

            migrationBuilder.AlterColumn<string>(
                name: "Body",
                table: "NotificationMessages",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationMessages_People_RecipientPersonId",
                table: "NotificationMessages",
                column: "RecipientPersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
