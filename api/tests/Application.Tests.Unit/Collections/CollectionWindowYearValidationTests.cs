using Application.Collections.Commands.CreateCollection;
using Application.Collections.Commands.UpdateCollection;
using Application.Common.Validation;
using Domain.Collections;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace Application.Tests.Unit.Collections;

// BLUM-8053: DateOnly accepts any year up to 9999, so a collection could be saved opening in 5050. A supplied open
// date or deadline must fall in 2000-2100, on drafts as well as on publish. The messages reach the admin verbatim and
// match the wizard's client-side copy.
public class CollectionWindowYearValidationTests
{
    private const string OpenDateMessage = "Year collection opens must be between 2000 and 2100";

    private const string DeadlineMessage = "Year of submission deadline must be between 2000 and 2100";

    private static readonly Guid QuarterlyModuleId = Guid.Parse("0c9a3e41-6f0e-4c8a-9d52-2a51f7c1b002");

    private static readonly CreateCollectionCommandValidator CreateValidator = new();

    private static readonly UpdateCollectionCommandValidator UpdateValidator = new();

    public static TheoryData<int, bool> OutOfRangeYears => new()
    {
        // year, save as draft
        { 1999, true },
        { 1999, false },
        { 2101, true },
        { 2101, false },
        { 5050, true },
        { 5050, false },
    };

    public static TheoryData<int, bool> InRangeYears => new()
    {
        { 2000, true },
        { 2000, false },
        { 2100, true },
        { 2100, false },
    };

    [Theory]
    [MemberData(nameof(OutOfRangeYears))]
    public void Create_RejectsAYearOutsideTheRange(int year, bool draft)
    {
        var result = CreateValidator.TestValidate(Create(StartOf(year), EndOf(year), draft));

        result.ShouldHaveValidationErrorFor(x => x.OpenDate).WithErrorCode(ErrorCodes.OutOfRange).WithErrorMessage(OpenDateMessage);
        result.ShouldHaveValidationErrorFor(x => x.Deadline).WithErrorCode(ErrorCodes.OutOfRange).WithErrorMessage(DeadlineMessage);
        result.Errors.Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(InRangeYears))]
    public void Create_AcceptsAYearAtEitherEndOfTheRange(int year, bool draft)
    {
        CreateValidator.TestValidate(Create(StartOf(year), EndOf(year), draft)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [MemberData(nameof(OutOfRangeYears))]
    public void Update_RejectsAYearOutsideTheRange(int year, bool draft)
    {
        var result = UpdateValidator.TestValidate(Update(StartOf(year), EndOf(year), draft));

        result.ShouldHaveValidationErrorFor(x => x.CollectionOpenDate).WithErrorCode(ErrorCodes.OutOfRange).WithErrorMessage(OpenDateMessage);
        result.ShouldHaveValidationErrorFor(x => x.SubmissionDeadline).WithErrorCode(ErrorCodes.OutOfRange).WithErrorMessage(DeadlineMessage);
        result.Errors.Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(InRangeYears))]
    public void Update_AcceptsAYearAtEitherEndOfTheRange(int year, bool draft)
    {
        UpdateValidator.TestValidate(Update(StartOf(year), EndOf(year), draft)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void FlagsOnlyTheDateWhoseYearIsOutOfRange()
    {
        CreateValidator.TestValidate(Create(StartOf(1999), EndOf(2026), draft: false))
            .ShouldHaveValidationErrorFor(x => x.OpenDate).WithErrorMessage(OpenDateMessage).Only();
        CreateValidator.TestValidate(Create(StartOf(2026), EndOf(5050), draft: false))
            .ShouldHaveValidationErrorFor(x => x.Deadline).WithErrorMessage(DeadlineMessage).Only();

        UpdateValidator.TestValidate(Update(StartOf(1999), EndOf(2026), draft: false))
            .ShouldHaveValidationErrorFor(x => x.CollectionOpenDate).WithErrorMessage(OpenDateMessage).Only();
        UpdateValidator.TestValidate(Update(StartOf(2026), EndOf(5050), draft: false))
            .ShouldHaveValidationErrorFor(x => x.SubmissionDeadline).WithErrorMessage(DeadlineMessage).Only();
    }

    private static DateOnly StartOf(int year) => new(year, 1, 1);

    private static DateOnly EndOf(int year) => new(year, 12, 31);

    // Every other answer is complete and valid, so a failure can only come from the window dates.
    private static CreateCollectionCommand Create(DateOnly openDate, DateOnly deadline, bool draft) => new()
    {
        Name = "Window year",
        Type = CollectionTypes.QuarterlyReport,
        FinancialYear = "2026/27",
        Quarter = 1,
        WindowSameAsReportingPeriod = false,
        OpenDate = openDate,
        Deadline = deadline,
        IsSubmittedByAllLas = true,
        FormModuleIds = [QuarterlyModuleId],
        SaveAsDraft = draft,
    };

    private static UpdateCollectionCommand Update(DateOnly openDate, DateOnly deadline, bool draft) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Window year",
        Type = CollectionTypes.QuarterlyReport,
        FinancialYear = "2026/27",
        Quarter = 1,
        WindowSameAsReportingPeriod = false,
        CollectionOpenDate = openDate,
        SubmissionDeadline = deadline,
        IsSubmittedByAllLas = true,
        FormModuleIds = [QuarterlyModuleId],
        SaveAsDraft = draft,
    };
}