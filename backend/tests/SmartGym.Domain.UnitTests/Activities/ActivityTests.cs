using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ActivityTests
{
    [Fact]
    public void Activity_ShouldBeCreatedValid_WithProperCapacitiesAndStatus()
    {
        // Arrange
        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = "Sala Indoor",
            Capacity = 30
        };

        // Act
        var activity = new Activity
        {
            Id = Guid.NewGuid(),
            Name = "Ejercicio Funcional",
            Summary = "Entrenamiento de fuerza y resistencia funcional",
            Description = "Clase grupal de alta intensidad adaptada a todos los niveles.",
            MinCapacity = 5,
            MaxCapacity = 25,
            MinAge = 16,
            MaxAge = 65,
            DefaultRoomId = room.Id,
            DefaultRoom = room,
            Status = ActivityStatus.Enabled
        };

        // Assert
        Assert.Equal("Ejercicio Funcional", activity.Name);
        Assert.Equal(5, activity.MinCapacity);
        Assert.Equal(25, activity.MaxCapacity);
        Assert.Equal(ActivityStatus.Enabled, activity.Status);
        Assert.Equal(room.Id, activity.DefaultRoomId);
        Assert.True(activity.IsAgeAllowed(25));
        Assert.False(activity.IsAgeAllowed(14));
        Assert.False(activity.IsAgeAllowed(70));
    }

    [Fact]
    public void Activity_ShouldThrowException_WhenMaxCapacityIsLessThanMinCapacity()
    {
        // Arrange
        var activity = new Activity
        {
            Name = "Zumba",
            MinCapacity = 10
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => activity.SetCapacities(10, 5));
    }

    [Fact]
    public void Activity_ShouldThrowException_WhenMinCapacityIsLessThanOne()
    {
        // Arrange
        var activity = new Activity
        {
            Name = "Zumba"
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => activity.SetCapacities(0, 10));
    }

    [Fact]
    public void Room_ShouldInitializeWithProperties()
    {
        // Arrange & Act
        var room = new Room
        {
            Id = Guid.NewGuid(),
            Name = "Sala Outdoor",
            Description = "Área al aire libre para running y funcional",
            Capacity = 40
        };

        // Assert
        Assert.Equal("Sala Outdoor", room.Name);
        Assert.Equal(40, room.Capacity);
    }
}
