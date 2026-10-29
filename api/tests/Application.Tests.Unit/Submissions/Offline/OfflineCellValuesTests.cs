using Application.Submissions.Offline;
using Application.Submissions.Protocol;
using Domain.Forms;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Submissions.Offline;

// What a spreadsheet cell means (BLUM-8310): the upload accepts what a person plausibly types, and anything it cannot
// turn into a stored value is a message rather than a guess.
public class OfflineCellValuesTests
{
    private static ResolvedQuestion Question(string type, bool isPercentage = false, params (string Value, string Label)[] options) => new(
        Guid.NewGuid(), "CODE", "Label", null, type, true, 0, null, null,
        options.Select(option => new ResolvedQuestionOption(option.Value, option.Label)).ToList(), null, null, isPercentage, null, null, "results");

    private static readonly ResolvedQuestion Members = Question(
        QuestionTypes.Checkbox,
        false,
        ("police", "Police"),
        ("vcse", "Voluntary, community and social enterprises (VCSEs)"),
        ("other", "Other"));

    [Theory]
    [InlineData("'=1+1", "=1+1")]
    [InlineData("'-bullet", "-bullet")]
    [InlineData("'@name", "@name")]
    [InlineData("'quoted'", "'quoted'")]
    [InlineData("it's", "it's")]
    public void ShouldTakeOffOnlyTheFormulaGuard(string cell, string expected)
    {
        OfflineCellValues.StripFormulaGuard(cell).Should().Be(expected);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(" N/A ", true)]
    [InlineData("n/a", true)]
    [InlineData("NA", true)]
    [InlineData("No", false)]
    public void ShouldTreatBlankAndNotApplicableAlike(string cell, bool expected)
    {
        OfflineCellValues.IsBlankOrNotApplicable(cell).Should().Be(expected);
    }

    [Theory]
    [InlineData("Police; Other", "police,other")]
    [InlineData("other\npolice", "police,other")]
    [InlineData("‘Police’", "police")]
    [InlineData("Voluntary, community and social enterprises (VCSEs)", "vcse")]
    [InlineData("police, other", "police,other")]
    [InlineData("Police; police", "police")]
    public void ShouldReadChoices_AsTheirStoredValuesInOptionOrder(string cell, string expected)
    {
        OfflineCellValues.Parse(Members, cell).Value.Should().Be(expected);
    }

    [Fact]
    public void ShouldNameAChoiceThatIsNotAnOption()
    {
        OfflineCellValues.Parse(Members, "Police; Martians").Error.Should().Be(
            "‘Martians’ is not one of the answers listed in row 2. Separate answers with a semicolon (;)");
    }

    [Theory]
    [InlineData("£1,500", "1500")]
    [InlineData("1 500", "1500")]
    [InlineData("1500.00", "1500")]
    [InlineData("1500.5", "1500.5")]
    [InlineData("0.001", "0.001")]
    [InlineData("1500.004", "1500.004")]
    [InlineData("-5", "-5")]
    [InlineData("-1,500", "-1500")]
    [InlineData("1,500.00", "1500")]
    [InlineData("25 000", "25000")]
    public void ShouldReadMoneyAsTyped(string cell, string expected)
    {
        OfflineCellValues.Parse(Question(QuestionTypes.Currency), cell).Value.Should().Be(expected);
    }

    // BLUM-8955: every comma and space used to be stripped wherever it sat, so a decimal comma was stored as a
    // hundred times the amount — and the validators, handed 510, had nothing to refuse.
    [Theory]
    [InlineData("about £5")]
    [InlineData("5,10")]
    [InlineData("£0,10")]
    [InlineData("1,5000")]
    [InlineData("1 5 0 0")]
    public void ShouldRefuseMoneyThatIsNotANumber(string cell)
    {
        OfflineCellValues.Parse(Question(QuestionTypes.Currency), cell).Error.Should().Be("Enter a whole number of pounds, like 1500");
    }

    [Theory]
    [InlineData("25%", "25")]
    [InlineData("25 %", "25")]
    [InlineData("1,200", "1200")]
    [InlineData("1 200", "1200")]
    public void ShouldReadNumbers(string cell, string expected)
    {
        OfflineCellValues.Parse(Question(QuestionTypes.Number, isPercentage: true), cell).Value.Should().Be(expected);
    }

    // Stored as typed, so SubmissionAnswerValidator refuses it with the message the web form gives.
    [Theory]
    [InlineData("5,10")]
    [InlineData("12,5%")]
    [InlineData("2 5")]
    public void ShouldLeaveAMisplacedSeparatorInANumber_ForTheValidatorToRefuse(string cell)
    {
        var question = Question(QuestionTypes.Number, isPercentage: true);

        var stored = OfflineCellValues.Parse(question, cell).Value;

        SubmissionAnswerValidator.Validate([question], new Dictionary<string, string?> { [question.Code] = stored }, enforceRequired: true, transitive: false)
            .Should().ContainSingle().Which.Message.Should().Be("Enter a percentage, like 25");
    }

    [Fact]
    public void ShouldRefuseACharacterNoFormSends()
    {
        OfflineCellValues.Parse(Question(QuestionTypes.Text), "bad‮text").Error.Should().Be(
            "Retype this answer. It contains characters we cannot accept");
    }

    [Fact]
    public void ShouldShowStoredChoicesAsLabels()
    {
        OfflineCellValues.Display(Members, "other,police").Should().Be("Police; Other");
    }

    // BLUM-9104: an answer given with an option DHSC has since withdrawn is shown as its label, and typing that label
    // back is refused, because the upload accepts only what the web form offers.
    [Fact]
    public void ShouldShowAWithdrawnChoiceAsItsLabel_ButRefuseItOnUpload()
    {
        var question = Question(QuestionTypes.Radio, false, ("yes", "Yes"), ("no", "No")) with
        {
            WithdrawnOptions = [new ResolvedQuestionOption("partly", "Partly")],
        };
        var members = Members with { WithdrawnOptions = [new ResolvedQuestionOption("council", "Council")] };

        OfflineCellValues.Display(question, "partly").Should().Be("Partly");
        OfflineCellValues.Display(members, "council,police").Should().Be("Police; Council");
        OfflineCellValues.Parse(question, "Partly").Error.Should().Be("Enter ‘Yes’ or ‘No’");
        OfflineCellValues.Parse(members, "Council").IsError.Should().BeTrue();
    }

    [Theory]
    [InlineData("1500", "1500")]
    [InlineData("1,500", "1500")]
    [InlineData("5,10", "5,10")]
    public void ShouldShowStoredMoney_WithoutReadingAMisplacedSeparator(string stored, string expected)
    {
        OfflineCellValues.Display(Question(QuestionTypes.Currency), stored).Should().Be(expected);
    }
}