using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Memberships;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipPlanSingleClassPassTests
{
    [Fact]
    public void MembershipPlan_ShouldDefaultIsSingleClassPassToFalse()
    {
        var plan = new MembershipPlan
        {
            Name = "Pase Mensual 8 Clases",
            Price = 25000m,
            Credits = 8
        };

        Assert.False(plan.IsSingleClassPass);
    }

    [Fact]
    public void IsEligibleAsSingleClassPassFor_WhenPlanHasOneCredit_ShouldBeEligible()
    {
        var activityId = Guid.NewGuid();
        var plan = new MembershipPlan
        {
            Name = "Pase Diario Funcional",
            Price = 5000m,
            Credits = 1,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        Assert.True(plan.IsEligibleAsSingleClassPassFor(activityId));
    }

    [Fact]
    public void IsEligibleAsSingleClassPassFor_WhenExplicitlyMarkedSingleClassPass_ShouldBeEligible()
    {
        var activityId = Guid.NewGuid();
        var plan = new MembershipPlan
        {
            Name = "Clase Suelta Promocional",
            Price = 4500m,
            Credits = 1,
            IsSingleClassPass = true,
            AppliesToAllActivities = true,
            IsActive = true
        };

        Assert.True(plan.IsEligibleAsSingleClassPassFor(activityId));
    }

    [Fact]
    public void IsEligibleAsSingleClassPassFor_WhenInactive_ShouldReturnFalse()
    {
        var activityId = Guid.NewGuid();
        var plan = new MembershipPlan
        {
            Name = "Pase Diario Inactivo",
            Price = 5000m,
            Credits = 1,
            IsSingleClassPass = true,
            AppliesToAllActivities = true,
            IsActive = false
        };

        Assert.False(plan.IsEligibleAsSingleClassPassFor(activityId));
    }

    [Fact]
    public void IsEligibleAsSingleClassPassFor_WhenPriceIsZero_ShouldReturnFalse()
    {
        var activityId = Guid.NewGuid();
        var plan = new MembershipPlan
        {
            Name = "Pase Gratuito Inválido",
            Price = 0m,
            Credits = 1,
            IsSingleClassPass = true,
            AppliesToAllActivities = true,
            IsActive = true
        };

        Assert.False(plan.IsEligibleAsSingleClassPassFor(activityId));
    }

    [Fact]
    public void IsEligibleAsSingleClassPassFor_WhenActivityNotAllowed_ShouldReturnFalse()
    {
        var allowedActivityId = Guid.NewGuid();
        var otherActivityId = Guid.NewGuid();

        var plan = new MembershipPlan
        {
            Name = "Pase Diario Yoga Exclusivo",
            Price = 6000m,
            Credits = 1,
            IsSingleClassPass = true,
            AppliesToAllActivities = false,
            IsActive = true
        };
        plan.AddAllowedActivity(allowedActivityId);

        Assert.True(plan.IsEligibleAsSingleClassPassFor(allowedActivityId));
        Assert.False(plan.IsEligibleAsSingleClassPassFor(otherActivityId));
    }

    [Fact]
    public void IsEligibleAsSingleClassPassFor_WhenMultiCreditPlanNotMarked_ShouldReturnFalse()
    {
        var activityId = Guid.NewGuid();
        var plan = new MembershipPlan
        {
            Name = "Pase 8 Clases",
            Price = 28000m,
            Credits = 8,
            IsSingleClassPass = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        Assert.False(plan.IsEligibleAsSingleClassPassFor(activityId));
    }
}
