using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Validators;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ActivityValidatorTests
{
    private readonly CreateActivityRequestValidator _createValidator = new();
    private readonly UpdateActivityRequestValidator _updateValidator = new();

    [Theory]
    [InlineData("FUNCIONAL")]
    [InlineData("ZUMBA_1")]
    [InlineData("power_up")]
    public void CreateValidator_ValidCode_ShouldPass(string code)
    {
        var request = new CreateActivityRequest(code, "Nombre", null, null, null, null, null, null, null);
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ZU")]
    [InlineData("ZUMBA-1")]
    [InlineData("ZUMBA!")]
    public void CreateValidator_InvalidCode_ShouldFail(string code)
    {
        var request = new CreateActivityRequest(code, "Nombre", null, null, null, null, null, null, null);
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateActivityRequest.Code));
    }

    [Fact]
    public void CreateValidator_TextLengths_ShouldFailWhenExceeded()
    {
        var longName = new string('A', 101);
        var longSummary = new string('B', 251);
        var longDesc = new string('C', 2001);
        var longNotes = new string('D', 501);

        var request = new CreateActivityRequest("ZUMBA", longName, longSummary, longDesc, longNotes, null, null, null, null);
        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateActivityRequest.Name));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateActivityRequest.ShortDescription));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateActivityRequest.Description));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateActivityRequest.EquipmentNotes));
    }

    [Theory]
    [InlineData("#FF5733", true)]
    [InlineData("#00aabb", true)]
    [InlineData("FF5733", false)]
    [InlineData("#GGG000", false)]
    public void CreateValidator_ColorHex_Validation(string color, bool expectedValid)
    {
        var request = new CreateActivityRequest("ZUMBA", "Zumba", null, null, null, color, null, null, null);
        var result = _createValidator.Validate(request);
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(10, 20, true)]
    [InlineData(20, 10, false)]
    [InlineData(-1, 10, false)]
    [InlineData(10, -1, false)]
    public void CreateValidator_AgeRange_Validation(int minAge, int maxAge, bool expectedValid)
    {
        var request = new CreateActivityRequest("ZUMBA", "Zumba", null, null, null, null, null, minAge, maxAge);
        var result = _createValidator.Validate(request);
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public void CreateValidator_DefaultCapacity_Validation(int capacity, bool expectedValid)
    {
        var request = new CreateActivityRequest("ZUMBA", "Zumba", null, null, null, null, capacity, null, null);
        var result = _createValidator.Validate(request);
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void UpdateValidator_ValidRequest_ShouldPass()
    {
        var request = new UpdateActivityRequest("Zumba Gold", "Resumen", "Desc", "Notas", "#112233", 30, 15, 60, 1);
        var result = _updateValidator.Validate(request);
        Assert.True(result.IsValid);
    }
}
