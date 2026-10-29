using Domain.AuditLogs;

namespace Application.Common.Interfaces.Repositories;

public interface IAuditLogRepository
{
    Task<AuditLog> AppendAsync(AuditLog entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a row recording a change the caller has <b>already committed</b>. A failure is logged and swallowed,
    /// never thrown: answering an error for a change that went through invites a retry that the change itself now
    /// refuses ("already submitted", a duplicate name), and loses whatever the response would have reported. Takes
    /// no <see cref="CancellationToken"/> for the MfaFailureRecorder reason — a dropped socket must not cancel the
    /// record of a write that has happened. Only for work that cannot share a database transaction with its row —
    /// an external fan-out, an Entra update, a reset that truncates audit_logs. Anything else uses
    /// <see cref="AppendAsync"/> inside an <c>ITransactionRunner</c> so the row commits or rolls back with the change
    /// (BLUM-8748). Throws <see cref="InvalidOperationException"/> when called inside a transaction: PostgreSQL
    /// aborts a transaction on any failed statement, so a swallowed failure there would sink the change it records.
    /// </summary>
    /// <returns>Whether the row was written.</returns>
    Task<bool> TryAppendAsync(AuditLog entry);

    /// <summary>
    /// Deletes at most <paramref name="batchSize"/> rows that occurred before <paramref name="cutoff"/>, oldest
    /// first (BLUM-8750). The retention sweep's one statement; loop on <see cref="AuditLogDeletionResult.Found"/>.
    /// </summary>
    Task<AuditLogDeletionResult> DeleteOccurredBeforeAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of one bounded retention delete, as <see cref="SessionDeletionResult"/> is for MFA sessions: how
/// many rows the cutoff matched, and how many this call removed. A sweep loops on Found, never on Deleted.
/// </summary>
public record AuditLogDeletionResult(int Found, int Deleted);