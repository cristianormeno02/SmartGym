using FluentValidation.TestHelper;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Validators;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionValidatorTests
{
    private readonly CreateClassSessionRequestValidator _createValidator = new();
    private readonly UpdateClassSessionRequestValidator _updateValidator = new();
    private readonly SuspendClassSessionRequestValidator _suspendValidator = new();
    private readonly CancelClassSessionRequestValidator _cancelValidator = new();
    private readonly RescheduleClassSessionRequestValidator _rescheduleValidator = new();
    private readonly ReplaceStaffRequestValidator _replaceStaffValidator = new();
    private readonly GenerateSessionsRequestValidator _generateValidator = new();

    [Fact]
    public void CreateClassSession_WhenValid_ShouldNotHaveValidationError()
    {
        var request = new CreateClassSessionRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            20,
            "Notas de la clase"
        );

        var result = _createValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateClassSession_WhenStartsInPast_ShouldFail()
    {
        var request = new CreateClassSessionRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(1),
            20,
            null
        );

        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.StartsAt);
    }

    [Fact]
    public void CreateClassSession_WhenEndsBeforeStarts_ShouldFail()
    {
        var request = new CreateClassSessionRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(-1),
            20,
            null
        );

        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.EndsAt);
    }

    [Fact]
    public void CreateClassSession_WhenCapacityIsZeroOrNegative_ShouldFail()
    {
        var request = new CreateClassSessionRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            0,
            null
        );

        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Capacity);
    }

    [Fact]
    public void CreateClassSession_WhenNotesExceed2000_ShouldFail()
    {
        var request = new CreateClassSessionRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            20,
            new string('A', 2001)
        );

        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void UpdateClassSession_WhenCapacityIsZero_ShouldFail()
    {
        var request = new UpdateClassSessionRequest(0, null, 1);
        var result = _updateValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Capacity);
    }

    [Fact]
    public void SuspendClassSession_WhenReasonOtherWithoutObservations_ShouldFail()
    {
        var request = new SuspendClassSessionRequest(ClassSessionSuspensionReason.Other, "");
        var result = _suspendValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Observations);
    }

    [Fact]
    public void SuspendClassSession_WhenObservationsExceed1000_ShouldFail()
    {
        var request = new SuspendClassSessionRequest(ClassSessionSuspensionReason.Weather, new string('X', 1001));
        var result = _suspendValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Observations);
    }

    [Fact]
    public void CancelClassSession_WhenReasonIsScheduleEnded_ShouldFail()
    {
        var request = new CancelClassSessionRequest(ClassSessionCancellationReason.ScheduleEnded, "Fin de horario");
        var result = _cancelValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void CancelClassSession_WhenReasonOtherWithoutObservations_ShouldFail()
    {
        var request = new CancelClassSessionRequest(ClassSessionCancellationReason.Other, null);
        var result = _cancelValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Observations);
    }

    [Fact]
    public void RescheduleClassSession_WhenReasonEmptyOrTooLong_ShouldFail()
    {
        var requestEmpty = new RescheduleClassSessionRequest(
            DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(1), null, "");
        var resultEmpty = _rescheduleValidator.TestValidate(requestEmpty);
        resultEmpty.ShouldHaveValidationErrorFor(x => x.Reason);

        var requestLong = new RescheduleClassSessionRequest(
            DateTimeOffset.UtcNow.AddDays(2), DateTimeOffset.UtcNow.AddDays(2).AddHours(1), null, new string('R', 501));
        var resultLong = _rescheduleValidator.TestValidate(requestLong);
        resultLong.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void ReplaceStaff_WhenReasonEmptyOrTooLong_ShouldFail()
    {
        var requestEmpty = new ReplaceStaffRequest(Guid.NewGuid(), "", 1);
        var resultEmpty = _replaceStaffValidator.TestValidate(requestEmpty);
        resultEmpty.ShouldHaveValidationErrorFor(x => x.Reason);

        var requestLong = new ReplaceStaffRequest(Guid.NewGuid(), new string('S', 501), 1);
        var resultLong = _replaceStaffValidator.TestValidate(requestLong);
        resultLong.ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void GenerateSessions_WhenRangeExceeds92Days_ShouldFail()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 4, 15); // > 92 days
        var request = new GenerateSessionsRequest(from, to, null);

        var result = _generateValidator.TestValidate(request);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }
}
