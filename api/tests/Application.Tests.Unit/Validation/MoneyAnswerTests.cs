using System.Globalization;
using Application.Common.Validation;
using FluentAssertions;
using Xunit;

namespace Application.Tests.Unit.Validation;

// BLUM-8955: the rules a Currency answer is held to. Every comma and space used to be stripped wherever it sat, so
// "5,10" validated as 510 and was shown as £510, and no amount had a minimum. The client's validateCurrencyAnswer
// (client/src/views/Activities/answers.ts) words the same messages from the same inputs. Its tests,
// client/src/views/Activities/answers.test.ts and client/src/helpers/currency.test.ts, copy these cases and
// messages by hand: nothing links the two suites, so a change here has to be made there too.
public class MoneyAnswerTests
{
    // The joint partnership's JP_COMMITTED as it is seeded: a minimum of 1 and the protocol's name for the field.
    private static readonly MoneySubject AmountAgreed = new("Amount agreed", "the amount you have agreed");

    [Theory]
    [InlineData("1")]
    [InlineData("1500")]
    [InlineData("1,500")]
    [InlineData("25 000")]
    [InlineData("25\u00a0000")]
    [InlineData("1,000,000")]
    [InlineData("1000000000")]
    [InlineData("1,000,000,000")]
    public void Validate_AcceptsWholePounds_WithOrWithoutThousandsSeparators(string value)
    {
        MoneyAnswer.Validate(value, AmountAgreed, 1).Should().BeNull();
    }

    // A separator anywhere but a thousands position is a decimal mark to the person who typed it, whichever of
    // the two it is. "0,100" has its groups in the right places and is still not a grouped amount: nothing is
    // written with a leading zero group.
    [Theory]
    [InlineData("10.50")]
    [InlineData("5,10")]
    [InlineData("0,10")]
    [InlineData("0,100")]
    [InlineData("1,5000")]
    [InlineData("1,500.50")]
    [InlineData("1,500,50")]
    [InlineData("25 000,5")]
    public void Validate_RefusesDecimals_WhicheverSeparatorMarksThem(string value)
    {
        MoneyAnswer.Validate(value, AmountAgreed, 1)
            .Should().Be("Amount agreed must be a whole number and not include pence");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("000")]
    [InlineData("-5")]
    [InlineData("- 5")]
    [InlineData("-1,500")]
    public void Validate_RefusesAnAmountBelowTheMinimum(string value)
    {
        MoneyAnswer.Validate(value, AmountAgreed, 1)
            .Should().Be("Enter an amount of 1 or more for the amount you have agreed");
    }

    [Theory]
    [InlineData("lots")]
    [InlineData("5 10")]
    [InlineData("12,34,56")]
    [InlineData("1,,500")]
    [InlineData(",500")]
    [InlineData("1,500,")]
    [InlineData(".5")]
    [InlineData("5.")]
    public void Validate_RefusesWhatIsNotANumber(string value)
    {
        MoneyAnswer.Validate(value, AmountAgreed, 1)
            .Should().Be("Enter a number for the amount you have agreed, for example 1500");
    }

    [Theory]
    [InlineData("£5")]
    [InlineData("£1,500")]
    [InlineData("5£")]
    public void Validate_RefusesATypedPoundSign(string value)
    {
        MoneyAnswer.Validate(value, AmountAgreed, 1).Should().Be("Amount agreed must not include £");
    }

    [Theory]
    [InlineData("1000000001")]
    [InlineData("1,000,000,001")]
    [InlineData("99999999999999999999999999999999999999")]
    public void Validate_RefusesAnAmountOverTheCap(string value)
    {
        MoneyAnswer.Validate(value, AmountAgreed, 1).Should().Be("Amount agreed must be £1,000,000,000 or less");
    }

    // A Currency question the protocol gives no minimum and no subject — one a DHSC admin adds in the Form
    // Designer, or one on a data-collection module — keeps the rules it had: £0 is an amount, and the messages
    // quote the label.
    [Fact]
    public void Validate_WithNoMinimum_AcceptsZero()
    {
        MoneyAnswer.Validate("0", MoneySubject.Quoting("How much?"), null).Should().BeNull();
    }

    // A quarterly-report question's minimum is a numeric column, so it arrives with a scale. The client words the
    // message from a JSON number, which has none.
    [Fact]
    public void Validate_WritesAMinimumWithoutItsScale()
    {
        MoneyAnswer.Validate("0", AmountAgreed, 1.00m)
            .Should().Be("Enter an amount of 1 or more for the amount you have agreed");
    }

    [Theory]
    [InlineData("5,10", "“How much?” must be a whole number and not include pence")]
    [InlineData("£5", "“How much?” must not include £")]
    [InlineData("lots", "Enter a number for “How much?”, for example 0 or 1500")]
    [InlineData("-5", "Enter a number for “How much?”, for example 0 or 1500")]
    [InlineData("1000000001", "“How much?” must be £1,000,000,000 or less")]
    public void Validate_WithNoSubject_QuotesTheLabel(string value, string expected)
    {
        MoneyAnswer.Validate(value, MoneySubject.Quoting("How much?"), null).Should().Be(expected);
    }

    // What the display ports read a stored amount through: the digits of a grouped amount, anything else as typed.
    [Theory]
    [InlineData("1,500", "1500")]
    [InlineData(" 25 000 ", "25000")]
    [InlineData("1500", "1500")]
    [InlineData("5,10", "5,10")]
    [InlineData("0,100", "0,100")]
    [InlineData("12.5", "12.5")]
    public void Normalise_ReadsThroughThousandsSeparatorsOnly(string value, string expected)
    {
        MoneyAnswer.Normalise(value).Should().Be(expected);
    }

    // What a display port or a spreadsheet cell reads: a sign and pence are kept, because Validate is not the one
    // asking, and only the pounds may be grouped.
    [Theory]
    [InlineData("1500", "1500")]
    [InlineData("1,500", "1500")]
    [InlineData("25 000", "25000")]
    [InlineData("-40", "-40")]
    [InlineData("- 5", "-5")]
    [InlineData("-1,500", "-1500")]
    [InlineData("1,500.50", "1500.50")]
    [InlineData("-1 500.5", "-1500.5")]
    [InlineData("0.001", "0.001")]
    public void TryRead_ReadsASignedOrDecimalAmount_ThroughItsThousandsSeparators(string value, string expected)
    {
        MoneyAnswer.TryRead(value, out var amount).Should().BeTrue();
        amount.Should().Be(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("")]
    [InlineData("5,10")]
    [InlineData("0,100")]
    [InlineData("1,5000")]
    [InlineData("1 5 0 0")]
    [InlineData("1,500.5,0")]
    [InlineData("1500.50,000")]
    [InlineData("--5")]
    [InlineData("lots")]
    public void TryRead_DoesNotReadAMisplacedSeparator(string value)
    {
        MoneyAnswer.TryRead(value, out _).Should().BeFalse();
    }
}