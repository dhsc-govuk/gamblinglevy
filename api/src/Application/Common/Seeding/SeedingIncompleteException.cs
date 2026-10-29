namespace Application.Common.Seeding;

/// <summary>
/// One or more seeding steps failed, or seeding could not start at all (BLUM-8749). Every step that could run has
/// run. <see cref="FailedSteps"/> holds the step names only, never SQL or row values, so it is safe to put in an audit
/// row. Thrown from Infrastructure's seeding runner. It lives here so that the test-data reset handler can name the
/// failed steps.
/// </summary>
public sealed class SeedingIncompleteException(IReadOnlyList<string> failedSteps, IEnumerable<Exception> errors)
    : AggregateException($"Seeding did not complete. Failed step(s): {string.Join(", ", failedSteps)}.", errors)
{
    public IReadOnlyList<string> FailedSteps { get; } = failedSteps;
}