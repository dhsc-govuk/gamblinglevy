using Application.Common.Interfaces.Queries;
using Domain.Categories;
using Domain.KeyResults;

namespace Application.Activities.Commands;

// One key result submitted with an activity (BLUM-6611 standardisation): either a standard selection
// (KeyResultCode set — Text is ignored and snapshotted from the definition) or a custom "Other" entry
// (KeyResultCode null, free Text). Blank rows are dropped.
public record ExpectedResultInput(string? KeyResultCode, string? Text);

public static class ExpectedResultInputs
{
    // People-and-roles activities have no key results (no protocol questions exist for them).
    public const string PeopleCategoryName = CategoryNames.PeopleAndRoles;

    // All three messages are surfaced verbatim in the 422 body and are mirrored exactly by the client, which uses
    // them to decide that a refusal belongs on the expected-results field rather than the generic error summary
    // (EXPECTED_RESULTS_MESSAGES in client/src/views/Activities/messages.ts). Reword one and the other must follow.
    //
    // Which of the two minimum messages applies is the screen the user is looking at: a checkbox list of standard
    // measures says "Select", the free-text-only "Other type of …" screens say "Enter" (Question Protocol,
    // 13 August — BLUM-7620).
    public const string SelectionError = "Select at least one measure";

    public const string EntryError = "Enter at least one measure";

    public const string InvalidCodeError = "One or more selected outcomes are not valid for this activity type.";

    // The activity name became mandatory on completion in the 13 August protocol (1.BCDEF.1.1). People-and-roles
    // is exempt for the same reason it is exempt from key results: a role record is named by its role type and
    // never shows the field. Joint partnership is exempt too since BLUM-8090 — see ValidateCompletionAsync.
    public const string NameRequiredError = "Enter the name of the activity";

    // Minimum key-result selections on completion, counting standard selections and custom entries together. The
    // 13 August protocol struck needs assessment's "<2 selected" trigger through in favour of "<1 selected", and
    // reworded every screen's error and hint to "at least one" — including the free-text-only screens, which drop
    // from 2 entries to 1. People-and-roles is exempt entirely.
    public const int MinimumSelections = 1;

    // The minimum message for a screen: standard checkbox options ask the user to select, the free-text-only
    // "Other type of …" screens ask them to enter.
    public static string MinimumMessageFor(bool hasStandardDefinitions) => hasStandardDefinitions ? SelectionError : EntryError;

    // The completion rules that need the category resolved, shared by CreateActivity and UpdateActivity: the
    // activity name and the key-result minimum. The category name is read once and answers both. Each element is
    // null when its rule is satisfied.
    //
    // The two exemptions are kept as independent rules rather than one "is this a special category?" branch.
    // People-and-roles is exempt from both (a role is named by its role type, and no protocol questions exist for
    // any of its types). Joint partnership is exempt from the minimum because it has no key-result definitions
    // (BLUM-7860), and from the name since BLUM-8090 removed the field from its record — the Figma asks only what
    // was committed and who the partners are. The consequence BLUM-7860 named the field to avoid is accepted: the
    // funding section labels each row Title -> CustomTaskText -> type name, so an unnamed partnership reads "Joint
    // partnership" there and in every list.
    public static async Task<(string? NameError, string? ExpectedResultsError)> ValidateCompletionAsync(
        ICategoryQueries categoryQueries,
        CategoryId? categoryId,
        string? title,
        int standardDefinitionCount,
        int selectedRowCount,
        CancellationToken cancellationToken)
    {
        var categoryName = await ResolveCategoryNameAsync(categoryQueries, categoryId, cancellationToken);

        var namedByItsType = categoryName == PeopleCategoryName || categoryName == CategoryNames.JointPartnership;
        var hasKeyResults = categoryName != PeopleCategoryName && categoryName != CategoryNames.JointPartnership;

        var nameError = !namedByItsType && string.IsNullOrWhiteSpace(title) ? NameRequiredError : null;
        var minimumError = hasKeyResults && selectedRowCount < MinimumSelections
            ? MinimumMessageFor(standardDefinitionCount > 0)
            : null;
        return (nameError, minimumError);
    }

    // The name of the category a command names, or null when it names none (a draft) or one that is not active.
    // Category ids are minted per environment, so every category-specific completion rule keys on this name — the
    // two above, and the Joint partnership start-date exemption in Create/UpdateActivityCommandValidator.
    public static async Task<string?> ResolveCategoryNameAsync(
        ICategoryQueries categoryQueries,
        CategoryId? categoryId,
        CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            return null;
        }

        var categories = await categoryQueries.GetActiveCategoriesAsync(cancellationToken);
        return categories.FirstOrDefault(c => c.Id == categoryId)?.Name;
    }

    // Normalises raw inputs into persistable (code, text) rows against the activity type's standard definitions.
    // Returns an error message instead when a standard code does not belong to the type.
    public static (IReadOnlyList<(string? KeyResultCode, string Text)> Rows, string? Error) Normalise(
        IReadOnlyList<ExpectedResultInput> inputs,
        IReadOnlyList<KeyResultDefinition> definitions)
    {
        var labelByCode = definitions.ToDictionary(d => d.Code, d => d.Label, StringComparer.Ordinal);
        var rows = new List<(string? KeyResultCode, string Text)>();
        var seenCodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var input in inputs)
        {
            if (!string.IsNullOrWhiteSpace(input.KeyResultCode))
            {
                var code = input.KeyResultCode.Trim();
                if (!labelByCode.TryGetValue(code, out var label))
                {
                    return ([], InvalidCodeError);
                }

                if (seenCodes.Add(code))
                {
                    rows.Add((code, label));
                }
            }
            else if (!string.IsNullOrWhiteSpace(input.Text))
            {
                rows.Add((null, input.Text.Trim()));
            }
        }

        return (rows, null);
    }
}