using Domain.Common;
using Domain.Forms;

namespace Domain.KeyResults;

public static class ProtocolFieldTypes
{
    public const string Boolean = "boolean";
    public const string Integer = "integer";
    public const string Percentage = "percentage";
    public const string Text = "text";
    public const string List = "list";

    // Single-select with seeded options (BLUM-7620). Distinct from Boolean, whose yes/no options the resolver
    // synthesises: a Radio question carries its own option list, e.g. the advertising-restriction question's
    // "Yes, on all council-owned property" / "Yes, on some property" / "No".
    public const string Radio = "radio";
}

// The Question Protocol's field vocabulary for quarterly-report questions. Kept separate from Domain.Forms.QuestionType
// because the protocol carries richer semantics (boolean yes/no, bounded percentage, integer minimums) that the
// renderer-facing vocabulary flattens: ToRendererQuestionType() maps onto the QuestionType values the frontend
// GovUKDynamicQuestionRenderer already understands.
public class ProtocolFieldType : ValueObject
{
    private string Value { get; }

    private ProtocolFieldType(string value)
    {
        Value = value;
    }

    public static ProtocolFieldType From(string value)
    {
        var fieldType = new ProtocolFieldType(value);

        if (!SupportedTypes.Contains(fieldType))
        {
            throw new UnsupportedProtocolFieldTypeException(value);
        }

        return fieldType;
    }

    public static ProtocolFieldType Boolean => new(ProtocolFieldTypes.Boolean);
    public static ProtocolFieldType Integer => new(ProtocolFieldTypes.Integer);
    public static ProtocolFieldType Percentage => new(ProtocolFieldTypes.Percentage);
    public static ProtocolFieldType Text => new(ProtocolFieldTypes.Text);
    public static ProtocolFieldType List => new(ProtocolFieldTypes.List);
    public static ProtocolFieldType Radio => new(ProtocolFieldTypes.Radio);

    public bool IsBoolean => Value == ProtocolFieldTypes.Boolean;
    public bool IsNumeric => Value is ProtocolFieldTypes.Integer or ProtocolFieldTypes.Percentage;
    public bool IsList => Value == ProtocolFieldTypes.List;
    public bool IsRadio => Value == ProtocolFieldTypes.Radio;

    public string ToRendererQuestionType() => Value switch
    {
        ProtocolFieldTypes.Boolean => QuestionTypes.Radio,
        ProtocolFieldTypes.Integer => QuestionTypes.Number,
        ProtocolFieldTypes.Percentage => QuestionTypes.Number,
        ProtocolFieldTypes.List => QuestionTypes.Checkbox,
        ProtocolFieldTypes.Radio => QuestionTypes.Radio,
        _ => QuestionTypes.Textarea,
    };

    public static implicit operator string(ProtocolFieldType fieldType) => fieldType.ToString();
    public static explicit operator ProtocolFieldType(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<ProtocolFieldType> SupportedTypes
    {
        get
        {
            yield return Boolean;
            yield return Integer;
            yield return Percentage;
            yield return Text;
            yield return List;
            yield return Radio;
        }
    }
}

public class UnsupportedProtocolFieldTypeException(string value)
    : Exception($"ProtocolFieldType \"{value}\" is not supported.");