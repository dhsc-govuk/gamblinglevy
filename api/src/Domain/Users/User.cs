using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Users;

public class User : IEntity<UserId>
{
    public UserId Id { get; private set; }
    public Name Name { get; private set; }
    public string Email { get; private set; }
    public SubId SubId { get; private set; }
    public UserStatus Status { get; private set; }
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private User(
        UserId id,
        Name name,
        string email,
        SubId subId,
        UserStatus status,
        UserRole role,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        Id = id;
        Name = name;
        Email = email;
        SubId = subId;
        Status = status;
        Role = role;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static User New(UserId id, Name name, string email, SubId subId, UserStatus status, UserRole role)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(name, nameof(name));
        Guard.NotNullOrEmpty(email, nameof(email));
        Guard.NotNull(subId, nameof(subId));
        Guard.NotNull(status, nameof(status));
        Guard.NotNull(role, nameof(role));

        return new User(id, name, email, subId, status, role, DateTime.UtcNow, null);
    }

    public void UpdateDetails(Name name, string email, UserRole? role = null)
    {
        Guard.NotNull(name, nameof(name));
        Guard.NotNullOrEmpty(email, nameof(email));

        Name = name;
        Email = email;
        Role = role ?? Role;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        Status = UserStatus.Deactivated;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reactivate()
    {
        Status = UserStatus.Active;
        UpdatedAt = DateTime.UtcNow;
    }
}