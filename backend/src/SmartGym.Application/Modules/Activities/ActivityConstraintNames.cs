namespace SmartGym.Application.Modules.Activities;

/// <summary>
/// Nombres de los índices únicos del módulo. La configuración de EF los usa para crearlos y
/// <c>ActivityService</c> para traducir sus violaciones (23505) a conflictos de negocio.
/// </summary>
public static class ActivityConstraintNames
{
    public const string CodeUnique = "IX_Activities_Code";
    public const string SinglePrimaryImage = "IX_ActivityMedias_ActivityId_IsPrimary";
    public const string SingleLogo = "IX_ActivityMedias_ActivityId_Logo";
}
