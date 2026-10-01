using FluentValidation;
using SmartGym.Application.Modules.People.Dtos;
using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.People.Validators;

public class CreatePersonRequestValidator : AbstractValidator<CreatePersonRequest>
{
    public CreatePersonRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre no puede superar los 100 caracteres.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("El apellido es obligatorio.")
            .MaximumLength(100).WithMessage("El apellido no puede superar los 100 caracteres.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("El correo electrónico no tiene un formato válido.")
            .MaximumLength(255).WithMessage("El correo electrónico no puede superar los 255 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.PrimaryPhone)
            .MaximumLength(30).WithMessage("El teléfono principal no puede superar los 30 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.PrimaryPhone));

        RuleFor(x => x.SecondaryPhone)
            .MaximumLength(30).WithMessage("El teléfono secundario no puede superar los 30 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.SecondaryPhone));

        RuleFor(x => x.BirthDate)
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("La fecha de nacimiento no puede ser futura.")
            .When(x => x.BirthDate.HasValue);

        // Document consistency
        When(HasAnyDocumentField, () =>
        {
            RuleFor(x => x.DocumentType)
                .NotNull().WithMessage("Debe especificar el tipo de documento.");

            RuleFor(x => x.DocumentNumber)
                .NotEmpty().WithMessage("Debe especificar el número de documento.");

            RuleFor(x => x)
                .Must(ValidateDocumentIssuingCountry)
                .WithMessage("El país emisor es obligatorio para este tipo de documento y debe tener 2 letras.")
                .When(x => x.DocumentType.HasValue && x.DocumentType.Value != DocumentType.Dni);

            RuleFor(x => x)
                .Must(ValidateDocumentFormat)
                .WithMessage("El formato del número de documento no es válido.")
                .When(x => x.DocumentType.HasValue && !string.IsNullOrWhiteSpace(x.DocumentNumber));
        });

        // Minor check: emergency contact is mandatory for minors under 18
        RuleFor(x => x.EmergencyContact)
            .NotNull().WithMessage("El contacto de emergencia es obligatorio para menores de 18 años.")
            .Must(ec => ec != null && !string.IsNullOrWhiteSpace(ec.Name) && !string.IsNullOrWhiteSpace(ec.Phone) && !string.IsNullOrWhiteSpace(ec.Relationship))
            .WithMessage("El contacto de emergencia debe incluir nombre, teléfono y vínculo para menores de 18 años.")
            .When(x => x.BirthDate.HasValue && IsMinor(x.BirthDate.Value));

        // When emergency contact is provided (for adults or optional)
        RuleFor(x => x.EmergencyContact)
            .Must(ec => string.IsNullOrWhiteSpace(ec.Name) && string.IsNullOrWhiteSpace(ec.Phone) && string.IsNullOrWhiteSpace(ec.Relationship) ||
                        (!string.IsNullOrWhiteSpace(ec.Name) && !string.IsNullOrWhiteSpace(ec.Phone) && !string.IsNullOrWhiteSpace(ec.Relationship)))
            .WithMessage("Si se informa un contacto de emergencia, deben completarse nombre, teléfono y vínculo.")
            .When(x => x.EmergencyContact != null && (!x.BirthDate.HasValue || !IsMinor(x.BirthDate.Value)));
    }

    private static bool HasAnyDocumentField(CreatePersonRequest r) =>
        r.DocumentType.HasValue || !string.IsNullOrWhiteSpace(r.DocumentNumber) || !string.IsNullOrWhiteSpace(r.DocumentIssuingCountry);

    private static bool ValidateDocumentIssuingCountry(CreatePersonRequest r)
    {
        return !string.IsNullOrWhiteSpace(r.DocumentIssuingCountry) && r.DocumentIssuingCountry.Trim().Length == 2;
    }

    private static bool ValidateDocumentFormat(CreatePersonRequest r)
    {
        if (!r.DocumentType.HasValue || string.IsNullOrWhiteSpace(r.DocumentNumber))
        {
            return false;
        }

        var country = r.DocumentType == DocumentType.Dni ? "AR" : r.DocumentIssuingCountry;
        return DocumentNormalizer.TryNormalize(r.DocumentType.Value, r.DocumentNumber, country, out _, out _);
    }

    private static bool IsMinor(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--;
        return age < 18;
    }
}

public class UpdatePersonRequestValidator : AbstractValidator<UpdatePersonRequest>
{
    public UpdatePersonRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("El nombre es obligatorio.")
            .MaximumLength(100).WithMessage("El nombre no puede superar los 100 caracteres.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("El apellido es obligatorio.")
            .MaximumLength(100).WithMessage("El apellido no puede superar los 100 caracteres.");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("El correo electrónico no tiene un formato válido.")
            .MaximumLength(255).WithMessage("El correo electrónico no puede superar los 255 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.PrimaryPhone)
            .MaximumLength(30).WithMessage("El teléfono principal no puede superar los 30 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.PrimaryPhone));

        RuleFor(x => x.SecondaryPhone)
            .MaximumLength(30).WithMessage("El teléfono secundario no puede superar los 30 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.SecondaryPhone));

        RuleFor(x => x.BirthDate)
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("La fecha de nacimiento no puede ser futura.")
            .When(x => x.BirthDate.HasValue);

        When(HasAnyDocumentField, () =>
        {
            RuleFor(x => x.DocumentType)
                .NotNull().WithMessage("Debe especificar el tipo de documento.");

            RuleFor(x => x.DocumentNumber)
                .NotEmpty().WithMessage("Debe especificar el número de documento.");

            RuleFor(x => x)
                .Must(ValidateDocumentIssuingCountry)
                .WithMessage("El país emisor es obligatorio para este tipo de documento y debe tener 2 letras.")
                .When(x => x.DocumentType.HasValue && x.DocumentType.Value != DocumentType.Dni);

            RuleFor(x => x)
                .Must(ValidateDocumentFormat)
                .WithMessage("El formato del número de documento no es válido.")
                .When(x => x.DocumentType.HasValue && !string.IsNullOrWhiteSpace(x.DocumentNumber));
        });

        RuleFor(x => x.EmergencyContact)
            .NotNull().WithMessage("El contacto de emergencia es obligatorio para menores de 18 años.")
            .Must(ec => ec != null && !string.IsNullOrWhiteSpace(ec.Name) && !string.IsNullOrWhiteSpace(ec.Phone) && !string.IsNullOrWhiteSpace(ec.Relationship))
            .WithMessage("El contacto de emergencia debe incluir nombre, teléfono y vínculo para menores de 18 años.")
            .When(x => x.BirthDate.HasValue && IsMinor(x.BirthDate.Value));

        RuleFor(x => x.EmergencyContact)
            .Must(ec => string.IsNullOrWhiteSpace(ec.Name) && string.IsNullOrWhiteSpace(ec.Phone) && string.IsNullOrWhiteSpace(ec.Relationship) ||
                        (!string.IsNullOrWhiteSpace(ec.Name) && !string.IsNullOrWhiteSpace(ec.Phone) && !string.IsNullOrWhiteSpace(ec.Relationship)))
            .WithMessage("Si se informa un contacto de emergencia, deben completarse nombre, teléfono y vínculo.")
            .When(x => x.EmergencyContact != null && (!x.BirthDate.HasValue || !IsMinor(x.BirthDate.Value)));
    }

    private static bool HasAnyDocumentField(UpdatePersonRequest r) =>
        r.DocumentType.HasValue || !string.IsNullOrWhiteSpace(r.DocumentNumber) || !string.IsNullOrWhiteSpace(r.DocumentIssuingCountry);

    private static bool ValidateDocumentIssuingCountry(UpdatePersonRequest r)
    {
        return !string.IsNullOrWhiteSpace(r.DocumentIssuingCountry) && r.DocumentIssuingCountry.Trim().Length == 2;
    }

    private static bool ValidateDocumentFormat(UpdatePersonRequest r)
    {
        if (!r.DocumentType.HasValue || string.IsNullOrWhiteSpace(r.DocumentNumber))
        {
            return false;
        }

        var country = r.DocumentType == DocumentType.Dni ? "AR" : r.DocumentIssuingCountry;
        return DocumentNormalizer.TryNormalize(r.DocumentType.Value, r.DocumentNumber, country, out _, out _);
    }

    private static bool IsMinor(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--;
        return age < 18;
    }
}

public class UpdateOwnContactRequestValidator : AbstractValidator<UpdateOwnContactRequest>
{
    public UpdateOwnContactRequestValidator()
    {
        RuleFor(x => x.PrimaryPhone)
            .MaximumLength(30).WithMessage("El teléfono principal no puede superar los 30 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.PrimaryPhone));

        RuleFor(x => x.SecondaryPhone)
            .MaximumLength(30).WithMessage("El teléfono secundario no puede superar los 30 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.SecondaryPhone));

        RuleFor(x => x.EmergencyContact)
            .Must(ec => string.IsNullOrWhiteSpace(ec.Name) && string.IsNullOrWhiteSpace(ec.Phone) && string.IsNullOrWhiteSpace(ec.Relationship) ||
                        (!string.IsNullOrWhiteSpace(ec.Name) && !string.IsNullOrWhiteSpace(ec.Phone) && !string.IsNullOrWhiteSpace(ec.Relationship)))
            .WithMessage("Si se informa un contacto de emergencia, deben completarse nombre, teléfono y vínculo.")
            .When(x => x.EmergencyContact != null);
    }
}

public class ChangePersonStatusRequestValidator : AbstractValidator<ChangePersonStatusRequest>
{
    public ChangePersonStatusRequestValidator()
    {
        RuleFor(x => x.TargetStatus)
            .IsInEnum().WithMessage("El estado objetivo no es válido.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("El motivo es obligatorio al bloquear una persona.")
            .MaximumLength(500).WithMessage("El motivo no puede superar los 500 caracteres.")
            .When(x => x.TargetStatus == PersonStatus.Blocked);

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("El motivo es obligatorio al registrar el fallecimiento de una persona.")
            .MaximumLength(500).WithMessage("El motivo no puede superar los 500 caracteres.")
            .When(x => x.TargetStatus == PersonStatus.Deceased);

        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("El motivo no puede superar los 500 caracteres.")
            .When(x => !string.IsNullOrWhiteSpace(x.Reason));
    }
}
