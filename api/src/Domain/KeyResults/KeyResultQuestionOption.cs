namespace Domain.KeyResults;

// A selectable option on a list-type protocol question. Persisted as a jsonb array on the question row (the repo
// convention for small owned value collections) — options are reference data and never queried independently.
public record KeyResultQuestionOption(string Value, string Label);