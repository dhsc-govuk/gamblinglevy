using Application.Collections.Commands.CreateCollection;
using Application.Collections.Commands.UpdateCollection;
using Application.Common.Validation;
using Application.MaturityAssessments.Protocol;
using Domain.Collections;
using FluentValidation.TestHelper;
using Xunit;

namespace Application.Tests.Unit.Collections;

// BLUM-7943: a collection's forms must belong to its type — catalogue sections for a maturity assessment, form
// modules for a quarterly report. Checked on drafts as well as on publish, so switching type in the wizard can never
// persist the other type's selection.
public class CollectionFormTypeValidationTests
{
    private static readonly Guid QuarterlyModuleId = Guid.Parse("0c9a3e41-6f0e-4c8a-9d52-2a51f7c1b001");

    private static readonly CreateCollectionCommandValidator CreateValidator = new();

    private static readonly UpdateCollectionCommandValidator UpdateValidator = new();

    public static TheoryData<string, bool> Cases => new()
    {
        // type, save as draft
        { CollectionTypes.MaturityAssessment, true },
        { CollectionTypes.MaturityAssessment, false },
        { CollectionTypes.QuarterlyReport, true },
        { CollectionTypes.QuarterlyReport, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Create_AcceptsFormsOfItsOwnType(string type, bool draft)
    {
        var result = CreateValidator.TestValidate(Create(type, OwnIds(type), draft));

        result.ShouldNotHaveValidationErrorFor(x => x.FormModuleIds);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Create_RejectsFormsOfTheOtherType(string type, bool draft)
    {
        var result = CreateValidator.TestValidate(Create(type, OwnIdsPlusOneForeign(type), draft));

        result.ShouldHaveValidationErrorFor(x => x.FormModuleIds)
            .WithErrorCode(ErrorCodes.NotAllowed)
            .WithErrorMessage(ExpectedMessage(type))
            .Only();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Update_AcceptsFormsOfItsOwnType(string type, bool draft)
    {
        var result = UpdateValidator.TestValidate(Update(type, OwnIds(type), draft));

        result.ShouldNotHaveValidationErrorFor(x => x.FormModuleIds);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Update_RejectsFormsOfTheOtherType(string type, bool draft)
    {
        var result = UpdateValidator.TestValidate(Update(type, OwnIdsPlusOneForeign(type), draft));

        result.ShouldHaveValidationErrorFor(x => x.FormModuleIds)
            .WithErrorCode(ErrorCodes.NotAllowed)
            .WithErrorMessage(ExpectedMessage(type))
            .Only();
    }

    [Fact]
    public void Draft_WithNoTypeYet_DoesNotJudgeTheForms()
    {
        var ids = new List<Guid> { QuarterlyModuleId, MaturityCollectionForms.Ids[0] };

        CreateValidator.TestValidate(Create(null, ids, draft: true)).ShouldNotHaveValidationErrorFor(x => x.FormModuleIds);
        UpdateValidator.TestValidate(Update(null, ids, draft: true)).ShouldNotHaveValidationErrorFor(x => x.FormModuleIds);
    }

    private static IReadOnlyList<Guid> OwnIds(string type) =>
        type == CollectionTypes.MaturityAssessment ? MaturityCollectionForms.Ids : [QuarterlyModuleId];

    // A single foreign id among valid ones is enough to refuse the whole list.
    private static IReadOnlyList<Guid> OwnIdsPlusOneForeign(string type) =>
        type == CollectionTypes.MaturityAssessment
            ? [.. MaturityCollectionForms.Ids, QuarterlyModuleId]
            : [QuarterlyModuleId, MaturityCollectionForms.Ids[0]];

    private static string ExpectedMessage(string type) => type == CollectionTypes.MaturityAssessment
        ? "Select only forms for a maturity assessment"
        : "Select only forms for a quarterly report";

    // Every other answer is complete and valid, so a failure can only come from the forms.
    private static CreateCollectionCommand Create(string? type, IReadOnlyList<Guid> formModuleIds, bool draft) => new()
    {
        Name = "Forms by type",
        Type = type,
        FinancialYear = "2026/27",
        Quarter = type == CollectionTypes.QuarterlyReport ? 1 : null,
        WindowSameAsReportingPeriod = true,
        IsSubmittedByAllLas = true,
        FormModuleIds = formModuleIds,
        SaveAsDraft = draft,
    };

    private static UpdateCollectionCommand Update(string? type, IReadOnlyList<Guid> formModuleIds, bool draft) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Forms by type",
        Type = type,
        FinancialYear = "2026/27",
        Quarter = type == CollectionTypes.QuarterlyReport ? 1 : null,
        WindowSameAsReportingPeriod = true,
        IsSubmittedByAllLas = true,
        FormModuleIds = formModuleIds,
        SaveAsDraft = draft,
    };
}