using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Application.Tests.Unit.Migrations;

/// <summary>
/// Two habits from before go-live that a migration on a live service must not repeat (BLUM-8749). Both are
/// permanent once deployed, because a database records a migration's id in __EFMigrationsHistory and never runs
/// it again.
/// <list type="bullet">
/// <item>A placeholder in the id. BLUM_XXXX_UserPasswordLoginEnabled can never be renamed now (see its class).</item>
/// <item>Deleting reference rows so a seed-if-empty seeder would rebuild them. That re-mints ids that saved answers,
/// activities and admin edits point at. The seeders now insert whatever is missing by key, so a new row is a seeder
/// edit, a wording change is a guarded UPDATE (BLUM-8156), and a withdrawal is is_active = false (BLUM-8340).</item>
/// </list>
/// </summary>
public partial class MigrationConventionsTests
{
    // BLUM-8156 (22 Sep 2026) is the first migration written for the live service. Everything before it ran on
    // disposable MVP data and is left alone.
    private const string LiveServiceCutoff = "20260922000000";

    private static readonly string[] GrandfatheredIds = ["20260922093051_BLUM_XXXX_UserPasswordLoginEnabled"];

    // Rows the seeders own or that admins author. Deleting any of them in bulk is the "reseed" pattern.
    private static readonly HashSet<string> ReferenceTables =
    [
        "categories",
        "activity_tasks",
        "form_sections",
        "form_questions",
        "form_question_options",
        "form_modules",
        "dc_form_sections",
        "dc_form_questions",
        "dc_form_question_options",
        "key_result_definitions",
        "key_result_questions",
        "maturity_baseline_answers",
    ];

    public static IEnumerable<object[]> MigrationIds() => Migrations().Select(m => new object[] { m.Id });

    [Fact]
    public void EveryMigration_HasAnId()
    {
        typeof(ApplicationDbContext).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Migration)) && !t.IsAbstract)
            .Should().OnlyContain(t => t.GetCustomAttribute<MigrationAttribute>() != null, "EF skips a migration with no [Migration] id");
    }

    [Theory]
    [MemberData(nameof(MigrationIds))]
    public void MigrationId_IsATimestampAndANameWithoutAPlaceholder(string id)
    {
        if (GrandfatheredIds.Contains(id))
        {
            return;
        }

        id.Should().MatchRegex(@"^\d{14}_[A-Za-z][A-Za-z0-9_]*$");
        id.Should().NotContainAny(["XXXX", "xxxx"], "the id is permanent once deployed; name the ticket before generating the migration");
    }

    [Fact]
    public void MigrationsForTheLiveService_NeverDeleteReferenceRows()
    {
        var offenders = Migrations()
            .Where(m => string.CompareOrdinal(m.Id, LiveServiceCutoff) >= 0)
            .SelectMany(m => DeletedTables(m.Migration).Where(ReferenceTables.Contains).Select(table => $"{m.Id} deletes from {table}"))
            .ToList();

        offenders.Should().BeEmpty(
            "a live service keeps its reference rows: add a row through the seed catalogue, change one with a guarded "
            + "UPDATE (BLUM-8156) and withdraw one with is_active = false (BLUM-8340)");
    }

    [Theory]
    [InlineData("DELETE FROM key_result_questions;", "key_result_questions")]
    [InlineData("delete from public.\"categories\" where name = 'x';", "categories")]
    [InlineData("TRUNCATE TABLE form_questions, form_sections CASCADE;", "form_questions", "form_sections")]
    [InlineData("TRUNCATE key_result_questions\nUPDATE submissions SET status = 'x', reviewed = true", "key_result_questions")]
    [InlineData("UPDATE form_questions SET is_active = FALSE WHERE code = 'x';")]
    public void DeletedTables_FindsEveryTableASqlStatementEmpties(string sql, params string[] expected)
    {
        DeletedTables(sql).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void DeletedTables_CountsADroppedTable()
    {
        // Dropping and recreating a table re-mints its ids as surely as emptying it does.
        DeletedTables(new DropsATable()).Should().Equal("key_result_questions");
    }

    private static IEnumerable<(string Id, Migration Migration)> Migrations() =>
        typeof(ApplicationDbContext).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Migration)) && !t.IsAbstract)
            .Select(t => (Id: t.GetCustomAttribute<MigrationAttribute>()?.Id, Type: t))
            .Where(m => m.Id != null)
            .Select(m => (m.Id!, (Migration)Activator.CreateInstance(m.Type)!))
            .OrderBy(m => m.Item1, StringComparer.Ordinal);

    private static IEnumerable<string> DeletedTables(Migration migration) =>
        migration.UpOperations.SelectMany(operation => operation switch
        {
            SqlOperation sql => DeletedTables(sql.Sql),
            DeleteDataOperation delete => [delete.Table],
            DropTableOperation drop => [drop.Name],
            _ => [],
        });

    private static IEnumerable<string> DeletedTables(string sql)
    {
        foreach (Match match in DeleteFrom().Matches(sql))
        {
            yield return match.Groups["table"].Value.ToLowerInvariant();
        }

        foreach (Match match in Truncate().Matches(sql))
        {
            foreach (var table in match.Groups["tables"].Value.Split(','))
            {
                yield return Unquote(table.Trim());
            }
        }
    }

    private static string Unquote(string table) =>
        table.Split('.')[^1].Trim('"').ToLowerInvariant();

    [GeneratedRegex(@"\bDELETE\s+FROM\s+(?:ONLY\s+)?(?:""?\w+""?\.)?""?(?<table>\w+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex DeleteFrom();

    // The table list only: comma-separated, optionally schema-qualified and quoted names. It stops at the first thing
    // that is not one, so neither CASCADE nor a following statement with no semicolon in between is read as a table.
    [GeneratedRegex(@"\bTRUNCATE\s+(?:TABLE\s+)?(?:ONLY\s+)?(?<tables>(?:""?\w+""?\.)?""?\w+""?(?:\s*,\s*(?:""?\w+""?\.)?""?\w+""?)*)", RegexOptions.IgnoreCase)]
    private static partial Regex Truncate();

    private sealed class DropsATable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("key_result_questions");
    }
}