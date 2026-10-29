namespace Application.Submissions.Protocol;

// The "Activity delivery" wording a Joint partnership activity is asked in the quarterly report (BLUM-8090).
//
// BLUM-7860 asked these three things on the activity record itself (JP_PROGRESS / JP_CHALLENGES /
// JP_NEXT_QUARTER); BLUM-8090 moved them into the report, where every other activity already answers the same three
// questions under the module's AKR_DELIVERY_* codes. Only the wording differs, so a partnership reuses those codes —
// same required/optional flags, same 500-character limit, same answer rows, and one column of progress across every
// activity in the exports — and this swaps the copy in. The labels and hints are the Figma's (node 3264-356641);
// the progress error message is the one the retired JP_PROGRESS carried, because the label now says "this quarter".
//
// Static rather than seeded because the module's questions are one list shared by every category: a DcFormQuestion
// has no category scope, and giving it one would put partnership-only questions in the Form Designer. A code missing
// from the module's live question set simply has nothing to rewrite.
public static class JointPartnershipDeliveryCopy
{
    private static readonly IReadOnlyDictionary<string, (string Label, string Hint, string? ErrorMessage)> Copy =
        new Dictionary<string, (string Label, string Hint, string? ErrorMessage)>(StringComparer.Ordinal)
        {
            ["AKR_DELIVERY_PROGRESS"] = (
                "What progress have you made this quarter?",
                "Tell us about where you're at in the partnership process",
                "Enter a description of the progress you have made this quarter"),
            ["AKR_DELIVERY_CHALLENGES"] = (
                "What challenges have you encountered? (optional)",
                "Use this space to add any information about your challenges so far",
                null),
            ["AKR_DELIVERY_NEXT_QUARTER"] = (
                "What are you planning to do next quarter? (optional)",
                "Use this space to add any information about your plans for the next quarter",
                null),
        };

    // Rewrites a delivery question's wording for a partnership; anything else passes through untouched. The optional
    // questions keep the module's own error message (they have none — only the length limit applies).
    public static ResolvedQuestion Apply(ResolvedQuestion question)
    {
        if (question.Section != KeyResultQuestionResolver.DeliverySection || !Copy.TryGetValue(question.Code, out var copy))
        {
            return question;
        }

        return question with
        {
            Label = copy.Label,
            Hint = copy.Hint,
            ErrorMessage = copy.ErrorMessage ?? question.ErrorMessage,
        };
    }
}