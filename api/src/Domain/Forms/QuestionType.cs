using Domain.Common;

namespace Domain.Forms;

public static class QuestionTypes
{
    // PascalCase canonical values — these must match the string cases in the frontend
    // GovUKDynamicQuestionRenderer switch (e.g. case 'Text', 'Radio', ...).
    public const string Text = "Text";
    public const string Textarea = "Textarea";
    public const string Number = "Number";
    public const string Radio = "Radio";
    public const string Checkbox = "Checkbox";
    public const string Select = "Select";

    // A whole-pound money amount, rendered with a "£" prefix (BLUM-7860). Distinct from Number because the
    // prefix, the no-pence rule and the amount cap all follow from the type rather than from per-question
    // metadata, and because Number's answers are counts and percentages that must not gain a currency symbol.
    public const string Currency = "Currency";
}

public class QuestionType : ValueObject
{
    private string Value { get; }

    private QuestionType(string value)
    {
        Value = value;
    }

    public static QuestionType From(string value)
    {
        var type = new QuestionType(value);

        if (!SupportedTypes.Contains(type))
        {
            throw new UnsupportedQuestionTypeException(value);
        }

        return type;
    }

    public static QuestionType Text => new(QuestionTypes.Text);
    public static QuestionType Textarea => new(QuestionTypes.Textarea);
    public static QuestionType Number => new(QuestionTypes.Number);
    public static QuestionType Radio => new(QuestionTypes.Radio);
    public static QuestionType Checkbox => new(QuestionTypes.Checkbox);
    public static QuestionType Select => new(QuestionTypes.Select);

    public static QuestionType Currency => new(QuestionTypes.Currency);

    // The values From() accepts. Exposed so validators can reject an unsupported value with a 400 rather
    // than letting From() throw out of a handler as an unhandled 500 (BLUM-7240).
    public static IEnumerable<string> SupportedTypeNames => SupportedTypes.Select(type => type.Value);

    public static bool IsSupported(string? value)
    {
        return value is not null && SupportedTypeNames.Contains(value, StringComparer.Ordinal);
    }

    // Choice types render a fixed option set (radio/checkbox/select) and therefore require options.
    public bool IsChoice => Value is QuestionTypes.Radio or QuestionTypes.Checkbox or QuestionTypes.Select;

    // The same question as IsChoice, asked of a raw string. Validators need it before From() has been
    // called, to require an option list without first having to construct the value object.
    public static bool IsChoiceName(string? value)
    {
        return value is QuestionTypes.Radio or QuestionTypes.Checkbox or QuestionTypes.Select;
    }

    // Checkbox answers may hold multiple selected values (persisted comma-joined).
    public bool IsMultiSelect => Value == QuestionTypes.Checkbox;

    public static implicit operator string(QuestionType type) => type.ToString();
    public static explicit operator QuestionType(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<QuestionType> SupportedTypes
    {
        get
        {
            yield return Text;
            yield return Textarea;
            yield return Number;
            yield return Radio;
            yield return Checkbox;
            yield return Select;
            yield return Currency;
        }
    }
}

public class UnsupportedQuestionTypeException(string value) : Exception($"QuestionType \"{value}\" is not supported.");