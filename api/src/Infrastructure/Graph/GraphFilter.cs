namespace Infrastructure.Graph;

/// <summary>
/// Builds OData <c>$filter</c> clauses for Microsoft Graph. Anything user-typed must go through
/// <see cref="StringLiteral"/> rather than being interpolated between quotes: an apostrophe is legal in an
/// email address (o'brien@council.gov.uk), and unescaped it closes the literal early, so Graph answers
/// "Invalid filter clause: Syntax error" and user creation fails with a 500 (BLUM-8048).
/// </summary>
public static class GraphFilter
{
    public static string MailEquals(string email) => $"mail eq {StringLiteral(email)}";

    // OData ABNF (SQUOTE-in-string): a single quote inside a string literal is written as two.
    public static string StringLiteral(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}