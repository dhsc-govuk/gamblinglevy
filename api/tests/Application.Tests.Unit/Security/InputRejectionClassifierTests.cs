using Application.Common.Security;
using Application.Common.Validation;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Security;

/// <summary>
/// BLUM-7240 AC8 — which rejections earn an audit_logs row.
///
/// The split is the whole design: every user typo is a validation failure, each audit append is its own
/// committed transaction, and audit_logs has no retention sweep. Auditing all of them would swamp an
/// admin-facing table and let an authenticated caller choose how many transactions the service commits.
/// </summary>
public class InputRejectionClassifierTests
{
    [Fact]
    public void ShouldTreatAnUndeclaredFieldAsSecurityRelevant()
    {
        // A conforming client cannot send a field the DTO does not declare — it is a mass-assignment probe.
        var reasons = new[] { new InputRejectionReason("IsSuperAdmin", InputRejectionClassifier.UnknownFieldKind) };

        InputRejectionClassifier.Classify(reasons).Should().Be(InputRejectionTier.Security);
    }

    [Fact]
    public void ShouldTreatAnUnsafeCharacterAsSecurityRelevant()
    {
        var reasons = new[] { new InputRejectionReason("Name", InputRejectionClassifier.UnsafeCharacterKind) };

        InputRejectionClassifier.Classify(reasons).Should().Be(InputRejectionTier.Security);
    }

    [Theory]
    [InlineData(InputRejectionClassifier.FieldRuleKind)]
    [InlineData(InputRejectionClassifier.MalformedValueKind)]
    public void ShouldTreatAnOrdinaryFailureAsFieldLevel(string kind)
    {
        InputRejectionClassifier.Classify([new InputRejectionReason("Name", kind)])
            .Should().Be(InputRejectionTier.Field);
    }

    [Fact]
    public void ShouldPromoteTheWholeRequest_WhenAnyOneReasonIsSecurityRelevant()
    {
        // A hostile payload almost always arrives alongside ordinary failures — MVC adds a reason for the
        // unbound parameter itself — so the tier has to be the maximum, not the first or the majority.
        var reasons = new[]
        {
            new InputRejectionReason("request", InputRejectionClassifier.FieldRuleKind),
            new InputRejectionReason("Name", InputRejectionClassifier.UnsafeCharacterKind),
        };

        InputRejectionClassifier.Classify(reasons).Should().Be(InputRejectionTier.Security);
    }

    [Fact]
    public void ShouldTreatAnEmptyReasonListAsFieldLevel()
    {
        InputRejectionClassifier.Classify([]).Should().Be(InputRejectionTier.Field);
    }

    [Theory]
    [InlineData(ErrorCodes.ControlCharacter, InputRejectionClassifier.UnsafeCharacterKind)]
    [InlineData(ErrorCodes.UnknownField, InputRejectionClassifier.UnknownFieldKind)]
    [InlineData(ErrorCodes.MaxLength, InputRejectionClassifier.FieldRuleKind)]
    [InlineData(ErrorCodes.NotAllowed, InputRejectionClassifier.FieldRuleKind)]
    [InlineData(ErrorCodes.OutOfRange, InputRejectionClassifier.FieldRuleKind)]
    [InlineData(null, InputRejectionClassifier.FieldRuleKind)]
    public void ShouldMapAValidatorErrorCodeOntoItsKind(string? errorCode, string expected)
    {
        InputRejectionClassifier.KindForErrorCode(errorCode).Should().Be(expected);
    }

    [Fact]
    public void ShouldTreatAnyUnrecognisedSecurityPrefixedCodeAsSecurityRelevant()
    {
        // The prefix is the contract, not the exhaustive list — a validator added later gets the right
        // treatment without this classifier having to learn its name.
        var kind = InputRejectionClassifier.KindForErrorCode(ErrorCodes.SecurityPrefix + "SOMETHING_NEW");

        InputRejectionClassifier.Classify([new InputRejectionReason("X", kind)])
            .Should().Be(InputRejectionTier.Security);
    }
}