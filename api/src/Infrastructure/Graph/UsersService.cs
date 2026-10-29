using System.Net;
using Application.Common.Settings;
using Application.Users.Interfaces;
using Domain.Common;
using Domain.ValueObjects;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace Infrastructure.Graph;

public class UsersService : IUsersService
{
    private readonly GraphServiceClient _graphServiceClient;
    private readonly string _defaultUserPassword;
    private readonly string _domain;
    private readonly ILogger<UsersService> _logger;

    public UsersService(
        GraphServiceClient graphServiceClient,
        ApplicationSettings settings,
        ILogger<UsersService> logger)
    {
        Guard.NotNull(graphServiceClient, nameof(graphServiceClient));
        Guard.NotNull(settings, nameof(settings));
        Guard.NotNull(logger, nameof(logger));
        Guard.NotNullOrEmpty(settings.Graph.Domain, nameof(settings.Graph.Domain));
        Guard.NotNullOrEmpty(settings.Graph.DefaultUserPassword, nameof(settings.Graph.DefaultUserPassword));

        _graphServiceClient = graphServiceClient;
        _defaultUserPassword = settings.Graph.DefaultUserPassword;
        _domain = settings.Graph.Domain;
        _logger = logger;
    }

    public async Task<Option<string>> FindById(string userId, CancellationToken cancellationToken)
    {
        var user = await _graphServiceClient.Users[userId].GetAsync(cancellationToken: cancellationToken);
        if (user == null)
        {
            return Option<string>.None;
        }

        return user.Id;
    }

    // No "does not exist" case to handle: a $filter that matches nobody answers 200 with an empty page, so a
    // failure here is never "no such user". The lookup result is an Option inside the Either for the reason
    // given on SetPasswordAndIdentity.
    public Task<Either<Exception, Option<string>>> FindByEmail(string email, CancellationToken cancellationToken)
    {
        var normalisedEmail = email.Trim().ToLowerInvariant();

        return CallGraph(
            "GET users (find by email)",
            async () =>
            {
                var users = await _graphServiceClient.Users.GetAsync(
                    requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter = GraphFilter.MailEquals(normalisedEmail);
                        requestConfiguration.QueryParameters.Select = ["id"];
                        requestConfiguration.QueryParameters.Top = 1;
                    },
                    cancellationToken);

                var existingId = users?.Value?.FirstOrDefault()?.Id;
                return existingId is null ? Option<string>.None : Option<string>.Some(existingId);
            },
            cancellationToken);
    }

    public Task<Either<Exception, string>> Add(
        Name userName,
        string email,
        CancellationToken cancellationToken)
    {
        // Lower-cased deliberately: IssuerAssignedId below is what Entra's own local-account sign-in
        // matches the typed email against. A mixed-case value stored here can surface as AADSTS50020
        // ("account does not exist") for a real account whose email just differs by case — every
        // account must be created with the same casing convention this app looks up with
        // (UserRepository.FindByEmailAsync, FindByEmail above).
        var normalisedEmail = email.Trim().ToLowerInvariant();

        var user = new User
        {
            // Always enabled — see IUsersService.Add. Deactivation is enforced by
            // UserDeactivatedMiddleware against the local status, not by disabling the CIAM account.
            AccountEnabled = true,
            DisplayName = userName.ToString(),
            GivenName = userName.FirstName,
            Surname = userName.LastName,
            Mail = normalisedEmail,
            MailNickname = GenerateMailNickname(normalisedEmail),
            Identities =
            [
                new ObjectIdentity
                {
                    SignInType = "federated",
                    Issuer = "mail",
                    IssuerAssignedId = normalisedEmail,
                }
            ],
            PasswordProfile = new PasswordProfile
            {
                ForceChangePasswordNextSignIn = false,
                Password = _defaultUserPassword,
            },
        };

        return CallGraph(
            "POST user",
            async () =>
            {
                var createdUser = await _graphServiceClient.Users.PostAsync(user, cancellationToken: cancellationToken);
                return createdUser!.Id!;
            },
            cancellationToken);
    }

    public async Task<Either<Exception, Unit>> Update(
        string id,
        Name userName,
        string email,
        bool passwordLoginEnabled,
        CancellationToken cancellationToken)
    {
        // Lower-cased for the same reason as Add: IssuerAssignedId is what Entra's local-account sign-in
        // matches the typed email against. This method used to ignore `email` entirely and only patch
        // GivenName/Surname/DisplayName, which meant a mismatched Entra identity (see
        // NormaliseUserEmailsCommand) could never be corrected by editing the user — only by recreating the
        // account through Add.
        var normalisedEmail = email.Trim().ToLowerInvariant();

        var identitiesResult = await BuildMergedIdentities(id, normalisedEmail, passwordLoginEnabled, cancellationToken);

        return await identitiesResult.MatchAsync(
            merged => PatchIdentityAndProfile(id, normalisedEmail, userName, merged.Identities, cancellationToken),
            ex => ex);
    }

    private Task<Either<Exception, Unit>> PatchIdentityAndProfile(
        string id,
        string normalisedEmail,
        Name userName,
        List<ObjectIdentity> identities,
        CancellationToken cancellationToken)
    {
        var user = new User
        {
            GivenName = userName.FirstName,
            Surname = userName.LastName,
            DisplayName = userName.ToString(),
            Mail = normalisedEmail,
            MailNickname = GenerateMailNickname(normalisedEmail),
            Identities = identities,
        };

        return CallGraphForNoResult(
            $"PATCH user {id} (profile/identity)",
            () => _graphServiceClient.Users[id].PatchAsync(user, cancellationToken: cancellationToken),
            cancellationToken);
    }

    // userName is required — not just email/id — because a small, otherwise-unexplained set of accounts have
    // no local identity Graph will let this app patch onto them (see the guard in ApplyPasswordAndIdentity),
    // and recovering from that means recreating the Entra object outright, which needs the same profile
    // fields Add() does. Returns the new Entra object id when that happens (None otherwise) so the caller can
    // update User.SubId — the old id stops resolving the moment HardDelete runs inside RecreateWithPasswordLoginAsync.
    public Task<Either<Exception, Option<string>>> EnablePasswordLogin(
        string id,
        Name userName,
        string email,
        string temporaryPassword,
        CancellationToken cancellationToken)
        => SetPasswordAndIdentity(id, userName, email, temporaryPassword, allowRecreate: true, cancellationToken);

    // No recreate fallback here, deliberately: ResetPassword is only ever called once PasswordLoginEnabled is
    // already true (see IUsersService), which means EnablePasswordLogin already got this account past the
    // one-time recreate case below — there is nothing left for a reset to hit.
    public Task<Either<Exception, Unit>> ResetPassword(
        string id,
        string email,
        string temporaryPassword,
        CancellationToken cancellationToken)
        => SetPasswordAndIdentityWithoutRecreate(id, email, temporaryPassword, cancellationToken);

    private async Task<Either<Exception, Unit>> SetPasswordAndIdentityWithoutRecreate(
        string id,
        string email,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        var result = await SetPasswordAndIdentity(id, userName: null, email, temporaryPassword, allowRecreate: false, cancellationToken);
        return result.Match<Either<Exception, Unit>>(
            _ => Unit.Default,
            ex => ex);
    }

    // Shared by EnablePasswordLogin and ResetPassword — re-verifying/re-merging the identity shape on every
    // reset (rather than trusting the caller's PasswordLoginEnabled flag) costs one extra Graph GET but rules
    // out a whole class of "flag says enabled but Entra disagrees" drift, e.g. from a partially-failed earlier
    // enable or a manual Graph edit made outside this app.
    //
    // The success case is Option<string>, not a bare nullable string: LanguageExt's Either treats a `null`
    // reference-type Right value as its uninitialised Bottom state rather than a proper Right(null), which
    // made an early version of this method's "no recreation happened" case fail Match/MatchAsync downstream
    // instead of just carrying no id. Option<string>.None carries the same "nothing here" meaning without
    // ever putting a null inside the Either.
    private async Task<Either<Exception, Option<string>>> SetPasswordAndIdentity(
        string id,
        Name? userName,
        string email,
        string temporaryPassword,
        bool allowRecreate,
        CancellationToken cancellationToken)
    {
        var normalisedEmail = email.Trim().ToLowerInvariant();

        var identitiesResult = await BuildMergedIdentities(id, normalisedEmail, passwordLoginEnabled: true, cancellationToken);

        return await identitiesResult.MatchAsync(
            merged => ApplyPasswordAndIdentity(id, userName, normalisedEmail, merged, temporaryPassword, allowRecreate, cancellationToken),
            ex => ex);
    }

    private async Task<Either<Exception, Option<string>>> ApplyPasswordAndIdentity(
        string id,
        Name? userName,
        string normalisedEmail,
        MergedIdentities merged,
        string temporaryPassword,
        bool allowRecreate,
        CancellationToken cancellationToken)
    {
        // Graph refuses to add a non-federated (emailAddress) identity to an account whose existing
        // identities are all federated/social (or none at all) — confirmed by reproducing Graph's exact
        // response against a throwaway account shaped exactly like Add() produces: "Adding a non-federated
        // user signInType is not permitted if the user only has social/federated identities or no identities
        // set. Please recreate the user..." Surfaced by the PATCH below as a bare, generic "Insufficient
        // privileges" once identities/passwordProfile/passwordPolicies are all set together, which is why
        // this is checked proactively here instead of parsed out of that error afterward.
        //
        // Every account Add() creates has exactly this shape — federated/mail and nothing else — so this
        // fires on essentially every FIRST EnablePasswordLogin call for a given user, not just the rare
        // no-identity-at-all case (the original motivating report, BLUM — Zeyad Golita,
        // 2801402c-dfda-4837-9516-6fdc97d19011, which has no identity at all rather than a federated one, but
        // hits the same Graph restriction for the same reason: neither shape is "non-federated"). Only a
        // *prior* EnablePasswordLogin's emailAddress identity means this account is already past the
        // restriction — see MergedIdentities.HasEmailAddressIdentity.
        if (allowRecreate && userName is not null && !merged.HasEmailAddressIdentity)
        {
            return await RecreateWithPasswordLoginAsync(id, userName, normalisedEmail, temporaryPassword, cancellationToken);
        }

        var patchResult = await PatchIdentityAndPassword(id, merged.Identities, temporaryPassword, cancellationToken);

        return patchResult.Match<Either<Exception, Option<string>>>(
            _ => Option<string>.None,
            ex => ex);
    }

    private Task<Either<Exception, Unit>> PatchIdentityAndPassword(
        string id,
        List<ObjectIdentity> identities,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        var user = new User
        {
            Identities = identities,
            PasswordProfile = new PasswordProfile
            {
                ForceChangePasswordNextSignIn = true,
                Password = temporaryPassword,
            },
            PasswordPolicies = "DisablePasswordExpiration",
        };

        return CallGraphForNoResult(
            $"PATCH user {id} (password/identity)",
            () => _graphServiceClient.Users[id].PatchAsync(user, cancellationToken: cancellationToken),
            cancellationToken);
    }

    // Graph's own error for this precondition says the fix outright: delete the account and create a fresh
    // one — there is no PATCH that gets a federated-only/no-identity account into the emailAddress-identity
    // state. Goes straight to CreateWithPasswordLogin's emailAddress shape rather than Add()'s federated/mail
    // one, since this only ever runs as part of enabling password login — creating it passwordless first and
    // immediately converting it would just be two Graph calls doing what one already does.
    private async Task<Either<Exception, Option<string>>> RecreateWithPasswordLoginAsync(
        string id,
        Name userName,
        string normalisedEmail,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        var deleteResult = await HardDelete(id, cancellationToken);

        return await deleteResult.MatchAsync(
            _ => CreateWithPasswordLogin(userName, normalisedEmail, temporaryPassword, cancellationToken),
            ex => ex);
    }

    private Task<Either<Exception, Option<string>>> CreateWithPasswordLogin(
        Name userName,
        string normalisedEmail,
        string temporaryPassword,
        CancellationToken cancellationToken)
    {
        var user = new User
        {
            AccountEnabled = true,
            DisplayName = userName.ToString(),
            GivenName = userName.FirstName,
            Surname = userName.LastName,
            Mail = normalisedEmail,
            MailNickname = GenerateMailNickname(normalisedEmail),
            Identities =
            [
                new ObjectIdentity { SignInType = "emailAddress", Issuer = _domain, IssuerAssignedId = normalisedEmail },
            ],
            PasswordProfile = new PasswordProfile
            {
                ForceChangePasswordNextSignIn = true,
                Password = temporaryPassword,
            },
            PasswordPolicies = "DisablePasswordExpiration",
        };

        // The one call that names the address: by now HardDelete has removed the old account, so a failure
        // here leaves this person with no directory account at all, and the log has to say who.
        return CallGraph(
            $"POST user (recreate for password login, {normalisedEmail})",
            async () =>
            {
                var createdUser = await _graphServiceClient.Users.PostAsync(user, cancellationToken: cancellationToken);
                return Option<string>.Some(createdUser!.Id!);
            },
            cancellationToken);
    }

    // PATCH .../identities replaces the whole collection rather than merging it, so blindly overwriting it
    // would drop Entra's own auto-managed userPrincipalName entry (and any other identity not owned by this
    // app). GET the current identities first, keep everything this app doesn't own, and only replace the one
    // "login" identity it does.
    private Task<Either<Exception, MergedIdentities>> BuildMergedIdentities(
        string id,
        string normalisedEmail,
        bool passwordLoginEnabled,
        CancellationToken cancellationToken)
    {
        return CallGraph(
            $"GET user {id} (identities)",
            async () =>
            {
                var current = await _graphServiceClient.Users[id].GetAsync(
                    requestConfiguration => requestConfiguration.QueryParameters.Select = ["identities"],
                    cancellationToken);

                var currentIdentities = current?.Identities ?? new List<ObjectIdentity>();
                var hasEmailAddressIdentity = currentIdentities.Any(identity => identity.SignInType == "emailAddress");

                var preserved = currentIdentities
                    .Where(identity => !IsAppManagedLoginIdentity(identity))
                    .ToList();

                preserved.Add(passwordLoginEnabled
                    ? new ObjectIdentity { SignInType = "emailAddress", Issuer = _domain, IssuerAssignedId = normalisedEmail }
                    : new ObjectIdentity { SignInType = "federated", Issuer = "mail", IssuerAssignedId = normalisedEmail });

                return new MergedIdentities(preserved, hasEmailAddressIdentity);
            },
            cancellationToken);
    }

    // HasEmailAddressIdentity is deliberately narrower than "has any app-managed identity": Add()'s
    // federated/mail identity does NOT count, because Graph's own restriction treats a federated-only
    // account the same as one with no identity at all (see ApplyPasswordAndIdentity) — only a previous
    // EnablePasswordLogin's emailAddress identity means this account is already past that restriction.
    private record MergedIdentities(List<ObjectIdentity> Identities, bool HasEmailAddressIdentity);

    // The one "login" identity this app owns and rewrites — either shape it creates via Add/Update/
    // EnablePasswordLogin. Everything else on the object (in particular Entra's own userPrincipalName entry)
    // is preserved untouched by BuildMergedIdentities.
    private static bool IsAppManagedLoginIdentity(ObjectIdentity identity) =>
        (identity.SignInType == "federated" && identity.Issuer == "mail") || identity.SignInType == "emailAddress";

    public Task<Either<Exception, Unit>> Disable(string id, CancellationToken cancellationToken)
    {
        var user = new User { AccountEnabled = false };

        return CallGraphForNoResult(
            $"PATCH user {id} (disable)",
            () => _graphServiceClient.Users[id].PatchAsync(user, cancellationToken: cancellationToken),
            cancellationToken);
    }

    public Task<Either<Exception, Unit>> Enable(string id, CancellationToken cancellationToken)
    {
        var user = new User { AccountEnabled = true };

        return CallGraphForNoResult(
            $"PATCH user {id} (enable)",
            () => _graphServiceClient.Users[id].PatchAsync(user, cancellationToken: cancellationToken),
            cancellationToken);
    }

    public Task<Either<Exception, Unit>> HardDelete(string id, CancellationToken cancellationToken)
    {
        return CallGraphForNoResult(
            $"DELETE user {id}",
            async () =>
            {
                try
                {
                    await _graphServiceClient.Users[id].DeleteAsync(cancellationToken: cancellationToken);
                }
                catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
                {
                    // User already absent in Entra — treat as an idempotent success so the local DB row can still be removed.
                    _logger.LogWarning("Entra user {Id} not found on delete; treating as already removed", id);
                }
            },
            cancellationToken);
    }

    // For the calls whose answer is not used: a PATCH or DELETE that came back at all has succeeded.
    private Task<Either<Exception, Unit>> CallGraphForNoResult(string operation, Func<Task> call, CancellationToken cancellationToken) =>
        CallGraph(
            operation,
            async () =>
            {
                await call();
                return Unit.Default;
            },
            cancellationToken);

    // The one place that decides what a failed Graph call is: anything except the caller's own cancellation
    // comes back as a Left. Catching ODataError alone was not enough. That is what Graph's answer to a refused
    // request becomes, but a throttled request ends as an AggregateException ("Too many retries performed")
    // once the SDK's retry middleware gives up, and a transport or token failure (an expired client secret)
    // never reaches Graph at all — each of those escaped the handler as an unhandled exception.
    //
    // Error, not Warning, for the same reason: the unhandled-exception path is what raised an event for those
    // failures (BLUM-8048 was found that way), and a Left from here ends as a 500, or as a failure the handler
    // chooses to tolerate, with no other trace of why.
    //
    // A cancelled caller is left to throw. The bulk importer tells its per-user ceiling from an abandoned
    // request by which token fired, which it can only do if the cancellation reaches it.
    private async Task<Either<Exception, T>> CallGraph<T>(string operation, Func<Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Graph {Operation} failed: {ErrorMessage}", operation, (ex as ODataError)?.Error?.Message ?? ex.Message);
            return ex;
        }
    }

    private static string GenerateMailNickname(string email)
    {
        var localPart = email.Split('@')[0];
        return localPart.Replace(".", "-").Replace("+", "-").Replace("_", "-");
    }
}