using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;

namespace SmartGym.Domain.UnitTests.Operations;

public class NotificationTemplateRendererTests
{
    private static SmartGymDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SmartGymDbContext(options);
    }

    [Fact]
    public async Task RenderAsync_ReplacesValidPlaceholders_InAppChannel()
    {
        await using var db = CreateDbContext();
        db.NotificationTemplates.Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Code = "RESERVATION_CONFIRMED",
            Type = NotificationType.ReservationConfirmed,
            Channel = NotificationChannel.InApp,
            Locale = "es-AR",
            Version = 2,
            Status = NotificationTemplateStatus.Active,
            SubjectTemplate = "Reserva confirmada: {{activityName}}",
            BodyTemplate = "Hola {{recipientName}}, tu reserva para {{activityName}} el {{sessionDate}} en {{roomName}} quedó confirmada.",
            AllowedVariables = "recipientName,activityName,sessionDate,roomName",
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var renderer = new NotificationTemplateRenderer(db);
        var rendered = await renderer.RenderAsync(
            NotificationType.ReservationConfirmed,
            NotificationChannel.InApp,
            new Dictionary<string, string?>
            {
                ["recipientName"] = "Clara",
                ["activityName"] = "Yoga Flow",
                ["sessionDate"] = "15/10 18:00",
                ["roomName"] = "Sala 1"
            });

        Assert.Equal("Reserva confirmada: Yoga Flow", rendered.Subject);
        Assert.Equal("Hola Clara, tu reserva para Yoga Flow el 15/10 18:00 en Sala 1 quedó confirmada.", rendered.Body);
        Assert.Equal("RESERVATION_CONFIRMED", rendered.TemplateCode);
        Assert.Equal(2, rendered.TemplateVersion);
    }

    [Fact]
    public async Task RenderAsync_EmailChannel_HtmlEncodesInjectedScriptAndAttributes()
    {
        await using var db = CreateDbContext();
        db.NotificationTemplates.Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Code = "CLASS_SUSPENDED",
            Type = NotificationType.ClassSuspended,
            Channel = NotificationChannel.Email,
            Locale = "es-AR",
            Version = 1,
            Status = NotificationTemplateStatus.Active,
            SubjectTemplate = "Clase suspendida: {{activityName}}",
            BodyTemplate = "<p>Hola {{recipientName}}, motivo: {{reason}}</p>",
            AllowedVariables = "recipientName,activityName,reason",
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var renderer = new NotificationTemplateRenderer(db);
        var rendered = await renderer.RenderAsync(
            NotificationType.ClassSuspended,
            NotificationChannel.Email,
            new Dictionary<string, string?>
            {
                ["recipientName"] = "Ana <img src=x onerror=alert(1)>",
                ["activityName"] = "Boxeo",
                ["reason"] = "<script>alert('xss')</script>"
            });

        Assert.DoesNotContain("<script>", rendered.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", rendered.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;", rendered.Body);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", rendered.Body);
        Assert.NotNull(rendered.PlainTextBody);
        Assert.DoesNotContain("<p>", rendered.PlainTextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenderAsync_WhenDatabaseTemplateIsInactive_FallsBackToDefaultNotificationTemplates()
    {
        await using var db = CreateDbContext();
        db.NotificationTemplates.Add(new NotificationTemplate
        {
            Id = Guid.NewGuid(),
            Code = "PAYMENT_CONFIRMED",
            Type = NotificationType.PaymentConfirmed,
            Channel = NotificationChannel.InApp,
            Locale = "es-AR",
            Version = 3,
            Status = NotificationTemplateStatus.Inactive,
            SubjectTemplate = "Plantilla inactiva",
            BodyTemplate = "No debe usarse",
            AllowedVariables = "recipientName,amount,currency",
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = false
        });
        await db.SaveChangesAsync();

        var renderer = new NotificationTemplateRenderer(db);
        var rendered = await renderer.RenderAsync(
            NotificationType.PaymentConfirmed,
            NotificationChannel.InApp,
            new Dictionary<string, string?>
            {
                ["recipientName"] = "Lucas",
                ["amount"] = "15000",
                ["currency"] = "ARS"
            });

        Assert.Equal("PAYMENT_CONFIRMED", rendered.TemplateCode);
        Assert.Equal(1, rendered.TemplateVersion);
        Assert.Contains("15000", rendered.Body);
        Assert.Contains("ARS", rendered.Body);
    }

    [Fact]
    public void ValidateTemplateDefinition_RejectsUndeclaredVariables_WithTemplateInvalidVariablesCode()
    {
        using var db = CreateDbContext();
        var renderer = new NotificationTemplateRenderer(db);

        var ex = Assert.Throws<ValidationException>(() =>
            renderer.ValidateTemplateDefinition(
                subjectTemplate: "Hola {{recipientName}}",
                bodyTemplate: "Tu saldo es {{secretInternalField}}",
                plainTextTemplate: null,
                allowedVariables: ["recipientName", "activityName"],
                channel: NotificationChannel.InApp));

        Assert.True(ex.Errors.ContainsKey(NotificationErrorCodes.TemplateInvalidVariables));
    }

    [Fact]
    public void ValidateTemplateDefinition_RejectsUnsafeScriptOrEventHandlers_WithTemplateUnsafeContentCode()
    {
        using var db = CreateDbContext();
        var renderer = new NotificationTemplateRenderer(db);

        var ex1 = Assert.Throws<ValidationException>(() =>
            renderer.ValidateTemplateDefinition(
                subjectTemplate: "Aviso",
                bodyTemplate: "<p>Hola {{recipientName}}</p><script>steal()</script>",
                plainTextTemplate: null,
                allowedVariables: ["recipientName"],
                channel: NotificationChannel.Email));

        Assert.True(ex1.Errors.ContainsKey(NotificationErrorCodes.TemplateUnsafeContent));

        var ex2 = Assert.Throws<ValidationException>(() =>
            renderer.ValidateTemplateDefinition(
                subjectTemplate: "Aviso",
                bodyTemplate: "<a href=\"javascript:alert(1)\" onclick=\"evil()\">Click</a>",
                plainTextTemplate: null,
                allowedVariables: ["recipientName"],
                channel: NotificationChannel.Email));

        Assert.True(ex2.Errors.ContainsKey(NotificationErrorCodes.TemplateUnsafeContent));
    }
}
