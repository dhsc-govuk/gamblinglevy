namespace Api.Dtos;

public record LocationDto(
    Guid? Id,
    Guid? OrganisationId,
    string Name,
    bool IsActive,
    OrganisationDto? Organisation = null);

public record LocationHomeDto(
    string LocationName,
    string OrganisationName,
    int Admins);