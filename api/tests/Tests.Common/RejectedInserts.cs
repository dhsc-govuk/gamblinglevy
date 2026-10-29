using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Tests.Common;

/// <summary>
/// Makes the database refuse chosen inserts, for tests about what a handler leaves behind when one of its writes
/// fails. Most writes cannot be made to fail through the API at all — an audit row, a pre-filled answer — and a
/// trigger fails them exactly where production would: as a statement error inside whatever transaction is open.
///
/// Each test class has a database of its own, so a trigger cannot reach another class; within a class, call
/// <see cref="RestoreAsync"/> in DisposeAsync, because the trigger outlives the test that installed it.
/// </summary>
public static class RejectedInserts
{
    private const string Function = "test_reject_insert";

    /// <summary>Every insert into <paramref name="table"/> fails.</summary>
    public static Task RejectAllAsync(ApplicationDbContext context, string table) =>
        InstallAsync(context, table, condition: null);

    /// <summary>Inserts into audit_logs fail for <paramref name="action"/> only, so the change they record still can.</summary>
    public static Task RejectAuditAsync(ApplicationDbContext context, string action) =>
        InstallAsync(context, "audit_logs", $"NEW.action = '{action.Replace("'", "''", StringComparison.Ordinal)}'");

    /// <summary>Drops every trigger this class installed.</summary>
    public static async Task RestoreAsync(ApplicationDbContext context)
    {
        await context.Database.ExecuteSqlRawAsync(
            $"""
            DO $$
            DECLARE t record;
            BEGIN
                FOR t IN SELECT tgname, tgrelid::regclass AS tbl FROM pg_trigger WHERE tgname LIKE '{Function}_%' LOOP
                    EXECUTE format('DROP TRIGGER %I ON %s', t.tgname, t.tbl);
                END LOOP;
            END $$;
            """);
    }

    private static async Task InstallAsync(ApplicationDbContext context, string table, string? condition)
    {
        await context.Database.ExecuteSqlRawAsync(
            $"""
            CREATE OR REPLACE FUNCTION {Function}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'insert into % rejected by test', TG_TABLE_NAME;
            END $$;
            """);

        var name = $"{Function}_{Guid.NewGuid():N}";
        var when = condition is null ? string.Empty : $"WHEN ({condition})";
        await context.Database.ExecuteSqlRawAsync(
            $"CREATE TRIGGER {name} BEFORE INSERT ON {table} FOR EACH ROW {when} EXECUTE FUNCTION {Function}()");
    }
}