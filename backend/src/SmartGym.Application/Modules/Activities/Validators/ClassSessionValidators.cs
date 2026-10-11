using FluentValidation;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Validators;

public class CreateClassSessionRequestValidator : AbstractValidator<CreateClassSessionRequest>
{
    public CreateClassSessionRequestValidator()
    {
        RuleFor(x => x.ActivityId)
            .NotEmpty().WithMessage("La actividad es obligatoria.");

        RuleFor(x => x.RoomId)
            .NotEmpty().WithMessage("La sala es obligatoria.");

        RuleFor(x => x.InstructorId)
            .NotEmpty().WithMessage("El instructor es obligatorio.");

        RuleFor(x => x.StartsAt)
            .GreaterThan(DateTimeOffset.UtcNow)
            .WithMessage("La fecha y hora de inicio debe ser futura.");

        RuleFor(x => x.EndsAt)
            .GreaterThan(x => x.StartsAt)
            .WithMessage("La fecha y hora de finalización debe ser posterior a la de inicio.");

        RuleFor(x => x)
            .Must(x => x.EndsAt - x.StartsAt <= TimeSpan.FromHours(24))
            .WithMessage("La duración de la sesión no puede exceder las 24 horas.");

        RuleFor(x => x.Capacity)
            .GreaterThan(0)
            .WithMessage("El cupo debe ser un número entero positivo.")
            .When(x => x.Capacity.HasValue);

        RuleFor(x => x.Notes)
            .MaximumLength(2000)
            .WithMessage("Las notas no pueden superar los 2000 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Notes));
    }
}

public class UpdateClassSessionRequestValidator : AbstractValidator<UpdateClassSessionRequest>
{
    public UpdateClassSessionRequestValidator()
    {
        RuleFor(x => x.Capacity)
            .GreaterThan(0)
            .WithMessage("El cupo debe ser mayor a 0.");

        RuleFor(x => x.Notes)
            .MaximumLength(2000)
            .WithMessage("Las notas no pueden superar los 2000 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Notes));
    }
}

public class SuspendClassSessionRequestValidator : AbstractValidator<SuspendClassSessionRequest>
{
    public SuspendClassSessionRequestValidator()
    {
        RuleFor(x => x.Observations)
            .NotEmpty()
            .WithMessage("Las observaciones son obligatorias cuando el motivo de suspensión es 'Other'.")
            .When(x => x.Reason == ClassSessionSuspensionReason.Other);

        RuleFor(x => x.Observations)
            .MaximumLength(1000)
            .WithMessage("Las observaciones no pueden superar los 1000 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Observations));
    }
}

public class CancelClassSessionRequestValidator : AbstractValidator<CancelClassSessionRequest>
{
    public CancelClassSessionRequestValidator()
    {
        RuleFor(x => x.Reason)
            .Must(r => r != ClassSessionCancellationReason.ScheduleEnded)
            .WithMessage("El motivo 'ScheduleEnded' está reservado para el sistema y no puede seleccionarse manualmente.");

        RuleFor(x => x.Observations)
            .NotEmpty()
            .WithMessage("Las observaciones son obligatorias cuando el motivo de cancelación es 'Other'.")
            .When(x => x.Reason == ClassSessionCancellationReason.Other);

        RuleFor(x => x.Observations)
            .MaximumLength(1000)
            .WithMessage("Las observaciones no pueden superar los 1000 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Observations));
    }
}

public class RescheduleClassSessionRequestValidator : AbstractValidator<RescheduleClassSessionRequest>
{
    public RescheduleClassSessionRequestValidator()
    {
        RuleFor(x => x.StartsAt)
            .GreaterThan(DateTimeOffset.UtcNow)
            .WithMessage("La fecha y hora de inicio debe ser futura.");

        RuleFor(x => x.EndsAt)
            .GreaterThan(x => x.StartsAt)
            .WithMessage("La fecha y hora de finalización debe ser posterior a la de inicio.");

        RuleFor(x => x)
            .Must(x => x.EndsAt - x.StartsAt <= TimeSpan.FromHours(24))
            .WithMessage("La duración de la sesión no puede exceder las 24 horas.");

        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("El motivo de reprogramación es obligatorio.")
            .MaximumLength(500)
            .WithMessage("El motivo de reprogramación no puede superar los 500 caracteres.");
    }
}

public class ReplaceStaffRequestValidator : AbstractValidator<ReplaceStaffRequest>
{
    public ReplaceStaffRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty()
            .WithMessage("El motivo del reemplazo es obligatorio.")
            .MaximumLength(500)
            .WithMessage("El motivo del reemplazo no puede superar los 500 caracteres.");
    }
}

public class ChangeRoomRequestValidator : AbstractValidator<ChangeRoomRequest>
{
    public ChangeRoomRequestValidator()
    {
        RuleFor(x => x.RoomId)
            .NotEmpty()
            .WithMessage("La sala es obligatoria.");
    }
}

public class GenerateSessionsRequestValidator : AbstractValidator<GenerateSessionsRequest>
{
    public GenerateSessionsRequestValidator()
    {
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .WithMessage("La fecha 'Hasta' debe ser igual o posterior a la fecha 'Desde'.");

        RuleFor(x => x)
            .Must(x => x.To.DayNumber - x.From.DayNumber <= 92)
            .WithMessage("El rango máximo de generación por lote es de 92 días.");
    }
}
