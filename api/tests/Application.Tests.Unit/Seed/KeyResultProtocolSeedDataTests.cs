using Domain.KeyResults;
using FluentAssertions;
using Infrastructure.Persistence.Seed;
using Xunit;

namespace Application.Tests.Unit.Seed;

// Consistency checks over the static transcription of the Question Protocol's Results table. These guard the
// seed data against transcription slips (duplicate codes, dangling key-result or conditional references,
// option-less list questions, unbounded percentages) that would otherwise only surface at runtime.
public class KeyResultProtocolSeedDataTests
{
    // The protocol covers every activity type except the four "Other type of …" rows and the People-and-roles
    // category (3 July update, BLUM-6933): 2 needs assessment + 3 strategy + 4 licensing + 5 education
    // + 3 help = 17.
    [Fact]
    public void Protocol_CoversTheSeventeenProtocolActivityTypes()
    {
        KeyResultProtocolSeedData.Protocol.Should().HaveCount(17);
    }

    [Fact]
    public void Protocol_TaskNamesAreUnique()
    {
        var names = KeyResultProtocolSeedData.Protocol.Select(t => t.TaskName).ToList();

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void KeyResultCodes_AreUniquePerType()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            type.KeyResults.Select(k => k.Code).Should().OnlyHaveUniqueItems(
                "key-result codes must be unique within type \"{0}\"", type.TaskName);
        }
    }

    [Fact]
    public void QuestionCodeAndStatusScopePairs_AreUniquePerType()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            type.Questions.Select(q => (q.Code, q.StatusScope)).Should().OnlyHaveUniqueItems(
                "(Code, StatusScope) pairs must be unique within type \"{0}\"", type.TaskName);
        }
    }

    [Fact]
    public void EveryQuestion_ReferencesAKeyResultOfItsOwnType()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            var keyResultCodes = type.KeyResults.Select(k => k.Code).ToHashSet(StringComparer.Ordinal);

            foreach (var question in type.Questions)
            {
                keyResultCodes.Should().Contain(
                    question.KeyResultCode,
                    "question \"{0}\" of type \"{1}\" must reference a key result of that type",
                    question.Code,
                    type.TaskName);
            }
        }
    }

    [Fact]
    public void EveryConditionalQuestionCode_ReferencesAnotherQuestionOfTheSameType()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            var questionCodes = type.Questions.Select(q => q.Code).ToHashSet(StringComparer.Ordinal);

            foreach (var question in type.Questions.Where(q => q.ConditionalQuestionCode is not null))
            {
                question.ConditionalQuestionCode.Should().NotBe(
                    question.Code,
                    "question \"{0}\" of type \"{1}\" must not be conditional on itself",
                    question.Code,
                    type.TaskName);
                questionCodes.Should().Contain(
                    question.ConditionalQuestionCode,
                    "the conditional parent of question \"{0}\" of type \"{1}\" must exist in the same type",
                    question.Code,
                    type.TaskName);
            }
        }
    }

    [Fact]
    public void ListQuestions_AlwaysCarryOptions()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            foreach (var question in type.Questions.Where(q => q.FieldType == ProtocolFieldTypes.List))
            {
                question.Options.Should().NotBeNullOrEmpty(
                    "list question \"{0}\" of type \"{1}\" must have options",
                    question.Code,
                    type.TaskName);
            }
        }
    }

    [Fact]
    public void PercentageQuestions_AreBoundedZeroToOneHundred()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            foreach (var question in type.Questions.Where(q => q.FieldType == ProtocolFieldTypes.Percentage))
            {
                question.Min.Should().Be(
                    0, "percentage question \"{0}\" of type \"{1}\" must have Min 0", question.Code, type.TaskName);
                question.Max.Should().Be(
                    100, "percentage question \"{0}\" of type \"{1}\" must have Max 100", question.Code, type.TaskName);
            }
        }
    }

    [Fact]
    public void EveryStatusScopeAndFieldType_ParsesIntoItsValueObject()
    {
        foreach (var type in KeyResultProtocolSeedData.Protocol)
        {
            foreach (var question in type.Questions)
            {
                var parseScope = () => QuestionStatusScope.From(question.StatusScope);
                var parseFieldType = () => ProtocolFieldType.From(question.FieldType);

                parseScope.Should().NotThrow(
                    "the StatusScope of question \"{0}\" of type \"{1}\" must be supported", question.Code, type.TaskName);
                parseFieldType.Should().NotThrow(
                    "the FieldType of question \"{0}\" of type \"{1}\" must be supported", question.Code, type.TaskName);
            }
        }
    }
}