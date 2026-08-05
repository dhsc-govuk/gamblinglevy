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

    public async Task<Option<string>> FindByEmail(string email, CancellationToken cancellationToken)
    {
        try
        {
            var users = await _graphServiceClient.Users.GetAsync(
                requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = $"mail eq '{email}'";
                    requestConfiguration.QueryParameters.Select = ["id"];
                    requestConfiguration.QueryParameters.Top = 1;
                },
                cancellationToken);

            if (users is { Value: not null } && users.Value.Count != 0)
            {
                return users.Value.First().Id;
            }
        }
        catch (ServiceException ex)
        {
            if (ex.Message.ToLower().Contains($"resource '{email}' does not exist"))
            {
                return Option<string>.None;
            }

            throw;
        }

        return Option<string>.None;
    }

    public async Task<Either<Exception, string>> Add(
        Name userName,
        string email,
        bool isActive,
        CancellationToken cancellationToken)
    {
        try
        {
            var user = new User
            {
                AccountEnabled = isActive,
                DisplayName = userName.ToString(),
                GivenName = userName.FirstName,
                Surname = userName.LastName,
                Mail = email,
                MailNickname = GenerateMailNickname(email),
                Identities =
                [
                    new ObjectIdentity
                    {
                        SignInType = "federated",
                        Issuer = "mail",
                        IssuerAssignedId = email,
                    }
                ],
                PasswordProfile = new PasswordProfile
                {
                    ForceChangePasswordNextSignIn = false,
                    Password = _defaultUserPassword,
                },
            };

            var createdUser = await _graphServiceClient.Users.PostAsync(user, cancellationToken: cancellationToken);

            return createdUser!.Id!;
        }
        catch (ServiceException ex)
        {
            return ex;
        }
    }

    public async Task<Either<Exception, Unit>> Update(
        string id,
        Name userName,
        string email,
        CancellationToken cancellationToken)
    {
        var user = new User
        {
            GivenName = userName.FirstName,
            Surname = userName.LastName,
            DisplayName = userName.ToString(),
        };

        try
        {
            await _graphServiceClient.Users[id].PatchAsync(user, cancellationToken: cancellationToken);
            return Unit.Default;
        }
        catch (ServiceException ex)
        {
            return ex;
        }
    }

    public async Task<Either<Exception, Unit>> Disable(string id, CancellationToken cancellationToken)
    {
        var user = new User { AccountEnabled = false };
        try
        {
            await _graphServiceClient.Users[id].PatchAsync(user, cancellationToken: cancellationToken);
            return Unit.Default;
        }
        catch (ServiceException ex)
        {
            return ex;
        }
    }

    public async Task<Either<Exception, Unit>> Enable(string id, CancellationToken cancellationToken)
    {
        var user = new User { AccountEnabled = true };
        try
        {
            await _graphServiceClient.Users[id].PatchAsync(user, cancellationToken: cancellationToken);
            return Unit.Default;
        }
        catch (ServiceException ex)
        {
            return ex;
        }
    }

    public async Task<Either<Exception, Unit>> HardDelete(string id, CancellationToken cancellationToken)
    {
        try
        {
            await _graphServiceClient.Users[id].DeleteAsync(cancellationToken: cancellationToken);
            return Unit.Default;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
        {
            // User already absent in Entra — treat as an idempotent success so the local DB row can still be removed.
            _logger.LogWarning("Entra user {Id} not found on delete; treating as already removed", id);
            return Unit.Default;
        }
        catch (ODataError ex)
        {
            return ex;
        }
        catch (ServiceException ex)
        {
            return ex;
        }
    }

    public Task<Either<Exception, Unit>> UpdateRole(string id, UserRole role, CancellationToken cancellationToken)
    {
        // Stub: Entra app role assignment requires the infrastructure repo to declare the app role
        // and surface the assignment id. Until then this is a no-op so the command pipeline runs end-to-end.
        _logger.LogWarning("UpdateRole called for user {Id} → {Role} but Entra app role assignment is not yet wired up", id, role.ToString());
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    private static string GenerateMailNickname(string email)
    {
        var localPart = email.Split('@')[0];
        return localPart.Replace(".", "-").Replace("+", "-").Replace("_", "-");
    }
}