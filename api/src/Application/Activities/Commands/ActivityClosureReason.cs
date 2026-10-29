using Application.Common.Validation;

namespace Application.Activities.Commands;

// Shared by the Create/Update activity validators (BLUM-7673). A Closed activity records why it was closed: required
// when completing, optional on a draft, and capped on both because the column is varchar(500). The client's
// validateClosureReason (display.tsx) checks first and uses role wording for People and roles; these are the
// backstop, so they use the activity wording.
public static class ActivityClosureReason
{
    public const string RequiredError = "Enter why the activity is being closed";

    public static readonly string TooLongError = $"Reason for closure must be {FieldLimits.ClosureReason} characters or less";
}