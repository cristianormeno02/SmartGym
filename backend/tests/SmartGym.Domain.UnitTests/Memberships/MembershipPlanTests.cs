using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Memberships;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipPlanTests
{
    [Fact]
    public void MembershipPlan_ShouldInitializeValid_WithCustomRules()
    {
        // Arrange
        var activity1 = new Activity { Id = Guid.NewGuid(), Name = "Funcional" };
        var activity2 = new Activity { Id = Guid.NewGuid(), Name = "Spinning" };

        // Act
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 8 Clases Funcional & Spinning",
            Description = "Acceso a 8 clases al mes de funcional o spinning",
            DurationDays = 30,
            Price = 28000.00m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = false
        };

        plan.AddAllowedActivity(activity1.Id);
        plan.AddAllowedActivity(activity2.Id);

        // Assert
        Assert.Equal("Pack 8 Clases Funcional & Spinning", plan.Name);
        Assert.Equal(30, plan.DurationDays);
        Assert.Equal(28000.00m, plan.Price);
        Assert.Equal(8, plan.Credits);
        Assert.False(plan.IsUnlimited);
        Assert.True(plan.AllowsActivity(activity1.Id));
        Assert.True(plan.AllowsActivity(activity2.Id));
        Assert.False(plan.AllowsActivity(Guid.NewGuid()));
    }

    [Fact]
    public void MembershipPlan_ShouldAllowAllActivities_WhenAppliesToAllIsTrue()
    {
        // Arrange & Act
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pase Libre Total",
            DurationDays = 30,
            Price = 45000.00m,
            Credits = 999,
            IsUnlimited = true,
            AppliesToAllActivities = true
        };

        // Assert
        Assert.True(plan.AllowsActivity(Guid.NewGuid()));
        Assert.True(plan.AllowsActivity(Guid.NewGuid()));
    }

    [Fact]
    public void MembershipPlan_ShouldThrowException_WhenPriceIsNegative()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new MembershipPlan
        {
            Name = "Plan Inválido",
            DurationDays = 30,
            Price = -500.00m
        });
    }

    [Fact]
    public void MembershipPlan_ShouldThrowException_WhenDurationDaysIsLessThanOne()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => new MembershipPlan
        {
            Name = "Plan Inválido",
            DurationDays = 0,
            Price = 1000.00m
        });
    }
}
