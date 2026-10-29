using Application.Collections.Commands.ExportSubmissions;
using Application.Collections.Commands.SendManualReminder;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.DataReset.Commands.ResetTestData;
using Application.MaturityAssessments.Commands.ExportMaturityAssessments;
using Application.Mfa;
using Application.Mfa.Commands;
using Application.Organisations.Commands.BulkAmendLocalAuthorityAllocations;
using Application.Organisations.Commands.BulkImportLocalAuthorities;
using Application.UserEmailNormalisation.Commands.NormaliseUserEmails;
using Application.Users;
using FluentAssertions;
using MediatR;
using Xunit;

namespace Application.Tests.Unit.AuditLogs;

public class AuditCoverageTests
{
    // Any one of these on a handler's constructor means it writes audit_logs rows itself.
    private static readonly Type[] AuditWriters = [typeof(IAuditLogRepository), typeof(UserDeactivator), typeof(MfaFailureRecorder)];

    // Commands that are audited without holding an audit writer. Each entry says where the row is written, so a
    // handler cannot be parked here just to make the test pass.
    private static readonly Dictionary<Type, string> AuditedElsewhere = new()
    {
        [typeof(LogoutMfaSessionCommandHandler)] = "IMfaSessionService.RevokeSessionAsync writes mfa.session.revoked",
        [typeof(RevokeMfaSessionCommandHandler)] = "IMfaSessionService.RevokeSessionAsync writes mfa.session.revoked",
    };

    // Handlers that hold IAuditLogRepository but write their row outside a transaction. Each entry says why the row
    // cannot share one with the change, so a handler cannot be parked here just to make the test pass.
    private static readonly Dictionary<Type, string> AuditedOutsideATransaction = new()
    {
        [typeof(ExportSubmissionsCommandHandler)] = "writes nothing but the audit row, which is atomic on its own",
        [typeof(ExportMaturityAssessmentsCommandHandler)] = "writes nothing but the audit row, which is atomic on its own",
        [typeof(SendManualReminderCommandHandler)] = "the row records an email fan-out, which no transaction can roll back",
        [typeof(NormaliseUserEmailsCommandHandler)] = "each user is corrected in Entra first; the row is written in a finally so a run that fails part-way still records who was corrected",
        [typeof(ResetTestDataCommandHandler)] = "the reset truncates audit_logs itself, and a failed reset is audited too",
        [typeof(BulkImportLocalAuthoritiesCommandHandler)] = "admin users are created through Entra in their own DI scopes, so there is no one context to hold a transaction",
        [typeof(BulkAmendLocalAuthorityAllocationsCommandHandler)] = "partial success per row is the contract; the summary row records which rows failed",
    };

    // BLUM-8747 (spike BLUM-8420 D1): 41 of 87 command handlers wrote no audit row, including SubmitReport and the
    // ops endpoints that truncate the database. This fails the moment a new command handler ships without one.
    [Fact]
    public void EveryCommandHandler_WritesAnAuditRow()
    {
        var unaudited = CommandHandlers()
            .Where(handler => !AuditedElsewhere.ContainsKey(handler))
            .Where(handler => !handler.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => AuditWriters.Contains(parameter.ParameterType)))
            .Select(handler => handler.FullName)
            .ToList();

        unaudited.Should().BeEmpty(
            "every state-changing command must append to audit_logs via IAuditLogRepository (or UserDeactivator / "
            + "MfaFailureRecorder); if a command is genuinely audited elsewhere, add it to AuditedElsewhere with where");
    }

    [Fact]
    public void AuditedElsewhere_OnlyListsHandlersThatHaveNoAuditWriter()
    {
        // Keeps the allow-list honest: once a listed handler gains its own audit writer the entry is stale.
        AuditedElsewhere.Keys.Should().OnlyContain(handler => !handler.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => AuditWriters.Contains(parameter.ParameterType)));
    }

    // BLUM-8748: an audit row written in its own SaveChanges is lost when the change commits and the append fails,
    // and describes something that never happened when the append commits and the change does not. A handler that
    // writes audit rows also takes an ITransactionRunner, so both go in one transaction.
    [Fact]
    public void EveryCommandHandlerThatAudits_DoesSoInsideATransaction()
    {
        var outsideATransaction = CommandHandlers()
            .Where(handler => !AuditedOutsideATransaction.ContainsKey(handler))
            .Where(handler => Takes(handler, typeof(IAuditLogRepository)) && !Takes(handler, typeof(ITransactionRunner)))
            .Select(handler => handler.FullName)
            .ToList();

        outsideATransaction.Should().BeEmpty(
            "a handler that appends audit rows must write them inside ITransactionRunner.RunAsync with the change they "
            + "record; if the row genuinely cannot share a transaction, add the handler to AuditedOutsideATransaction with why");
    }

    [Fact]
    public void AuditedOutsideATransaction_OnlyListsHandlersThatAuditWithoutARunner()
    {
        AuditedOutsideATransaction.Keys.Should().OnlyContain(handler =>
            Takes(handler, typeof(IAuditLogRepository)) && !Takes(handler, typeof(ITransactionRunner)));
    }

    private static bool Takes(Type handler, Type dependency)
    {
        return handler.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => parameter.ParameterType == dependency);
    }

    private static IEnumerable<Type> CommandHandlers()
    {
        return typeof(IAuditLogRepository).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)
                && contract.GetGenericArguments()[0].Name.EndsWith("Command", StringComparison.Ordinal)));
    }
}