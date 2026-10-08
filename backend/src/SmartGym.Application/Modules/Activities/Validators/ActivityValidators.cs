using FluentValidation;
using SmartGym.Application.Modules.Activities.Dtos;

namespace SmartGym.Application.Modules.Activities.Validators;

public class CreateActivityRequestValidator : AbstractValidator<CreateActivityRequest>
{
    public CreateActivityRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("El código de la actividad es obligatorio.")
            .Matches("^[A-Za-z0-9_]{3,50}$").WithMessage("El código debe tener entre 3 y 50 caracteres: letras, números o guion bajo.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre de la actividad es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre no puede superar los 100 caracteres.");

        RuleFor(x => x.ShortDescription)
            .MaximumLength(250).WithMessage("El resumen no puede superar los 250 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.ShortDescription));

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("La descripción no puede superar los 2000 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.EquipmentNotes)
            .MaximumLength(500).WithMessage("Las notas de equipamiento no pueden superar los 500 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.EquipmentNotes));

        RuleFor(x => x.ColorHex)
            .Matches("^#[0-9A-Fa-f]{6}$").WithMessage("El color debe tener formato hexadecimal (#RRGGBB).")
            .When(x => !string.IsNullOrWhiteSpace(x.ColorHex));

        RuleFor(x => x.DefaultCapacity)
            .GreaterThan(0).WithMessage("La capacidad por defecto debe ser un número entero positivo.")
            .When(x => x.DefaultCapacity.HasValue);

        RuleFor(x => x.MinAge)
            .GreaterThanOrEqualTo(0).WithMessage("La edad mínima no puede ser negativa.")
            .When(x => x.MinAge.HasValue);

        RuleFor(x => x.MaxAge)
            .GreaterThanOrEqualTo(0).WithMessage("La edad máxima no puede ser negativa.")
            .When(x => x.MaxAge.HasValue);

        RuleFor(x => x)
            .Must(x => !x.MinAge.HasValue || !x.MaxAge.HasValue || x.MinAge.Value <= x.MaxAge.Value)
            .WithMessage("La edad mínima no puede ser superior a la edad máxima.")
            .When(x => x.MinAge.HasValue && x.MaxAge.HasValue);
    }
}

public class UpdateActivityRequestValidator : AbstractValidator<UpdateActivityRequest>
{
    public UpdateActivityRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("El nombre de la actividad es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre no puede superar los 100 caracteres.");

        RuleFor(x => x.ShortDescription)
            .MaximumLength(250).WithMessage("El resumen no puede superar los 250 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.ShortDescription));

        RuleFor(x => x.Description)
            .MaximumLength(2000).WithMessage("La descripción no puede superar los 2000 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.EquipmentNotes)
            .MaximumLength(500).WithMessage("Las notas de equipamiento no pueden superar los 500 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.EquipmentNotes));

        RuleFor(x => x.ColorHex)
            .Matches("^#[0-9A-Fa-f]{6}$").WithMessage("El color debe tener formato hexadecimal (#RRGGBB).")
            .When(x => !string.IsNullOrWhiteSpace(x.ColorHex));

        RuleFor(x => x.DefaultCapacity)
            .GreaterThan(0).WithMessage("La capacidad por defecto debe ser un número entero positivo.")
            .When(x => x.DefaultCapacity.HasValue);

        RuleFor(x => x.MinAge)
            .GreaterThanOrEqualTo(0).WithMessage("La edad mínima no puede ser negativa.")
            .When(x => x.MinAge.HasValue);

        RuleFor(x => x.MaxAge)
            .GreaterThanOrEqualTo(0).WithMessage("La edad máxima no puede ser negativa.")
            .When(x => x.MaxAge.HasValue);

        RuleFor(x => x)
            .Must(x => !x.MinAge.HasValue || !x.MaxAge.HasValue || x.MinAge.Value <= x.MaxAge.Value)
            .WithMessage("La edad mínima no puede ser superior a la edad máxima.")
            .When(x => x.MinAge.HasValue && x.MaxAge.HasValue);
    }
}
