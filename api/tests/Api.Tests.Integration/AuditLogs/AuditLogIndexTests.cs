using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Tests.Common;
using Xunit;

namespace Api.Tests.Integration.AuditLogs;

/// <summary>
/// BLUM-8750 — the actor index is built with CREATE INDEX CONCURRENTLY outside the migration transaction, so EF
/// knows nothing of it beyond the model snapshot. A concurrent build can also fail into an INVALID index that
/// still has the name, so this checks the catalogue rather than the model: the index is there, valid, and on the
/// columns the actor filter reads.
/// </summary>
public class AuditLogIndexTests(IntegrationTestWebFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task ShouldHaveAValidActorIndex_AfterMigrating()
    {
        var index = await Context.Database
            .SqlQuery<IndexRow>($"""
                SELECT i.indisvalid AS is_valid, pg_get_indexdef(i.indexrelid) AS definition
                FROM pg_index i
                JOIN pg_class c ON c.oid = i.indexrelid
                WHERE c.relname = 'ix_audit_logs_actor_user_id_occurred_at'
                """)
            .SingleAsync();

        index.IsValid.Should().BeTrue();
        index.Definition.Should().Contain("(actor_user_id, occurred_at)");
    }

    private sealed record IndexRow(bool IsValid, string Definition);
}