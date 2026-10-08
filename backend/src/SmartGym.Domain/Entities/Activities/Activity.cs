using System.Text.RegularExpressions;
using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Activities;

public class Activity : BaseEntity
{
    public const int MaxGalleryImages = 10;
    private static readonly Regex CodePattern = new("^[A-Z0-9_]{3,50}$", RegexOptions.Compiled);
    private static readonly Regex ColorPattern = new("^#[0-9A-F]{6}$", RegexOptions.Compiled);

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? ShortDescription { get; private set; }
    public string? Description { get; private set; }
    public string? EquipmentNotes { get; private set; }
    public string? ColorHex { get; private set; }
    public int? DefaultCapacity { get; private set; }
    public int? MinAge { get; private set; }
    public int? MaxAge { get; private set; }
    public ActivityStatus Status { get; private set; } = ActivityStatus.Active;
    public uint Version { get; set; } // xmin

#pragma warning disable CS0414
    private bool _isActiveColumn = true;
#pragma warning restore CS0414

    // Derivado del estado (mismo patrón que Person). La columna se conserva porque otros módulos filtran por IsActive.
    public override bool IsActive
    {
        get => Status == ActivityStatus.Active;
        set => throw new InvalidOperationException("IsActive de una actividad se deriva de su estado; use ChangeStatus.");
    }

    public ICollection<ActivityMedia> Media { get; private set; } = new List<ActivityMedia>();

    private Activity() { }

    public Activity(
        string code,
        string name,
        string? shortDescription = null,
        string? description = null,
        string? equipmentNotes = null,
        string? colorHex = null,
        int? defaultCapacity = null,
        int? minAge = null,
        int? maxAge = null)
    {
        SetCode(code);
        UpdateDetails(name, shortDescription, description, equipmentNotes, colorHex, defaultCapacity, minAge, maxAge);
        UpdatedAtUtc = null;
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("El código de la actividad no puede estar vacío.", nameof(code));

        var normalized = NormalizeCode(code);
        if (!CodePattern.IsMatch(normalized))
            throw new ArgumentException("El código debe tener entre 3 y 50 caracteres: letras, números o guion bajo.", nameof(code));

        Code = normalized;
    }

    public void UpdateDetails(
        string name,
        string? shortDescription,
        string? description,
        string? equipmentNotes,
        string? colorHex,
        int? defaultCapacity,
        int? minAge,
        int? maxAge)
    {
        SetName(name);
        ShortDescription = Clean(shortDescription);
        Description = Clean(description);
        EquipmentNotes = Clean(equipmentNotes);
        SetColorHex(colorHex);
        SetDefaultCapacity(defaultCapacity);
        SetAgeRange(minAge, maxAge);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre de la actividad no puede estar vacío.", nameof(name));

        Name = name.Trim();
        NormalizedName = TextNormalizer.NormalizeForSearch(Name);
    }

    private void SetDefaultCapacity(int? value)
    {
        if (value is <= 0)
            throw new ArgumentException("La capacidad por defecto debe ser un entero positivo.", nameof(value));
        DefaultCapacity = value;
    }

    private void SetAgeRange(int? minAge, int? maxAge)
    {
        if (minAge is < 0) throw new ArgumentException("La edad mínima no puede ser negativa.", nameof(minAge));
        if (maxAge is < 0) throw new ArgumentException("La edad máxima no puede ser negativa.", nameof(maxAge));
        if (minAge.HasValue && maxAge.HasValue && minAge > maxAge)
            throw new ArgumentException("La edad mínima no puede ser superior a la edad máxima.", nameof(minAge));
        MinAge = minAge;
        MaxAge = maxAge;
    }

    private void SetColorHex(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
        {
            ColorHex = null;
            return;
        }

        var normalized = colorHex.Trim().ToUpperInvariant();
        if (!ColorPattern.IsMatch(normalized))
            throw new ArgumentException("El color debe tener formato hexadecimal (#RRGGBB).", nameof(colorHex));
        ColorHex = normalized;
    }

    public bool IsAgeAllowed(int age) =>
        (!MinAge.HasValue || age >= MinAge.Value) && (!MaxAge.HasValue || age <= MaxAge.Value);

    /// <summary>
    /// Cupo efectivo para un horario o clase: el explícito o, en su defecto, la capacidad por defecto.
    /// Sin ninguno de los dos no hay cupo resoluble (no se aplican valores fijos implícitos).
    /// </summary>
    public int ResolveCapacity(int? requested)
    {
        if (requested is <= 0)
            throw new ArgumentException("El cupo debe ser un entero positivo.", nameof(requested));

        return requested
            ?? DefaultCapacity
            ?? throw new ArgumentException(
                $"Debe especificarse el cupo: la actividad '{Name}' no tiene capacidad por defecto.", nameof(requested));
    }

    public bool CanTransitionTo(ActivityStatus target) => (Status, target) switch
    {
        (ActivityStatus.Active, ActivityStatus.Inactive) => true,
        (ActivityStatus.Inactive, ActivityStatus.Active) => true,
        (ActivityStatus.Inactive, ActivityStatus.Archived) => true,
        (ActivityStatus.Archived, ActivityStatus.Inactive) => true,
        _ => Status == target
    };

    /// <summary>Devuelve false si el estado ya era el solicitado (no-op).</summary>
    public bool ChangeStatus(ActivityStatus target)
    {
        if (Status == target) return false;
        if (!CanTransitionTo(target))
            throw new InvalidOperationException($"Transición de estado no permitida: {Status} → {target}.");
        Status = target;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
