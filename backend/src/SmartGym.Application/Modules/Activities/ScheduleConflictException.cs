using SmartGym.Application.Modules.Activities.Dtos;

namespace SmartGym.Application.Modules.Activities;

public class ScheduleConflictException : Exception
{
    public IReadOnlyList<ScheduleConflictDto> Conflicts { get; }

    public ScheduleConflictException(string message, IReadOnlyList<ScheduleConflictDto> conflicts)
        : base(message)
    {
        Conflicts = conflicts ?? Array.Empty<ScheduleConflictDto>();
    }

    public ScheduleConflictException(IReadOnlyList<ScheduleConflictDto> conflicts)
        : this("Se detectaron uno o más conflictos de programación.", conflicts)
    {
    }

    public ScheduleConflictException(ScheduleConflictDto conflict, string? customMessage = null)
        : this(customMessage ?? "Ya existe un turno asignado que genera conflicto durante ese día y rango horario.", new[] { conflict })
    {
    }
}
