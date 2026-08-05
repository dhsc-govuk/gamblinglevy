using Domain.Common;

namespace Domain.Organisations;

public class Organisation : IEntity<OrganisationId>
{
    // The width of the e_code column, shared by everything that has to agree with it: the EF configuration that
    // declares varchar(64), the create and update validators, and the bulk importer's per-row check. Those last
    // three used to carry their own literal 64, and the importer carried none at all — an over-long cell reached
    // the column, and the failed SaveChanges left the entity tracked on the shared DbContext so every later row
    // in the file failed too (BLUM-7050 review).
    public const int ECodeMaxLength = 64;

    public OrganisationId Id { get; private set; }
    public string Name { get; private set; }

    // Nullable by design: an ONS code is a Local Authority geography identifier, so VCSE organisations do not
    // have one. The "required" rule lives in the validators, conditioned on Type == LA (BLUM-7051).
    public string? OnsCode { get; private set; }
    public Region Region { get; private set; }
    public OrgType Type { get; private set; }

    // Required, unlike OnsCode above: every organisation receiving levy money has a DHSC finance identifier,
    // so BLUM-7050 makes it mandatory for both organisation types. e_code is NOT NULL in the database and the
    // organisations that predate the rule were backfilled with the "TBC" sentinel by the same migration, so
    // there is no legacy row this guard can lock out. The validators still carry their own NotEmpty rule —
    // they turn a missing value into a 400 with usable copy rather than letting it reach this guard.
    public string ECode { get; private set; }
    public OnboardingStatus OnboardingStatus { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Organisation(
        OrganisationId id,
        string name,
        string? onsCode,
        Region region,
        OrgType type,
        string eCode,
        OnboardingStatus onboardingStatus,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        Name = name;
        OnsCode = onsCode;
        Region = region;
        Type = type;
        ECode = eCode;
        OnboardingStatus = onboardingStatus;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static Organisation New(
        OrganisationId id,
        string name,
        string? onsCode,
        Region region,
        OrgType type,
        string eCode,
        OnboardingStatus onboardingStatus)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
        Guard.NotNull(region, nameof(region));
        Guard.NotNull(type, nameof(type));
        Guard.NotNullOrEmptyOrWhiteSpace(eCode, nameof(eCode));
        Guard.NotNull(onboardingStatus, nameof(onboardingStatus));

        return new Organisation(id, name, onsCode, region, type, eCode, onboardingStatus, DateTime.UtcNow, null);
    }

    public void UpdateDetails(
        string name,
        Region region,
        OrgType type,
        string eCode,
        string? onsCode)
    {
        Guard.NotNullOrEmptyOrWhiteSpace(name, nameof(name));
        Guard.NotNull(region, nameof(region));
        Guard.NotNull(type, nameof(type));
        Guard.NotNullOrEmptyOrWhiteSpace(eCode, nameof(eCode));

        Name = name;
        Region = region;
        Type = type;
        ECode = eCode;
        OnsCode = onsCode;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkActive()
    {
        OnboardingStatus = OnboardingStatus.Active;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkSuspended()
    {
        OnboardingStatus = OnboardingStatus.Suspended;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkInvited()
    {
        OnboardingStatus = OnboardingStatus.Invited;
        UpdatedAt = DateTime.UtcNow;
    }
}