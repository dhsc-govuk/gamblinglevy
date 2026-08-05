using Application.Common.Interfaces.Queries;
using Domain.LocationUsers;
using Domain.OrganisationUsers;
using Domain.Users;

namespace Api.Dtos;

public record AdminTotalsResponse(
    int Admins,
    int Organisations);

public record UserDto(
    Guid? Id,
    string FirstName,
    string LastName,
    string Email,
    string Status,
    string? Role,
    string? OrganisationName)
{
    public static UserDto FromDomainModel(User input)
        => new(
            Id: input.Id.Value,
            FirstName: input.Name.FirstName,
            LastName: input.Name.LastName,
            Email: input.Email,
            Status: input.Status.ToString(),
            Role: input.Role.ToString(),
            OrganisationName: null);

    public static UserDto FromDomainModel(LocationUser input)
        => new(
            Id: input.Id.Value,
            FirstName: input.User!.Name.FirstName,
            LastName: input.User.Name.LastName,
            Email: input.User.Email,
            Status: input.User.Status.ToString(),
            Role: input.User.Role.ToString(),
            OrganisationName: null);

    public static UserDto FromDomainModel(OrganisationUser input)
        => new(
            Id: input.Id.Value,
            FirstName: input.User!.Name.FirstName,
            LastName: input.User.Name.LastName,
            Email: input.User.Email,
            Status: input.User.Status.ToString(),
            Role: input.User.Role.ToString(),
            OrganisationName: input.Organisation?.Name);

    public static UserDto FromUserListItem(UserListItem item)
        => new(
            Id: item.Id,
            FirstName: item.FirstName,
            LastName: item.LastName,
            Email: item.Email,
            Status: item.Status,
            Role: item.Role,
            OrganisationName: item.OrganisationName);
}

public record AccountHistoryEntryDto(string ActorName, DateTime OccurredAt);

public record UserDetailDto(
    Guid? Id,
    string FirstName,
    string LastName,
    string Email,
    string Status,
    string? Role,
    string? OrganisationName,
    Guid? OrganisationId,
    string MfaStatus,
    AccountHistoryEntryDto? CreatedBy,
    AccountHistoryEntryDto? LastUpdatedBy,
    AccountHistoryEntryDto? DeletedBy)
{
    public static UserDetailDto FromDetailItem(UserDetailItem item)
        => new(
            Id: item.Id,
            FirstName: item.FirstName,
            LastName: item.LastName,
            Email: item.Email,
            Status: item.Status,
            Role: item.Role,
            OrganisationName: item.OrganisationName,
            OrganisationId: item.OrganisationId,
            MfaStatus: item.MfaEnabled ? "enabled" : "not_set_up",
            CreatedBy: item.CreatedHistory != null
                ? new AccountHistoryEntryDto(item.CreatedHistory.ActorName, item.CreatedHistory.OccurredAt)
                : null,
            LastUpdatedBy: item.LastUpdatedHistory != null
                ? new AccountHistoryEntryDto(item.LastUpdatedHistory.ActorName, item.LastUpdatedHistory.OccurredAt)
                : null,
            DeletedBy: item.DeletedHistory != null
                ? new AccountHistoryEntryDto(item.DeletedHistory.ActorName, item.DeletedHistory.OccurredAt)
                : null);
}

public record CreateUserPayload(
    string FirstName,
    string LastName,
    string Email,
    bool? IsActive = null);

public record UpdateUserPayload(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    Guid? OrganisationId);

public record Profile(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Status,
    string Role,
    DateTime? UpdatedAt,
    Guid? OrganisationId,
    Guid? LocationId);

public record LocationAdminHomeDto(
    int Admins);

public record OrganisationAdminHomeDto(
    int Admins,
    int Locations);