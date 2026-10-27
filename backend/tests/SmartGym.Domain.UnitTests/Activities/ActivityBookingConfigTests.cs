using SmartGym.Domain.Entities.Activities;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ActivityBookingConfigTests
{
    [Fact]
    public void Constructor_WithoutRequiresBooking_ShouldDefaultToTrue()
    {
        var activity = new Activity("FUNCIONAL", "Entrenamiento Funcional");

        Assert.True(activity.RequiresBooking);
    }

    [Fact]
    public void Constructor_WithExplicitRequiresBooking_ShouldSetProperty()
    {
        var activity = new Activity(
            code: "SALA_LIBRE",
            name: "Musculación y Sala Libre",
            requiresBooking: false);

        Assert.False(activity.RequiresBooking);
    }

    [Fact]
    public void SetRequiresBooking_ShouldUpdatePropertyAndTimestamp()
    {
        var activity = new Activity("FUNCIONAL", "Funcional");
        Assert.True(activity.RequiresBooking);

        activity.SetRequiresBooking(false);

        Assert.False(activity.RequiresBooking);
        Assert.NotNull(activity.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateDetails_ShouldPreserveOrUpdateRequiresBooking()
    {
        var activity = new Activity("SPINNING", "Spinning", requiresBooking: true);

        activity.UpdateDetails(
            name: "Spinning Pro",
            shortDescription: null,
            description: null,
            equipmentNotes: null,
            colorHex: null,
            defaultCapacity: 20,
            minAge: null,
            maxAge: null,
            requiresBooking: false);

        Assert.False(activity.RequiresBooking);
    }
}
