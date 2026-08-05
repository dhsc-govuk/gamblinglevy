using System.Reflection;
using Application.Common.Validation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Infrastructure.Persistence;
using Xunit;

namespace Application.Tests.Unit.Validation;

/// <summary>
/// BLUM-7240 — the guard that stops the validator limits drifting back out of step with the schema.
///
/// Nine columns were live 500s when this ticket was written: the validator allowed more than the column
/// held, so the value passed validation, reached PostgreSQL, raised 22001 or 22003, and the request failed
/// with a server error — and inside the bulk-import loops the failed entity stayed tracked, so every later
/// row failed too. A sweep fixes the nine; only a test stops the tenth.
///
/// This reads the EF model, which is metadata — no database is touched and no container is needed.
/// </summary>
public class FieldLimitsTests
{
    [Fact]
    public void EveryBoundedTextColumnShouldBeCoveredByAFieldLimit()
    {
        var declared = DeclaredTextLimits();
        var uncovered = new List<string>();

        foreach (var (column, width) in BoundedTextColumns())
        {
            // A constant may be narrower than its column — being stricter than the schema is safe. What must
            // not exist is a column with no constant at or below its width, because that is a validator with
            // nothing to point at.
            if (!declared.Any(limit => limit <= width))
            {
                uncovered.Add($"{column} is varchar({width}) and no FieldLimits constant fits it");
            }
        }

        uncovered.Should().BeEmpty();
    }

    [Fact]
    public void MoneyColumnsShouldHoldTheLargestAmountTheValidatorsAccept()
    {
        // FieldLimits.MaxMoney is the ceiling PositiveMoney() enforces; if a column could not hold it, the
        // rule would be admitting values the database rejects.
        var columns = MoneyColumns().ToList();

        columns.Should().NotBeEmpty("the money columns should be discoverable, or this test proves nothing");

        foreach (var (column, precision, scale) in columns)
        {
            var maxStorable = (decimal)Math.Pow(10, precision - scale) - 1m;

            maxStorable.Should().BeGreaterThanOrEqualTo(
                FieldLimits.MaxMoney,
                "{0} is numeric({1},{2}) and must hold FieldLimits.MaxMoney",
                column,
                precision,
                scale);

            scale.Should().Be(FieldLimits.MoneyScale, "{0} should share the scale PositiveMoney() enforces", column);
        }
    }

    [Fact]
    public void FinancialYearLimitShouldMatchItsColumn()
    {
        // The headline instance of the defect class: UpdateCollectionCommandValidator allowed 20 characters
        // against a varchar(16) column, so 17 to 20 characters was a 500.
        var widths = BoundedTextColumns()
            .Where(column => column.Column.EndsWith("financial_year", StringComparison.Ordinal))
            .Select(column => column.Width)
            .ToList();

        widths.Should().NotBeEmpty();
        widths.Should().AllSatisfy(width => width.Should().BeGreaterThanOrEqualTo(FieldLimits.FinancialYear));
    }

    private static IReadOnlyList<int> DeclaredTextLimits()
    {
        return typeof(FieldLimits)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(int))
            .Select(field => (int)field.GetRawConstantValue()!)
            .ToList();
    }

    private static IEnumerable<(string Column, int Width)> BoundedTextColumns()
    {
        foreach (var (column, type) in ColumnTypes())
        {
            if (!type.StartsWith("varchar(", StringComparison.Ordinal))
            {
                continue;
            }

            var width = type["varchar(".Length..^1];
            if (int.TryParse(width, out var parsed))
            {
                yield return (column, parsed);
            }
        }
    }

    private static IEnumerable<(string Column, int Precision, int Scale)> MoneyColumns()
    {
        foreach (var (column, type) in ColumnTypes())
        {
            if (!type.StartsWith("numeric(", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = type["numeric(".Length..^1].Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out var precision) && int.TryParse(parts[1], out var scale))
            {
                yield return (column, precision, scale);
            }
        }
    }

    private static IEnumerable<(string Column, string Type)> ColumnTypes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model-only;Username=none;Password=none")
            // Matches how the context is registered in production. Without it the model builds with
            // PascalCase column names, so anything here that matches on a name would silently find nothing.
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options;

        using var context = new ApplicationDbContext(options);

        foreach (var entity in context.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();

            foreach (var property in entity.GetProperties())
            {
                var type = property.GetColumnType();
                if (!string.IsNullOrEmpty(type))
                {
                    yield return ($"{table}.{property.GetColumnName()}", type);
                }
            }
        }
    }
}