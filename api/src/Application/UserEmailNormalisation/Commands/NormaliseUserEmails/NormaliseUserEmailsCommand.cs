using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.UserEmailNormalisation.Exceptions;
using Application.Users.Interfaces;
using Domain.AuditLogs;
using Domain.Users;
using LanguageExt;
using MediatR;

namespace Application.UserEmailNormalisation.Commands.NormaliseUserEmails;

// One-off backfill for BLUM-7869: normalising email casing at write time (User.New/UpdateDetails,
// UsersService.Add) does not retroactively fix a User row or its Entra account created before that fix
// shipped, and until this same change fixed UsersService.Update too, editing such a user never corrected
// their Entra identity either — only recreating the account did. This walks every user once and corrects
// both sides together.
public record NormaliseUserEmailsCommand(bool DryRun = true)
    : IRequest<Either<UserEmailNormalisationException, NormaliseUserEmailsResult>>;

public record NormaliseUserEmailsResult(
    bool DryRun,
    int AlreadyNormalised,
    IReadOnlyList<NormaliseUserEmailChange> Updated,
    IReadOnlyList<NormaliseUserEmailConflict> Conflicts,
    IReadOnlyList<NormaliseUserEmailFailure> GraphFailures);

public record NormaliseUserEmailChange(Guid UserId, string PreviousEmail, string NormalisedEmail);

/// <summary>Two or more existing users already collide once normalised — a pre-existing case-duplicate
/// (see UserRepository.FindByEmailAsync's own doc comment: neither users.email nor users.sub_id has a
/// unique index). Which account is "correct", and what to do with the other's Entra identity, is a decision
/// for a human, so this job reports the group and touches none of its members.</summary>
public record NormaliseUserEmailConflict(string NormalisedEmail, IReadOnlyList<Guid> UserIds);

/// <summary>Entra rejected the correction for this user, so — deliberately — neither side was written.
/// Re-running the job will retry it.</summary>
public record NormaliseUserEmailFailure(Guid UserId, string PreviousEmail, string NormalisedEmail, string Error);

public class NormaliseUserEmailsCommandHandler(
    IUserRepository userRepository,
    IUsersService usersService,
    IAuditLogRepository auditLogRepository,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<NormaliseUserEmailsCommand, Either<UserEmailNormalisationException, NormaliseUserEmailsResult>>
{
    public async Task<Either<UserEmailNormalisationException, NormaliseUserEmailsResult>> Handle(
        NormaliseUserEmailsCommand request,
        CancellationToken cancellationToken)
    {
        // Outside the try so the finally can audit a run that stops part-way: each user is committed on its own, Entra
        // first, so an exception on one user leaves every earlier correction already made (BLUM-8747).
        var alreadyNormalised = 0;
        var updated = new List<NormaliseUserEmailChange>();
        var conflicts = new List<NormaliseUserEmailConflict>();
        var graphFailures = new List<NormaliseUserEmailFailure>();
        Guid? inFlightUserId = null;
        var completed = false;

        try
        {
            var users = await userRepository.GetAllAsync(cancellationToken);
            var groups = users.GroupBy(user => Normalise(user.Email)).ToList();

            foreach (var group in groups)
            {
                var members = group.ToList();
                if (members.Count > 1)
                {
                    conflicts.Add(new NormaliseUserEmailConflict(group.Key, members.Select(member => member.Id.Value).ToList()));
                    continue;
                }

                var user = members[0];
                var previousEmail = user.Email;
                if (previousEmail == group.Key)
                {
                    alreadyNormalised++;
                    continue;
                }

                if (request.DryRun)
                {
                    updated.Add(new NormaliseUserEmailChange(user.Id.Value, previousEmail, group.Key));
                    continue;
                }

                // Entra first, deliberately: if this fails, the local row must stay exactly as it was, so a
                // later run of this job still sees the mismatch rather than a DB row that looks fixed while
                // the identity Entra actually checks at sign-in is not.
                inFlightUserId = user.Id.Value;
                var graphResult = await usersService.Update(user.SubId.Value.ToString(), user.Name, group.Key, user.PasswordLoginEnabled, cancellationToken);
                var graphError = graphResult.IsLeft ? graphResult.LeftToSeq().Head().Message : null;
                if (graphError is not null)
                {
                    graphFailures.Add(new NormaliseUserEmailFailure(user.Id.Value, previousEmail, group.Key, graphError));
                    inFlightUserId = null;
                    continue;
                }

                user.UpdateDetails(user.Name, group.Key, dateTimeProvider.GetUtcNow(), user.Role);
                await userRepository.UpdateAsync(user, cancellationToken);
                updated.Add(new NormaliseUserEmailChange(user.Id.Value, previousEmail, group.Key));
                inFlightUserId = null;
            }

            completed = true;
            return new NormaliseUserEmailsResult(request.DryRun, alreadyNormalised, updated, conflicts, graphFailures);
        }
        catch (Exception exception)
        {
            return new UserEmailNormalisationUnknownException(exception);
        }
        finally
        {
            // A dry run changes nothing, so it writes no row; nor does a real run that failed before touching anyone.
            if (!request.DryRun && (completed || updated.Count > 0 || inFlightUserId is not null))
            {
                await AppendAuditAsync(completed, alreadyNormalised, updated, conflicts, graphFailures, inFlightUserId);
            }
        }
    }

    // Counts and ids only — never an email address. No signed-in user (shared-secret endpoint), hence UserId.Empty().
    // interruptedAtUserId is the user whose Entra identity may have been corrected without the local row following.
    private async Task AppendAuditAsync(
        bool completed,
        int alreadyNormalised,
        List<NormaliseUserEmailChange> updated,
        List<NormaliseUserEmailConflict> conflicts,
        List<NormaliseUserEmailFailure> graphFailures,
        Guid? interruptedAtUserId)
    {
        await auditLogRepository.TryAppendAsync(
            AuditLog.Create(
                UserId.Empty(),
                AuditTargetType.System,
                Guid.Empty,
                AuditAction.SystemUserEmailsNormalised,
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    completed,
                    alreadyNormalised,
                    updatedCount = updated.Count,
                    conflictCount = conflicts.Count,
                    graphFailureCount = graphFailures.Count,
                    updatedUserIds = updated.Select(change => change.UserId),
                    interruptedAtUserId,
                })));
    }

    private static string Normalise(string email) => email.Trim().ToLowerInvariant();
}