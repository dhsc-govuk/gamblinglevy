using Domain.Activities;
using Domain.Common;

namespace Domain.KeyResults;

public static class QuestionStatusScopes
{
    public const string Active = "active";
    public const string ClosedFinished = "closed_finished";
    public const string Finished = "finished";
}

// Which activity lifecycle status a protocol question's phrasing applies to. The Question Protocol asks the same
// underlying question with different wording while an activity is Active ("Are you developing…?") versus once it is
// Closed or Finished ("Did you develop…?"); a handful of follow-ups only apply to Finished activities. Variant rows
// share a question Code and differ only by scope, so saved answers survive a status change.
public class QuestionStatusScope : ValueObject
{
    private string Value { get; }

    private QuestionStatusScope(string value)
    {
        Value = value;
    }

    public static QuestionStatusScope From(string value)
    {
        var scope = new QuestionStatusScope(value);

        if (!SupportedScopes.Contains(scope))
        {
            throw new UnsupportedQuestionStatusScopeException(value);
        }

        return scope;
    }

    public static QuestionStatusScope Active => new(QuestionStatusScopes.Active);
    public static QuestionStatusScope ClosedFinished => new(QuestionStatusScopes.ClosedFinished);
    public static QuestionStatusScope Finished => new(QuestionStatusScopes.Finished);

    public bool Matches(ActivityStatus status) => Value switch
    {
        QuestionStatusScopes.Active => status.Equals(ActivityStatus.Active),
        QuestionStatusScopes.ClosedFinished => status.Equals(ActivityStatus.Closed) || status.Equals(ActivityStatus.Finished),
        QuestionStatusScopes.Finished => status.Equals(ActivityStatus.Finished),
        _ => false,
    };

    public static implicit operator string(QuestionStatusScope scope) => scope.ToString();
    public static explicit operator QuestionStatusScope(string value) => From(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    private static IEnumerable<QuestionStatusScope> SupportedScopes
    {
        get
        {
            yield return Active;
            yield return ClosedFinished;
            yield return Finished;
        }
    }
}

public class UnsupportedQuestionStatusScopeException(string value)
    : Exception($"QuestionStatusScope \"{value}\" is not supported.");