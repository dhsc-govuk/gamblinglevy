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
    public const string PeopleCategoryName = "People and roles";

    // Needs-assessment key-result screens keep the protocol's original "select at least 2" trigger.
    public const string NeedsAssessmentCategoryName = "Needs assessment";

    public const string SelectionError = "Select the outcomes you will use to measure this activity";

    // Minimum key-result selections on completion (Question Protocol 3 July), counting standard selections and
    // custom entries together: types with standard options need 1, except needs-assessment which stays at 2;
    // types with no standard options (the "Other type of …" free-text screens) still require 2 custom entries.
    // People-and-roles is exempt entirely.
    public static int MinimumFor(bool hasStandardDefinitions, bool isNeedsAssessmentCategory) =>
        !hasStandardDefinitions || isNeedsAssessmentCategory ? 2 : 1;

    // The single completion-minimum rule shared by CreateActivity and UpdateActivity: resolves the category
    // name, applies the People-and-roles exemption and the per-category minimum, and returns the error message
    // when too few key results are selected (null when satisfied).
    public static async Task<string?> ValidateMinimumAsync(
        ICategoryQueries categoryQueries,
        CategoryId? categoryId,
        int standardDefinitionCount,
        int selectedRowCount,
        CancellationToken cancellationToken)
    {
        string? categoryName = null;
        if (categoryId is not null)
        {
            var categories = await categoryQueries.GetActiveCategoriesAsync(cancellationToken);
            categoryName = categories.FirstOrDefault(c => c.Id == categoryId)?.Name;
        }

        if (categoryName == PeopleCategoryName)
        {
            return null;
        }

        var minimum = MinimumFor(standardDefinitionCount > 0, categoryName == NeedsAssessmentCategoryName);
        return selectedRowCount < minimum ? SelectionError : null;
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
                    return ([], "One or more selected key results are not valid for this activity type.");
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