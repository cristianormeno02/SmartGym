using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ActivityTests
{
    [Fact]
    public void Constructor_WithValidData_ShouldInitializePropertiesCorrectly()
    {
        // Act
        var activity = new Activity(
            code: "FUNCIONAL",
            name: "Ejercicio Funcional",
            shortDescription: "Entrenamiento de fuerza y resistencia",
            description: "Clase grupal de alta intensidad adaptada a todos los niveles.",
            equipmentNotes: "Toalla y botella de agua",
            colorHex: "#FF5733",
            defaultCapacity: 25,
            minAge: 16,
            maxAge: 65);

        // Assert
        Assert.NotEqual(Guid.Empty, activity.Id);
        Assert.Equal("FUNCIONAL", activity.Code);
        Assert.Equal("Ejercicio Funcional", activity.Name);
        Assert.Equal("ejercicio funcional", activity.NormalizedName);
        Assert.Equal("Entrenamiento de fuerza y resistencia", activity.ShortDescription);
        Assert.Equal("Clase grupal de alta intensidad adaptada a todos los niveles.", activity.Description);
        Assert.Equal("Toalla y botella de agua", activity.EquipmentNotes);
        Assert.Equal("#FF5733", activity.ColorHex);
        Assert.Equal(25, activity.DefaultCapacity);
        Assert.Equal(16, activity.MinAge);
        Assert.Equal(65, activity.MaxAge);
        Assert.Equal(ActivityStatus.Active, activity.Status);
        Assert.True(activity.IsActive);
        Assert.Null(activity.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("funcional", "FUNCIONAL")]
    [InlineData("  zumba  ", "ZUMBA")]
    [InlineData("power_up_1", "POWER_UP_1")]
    public void Constructor_ShouldNormalizeCodeToUppercaseAndTrim(string rawCode, string expectedCode)
    {
        var activity = new Activity(rawCode, "Nombre Válido");
        Assert.Equal(expectedCode, activity.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ZU")]              // Menor a 3 caracteres
    [InlineData("POWER-UP")]        // Guion medio no permitido
    [InlineData("ZUMBA!")]          // Caracter especial inválido
    [InlineData("ESTE_ES_UN_CODIGO_QUE_SUPERA_LOS_CINCUENTA_CARACTERES_PERMITIDOS_EN_EL_SISTEMA")]
    public void Constructor_WithInvalidCode_ShouldThrowArgumentException(string invalidCode)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Activity(invalidCode, "Nombre Válido"));
        Assert.Equal("code", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithEmptyName_ShouldThrowArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => new Activity("ZUMBA", "   "));
        Assert.Equal("name", ex.ParamName);
    }

    [Fact]
    public void Constructor_ShouldNormalizeNameToSearchNormalizedName()
    {
        var activity = new Activity("AERO_BOX", "  Aéro-Bóx Especial  ");
        Assert.Equal("Aéro-Bóx Especial", activity.Name);
        Assert.Equal("aero-box especial", activity.NormalizedName);
    }

    [Theory]
    [InlineData("#ff5733", "#FF5733")]
    [InlineData("  #00AABB  ", "#00AABB")]
    public void Constructor_ShouldNormalizeColorHexToUppercase(string rawColor, string expectedColor)
    {
        var activity = new Activity("ZUMBA", "Zumba", colorHex: rawColor);
        Assert.Equal(expectedColor, activity.ColorHex);
    }

    [Theory]
    [InlineData("#GGG000")]
    [InlineData("FF5733")]
    [InlineData("#FFF")]
    [InlineData("#1234567")]
    public void Constructor_WithInvalidColorHex_ShouldThrowArgumentException(string invalidColor)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Activity("ZUMBA", "Zumba", colorHex: invalidColor));
        Assert.Equal("colorHex", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_WithNonPositiveDefaultCapacity_ShouldThrowArgumentException(int invalidCapacity)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Activity("ZUMBA", "Zumba", defaultCapacity: invalidCapacity));
        Assert.Equal("value", ex.ParamName);
    }

    [Theory]
    [InlineData(-1, 20, "minAge")]
    [InlineData(10, -1, "maxAge")]
    [InlineData(25, 20, "minAge")]
    public void Constructor_WithInvalidAgeRange_ShouldThrowArgumentException(int minAge, int maxAge, string expectedParam)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Activity("ZUMBA", "Zumba", minAge: minAge, maxAge: maxAge));
        Assert.Equal(expectedParam, ex.ParamName);
    }

    [Fact]
    public void UpdateDetails_ShouldCleanEmptyStringsToNull()
    {
        var activity = new Activity("ZUMBA", "Zumba", "Resumen", "Descripción", "Notas", "#FFFFFF", 20, 10, 50);

        activity.UpdateDetails("Zumba Gold", "   ", "", null, null, null, null, null);

        Assert.Equal("Zumba Gold", activity.Name);
        Assert.Equal("zumba gold", activity.NormalizedName);
        Assert.Null(activity.ShortDescription);
        Assert.Null(activity.Description);
        Assert.Null(activity.EquipmentNotes);
        Assert.Null(activity.ColorHex);
        Assert.Null(activity.DefaultCapacity);
        Assert.Null(activity.MinAge);
        Assert.Null(activity.MaxAge);
        Assert.NotNull(activity.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(16, 65, 25, true)]
    [InlineData(16, 65, 16, true)]
    [InlineData(16, 65, 65, true)]
    [InlineData(16, 65, 15, false)]
    [InlineData(16, 65, 66, false)]
    [InlineData(null, 50, 25, true)]
    [InlineData(null, 50, 55, false)]
    [InlineData(18, null, 25, true)]
    [InlineData(18, null, 15, false)]
    [InlineData(null, null, 25, true)]
    public void IsAgeAllowed_ShouldEvaluateRangesCorrectly(int? minAge, int? maxAge, int age, bool expected)
    {
        var activity = new Activity("ZUMBA", "Zumba", minAge: minAge, maxAge: maxAge);
        Assert.Equal(expected, activity.IsAgeAllowed(age));
    }

    [Fact]
    public void IsActive_Setter_ShouldThrowInvalidOperationException()
    {
        var activity = new Activity("ZUMBA", "Zumba");
        var ex = Assert.Throws<InvalidOperationException>(() => activity.IsActive = false);
        Assert.Contains("IsActive de una actividad se deriva de su estado", ex.Message);
    }

    [Fact]
    public void StateTransitions_ValidLifecycle_ShouldUpdateStatusAndIsActive()
    {
        var activity = new Activity("ZUMBA", "Zumba");
        Assert.Equal(ActivityStatus.Active, activity.Status);
        Assert.True(activity.IsActive);

        // Active -> Inactive
        Assert.True(activity.ChangeStatus(ActivityStatus.Inactive));
        Assert.Equal(ActivityStatus.Inactive, activity.Status);
        Assert.False(activity.IsActive);
        Assert.NotNull(activity.UpdatedAtUtc);

        // Inactive -> Archived
        Assert.True(activity.ChangeStatus(ActivityStatus.Archived));
        Assert.Equal(ActivityStatus.Archived, activity.Status);
        Assert.False(activity.IsActive);

        // Archived -> Inactive
        Assert.True(activity.ChangeStatus(ActivityStatus.Inactive));
        Assert.Equal(ActivityStatus.Inactive, activity.Status);
        Assert.False(activity.IsActive);

        // Inactive -> Active
        Assert.True(activity.ChangeStatus(ActivityStatus.Active));
        Assert.Equal(ActivityStatus.Active, activity.Status);
        Assert.True(activity.IsActive);
    }

    [Theory]
    [InlineData(ActivityStatus.Active, ActivityStatus.Archived)]
    [InlineData(ActivityStatus.Archived, ActivityStatus.Active)]
    public void ChangeStatus_InvalidTransition_ShouldThrowInvalidOperationException(ActivityStatus initial, ActivityStatus target)
    {
        var activity = new Activity("ZUMBA", "Zumba");
        if (initial == ActivityStatus.Archived)
        {
            activity.ChangeStatus(ActivityStatus.Inactive);
            activity.ChangeStatus(ActivityStatus.Archived);
        }

        Assert.Equal(initial, activity.Status);
        var ex = Assert.Throws<InvalidOperationException>(() => activity.ChangeStatus(target));
        Assert.Contains("Transición de estado no permitida", ex.Message);
    }

    [Fact]
    public void ChangeStatus_SameStatus_ShouldReturnFalseAndNotChangeTimestamp()
    {
        var activity = new Activity("ZUMBA", "Zumba");
        var initialTimestamp = activity.UpdatedAtUtc;

        var changed = activity.ChangeStatus(ActivityStatus.Active);

        Assert.False(changed);
        Assert.Equal(ActivityStatus.Active, activity.Status);
        Assert.Equal(initialTimestamp, activity.UpdatedAtUtc);
    }

    [Fact]
    public void ResolveCapacity_WithExplicitCapacity_ShouldPreferIt()
    {
        var activity = new Activity("FUNCIONAL", "Funcional", defaultCapacity: 20);

        Assert.Equal(15, activity.ResolveCapacity(15));
    }

    [Fact]
    public void ResolveCapacity_WithoutExplicitCapacity_ShouldUseDefaultCapacity()
    {
        var activity = new Activity("FUNCIONAL", "Funcional", defaultCapacity: 20);

        Assert.Equal(20, activity.ResolveCapacity(null));
    }

    [Fact]
    public void ResolveCapacity_WithoutAnyCapacity_ShouldThrow()
    {
        var activity = new Activity("RUNNING", "Running");

        var ex = Assert.Throws<ArgumentException>(() => activity.ResolveCapacity(null));
        Assert.Contains("cupo", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ResolveCapacity_WithNonPositiveExplicitCapacity_ShouldThrow(int requested)
    {
        var activity = new Activity("FUNCIONAL", "Funcional", defaultCapacity: 20);

        Assert.Throws<ArgumentException>(() => activity.ResolveCapacity(requested));
    }
}
