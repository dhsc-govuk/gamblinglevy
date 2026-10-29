using Domain.Common;
using Domain.Organisations;

namespace Domain.MaturityAssessments;

// One answer from an organisation's prior-year maturity SURVEY (the 2025 Microsoft Forms run, BLUM-7857), already
// translated into a maturity-assessment question code + option value by the baseline seeder. Read by the pre-fill
// engine as the lowest-precedence source; never shown or edited directly. SurveyYear scopes a future re-import of a
// different survey year without touching these rows.
public class MaturityBaselineAnswer : IEntity<MaturityBaselineAnswerId>
{
    public MaturityBaselineAnswerId Id { get; private set; }
    public OrganisationId OrganisationId { get; private set; }
    public string QuestionCode { get; private set; }
    public string Value { get; private set; }
    public int SurveyYear { get; private set; }
    public DateTime ImportedAt { get; private set; }

    private MaturityBaselineAnswer(
        MaturityBaselineAnswerId id,
        OrganisationId organisationId,
        string questionCode,
        string value,
        int surveyYear,
        DateTime importedAt)
    {
        Id = id;
        OrganisationId = organisationId;
        QuestionCode = questionCode;
        Value = value;
        SurveyYear = surveyYear;
        ImportedAt = importedAt;
    }

    public static MaturityBaselineAnswer Import(
        MaturityBaselineAnswerId id,
        OrganisationId organisationId,
        string questionCode,
        string value,
        int surveyYear,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(organisationId, nameof(organisationId));
        Guard.NotNullOrEmptyOrWhiteSpace(questionCode, nameof(questionCode));
        Guard.NotNullOrEmptyOrWhiteSpace(value, nameof(value));

        return new MaturityBaselineAnswer(
            id,
            organisationId,
            questionCode.Trim(),
            value.Trim(),
            surveyYear,
            utcNow);
    }
}