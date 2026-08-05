using Domain.Locations;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using Domain.ValueObjects;
using LanguageExt;

namespace Domain;

public class UserProfile
{
    public UserId? Id { get; private set; }
    public Name? Name { get; private set; }
    public string? Email { get; private set; }
    public UserStatus? Status { get; private set; }
    public UserRole? Role { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Option<OrganisationId> OrganisationId { get; private set; }
    public Option<LocationId> LocationId { get; private set; }
    public Option<OnboardingStatus> OrganisationStatus { get; private set; }

    private static UserProfile New(
        UserId id,
        Name name,
        string email,
        UserStatus status,
        UserRole role,
        DateTime? updatedAt,
        Option<OrganisationId> organisationId,
        Option<LocationId> locationId,
        Option<OnboardingStatus> organisationStatus)
    {
        return new UserProfile
        {
            Id = id,
            Name = name,
            Email = email,
            Status = status,
            Role = role,
            UpdatedAt = updatedAt,
            OrganisationId = organisationId,
            LocationId = locationId,
            OrganisationStatus = organisationStatus
        };
    }

    public static UserProfile NewAdminProfile(User user)
    {
        return New(
            user.Id,
            user.Name,
            user.Email,
            user.Status,
            user.Role,
            user.UpdatedAt,
            Option<OrganisationId>.None,
            Option<LocationId>.None,
            Option<OnboardingStatus>.None);
    }

    // Serves every LA role, not just organisation admin — see UserRole.IsOrganisationMember().
    public static UserProfile NewOrganisationMemberProfile(OrganisationUser user)
    {
        return New(
            user.User!.Id,
            user.User.Name,
            user.User.Email,
            user.User.Status,
            user.User.Role,
            user.User.UpdatedAt,
            user.OrganisationId,
            Option<LocationId>.None,
            OrganisationStatusOf(user.Organisation));
    }

    private static Option<OnboardingStatus> OrganisationStatusOf(Organisation? organisation)
    {
        return organisation is null
            ? Option<OnboardingStatus>.None
            : Option<OnboardingStatus>.Some(organisation.OnboardingStatus);
    }
}