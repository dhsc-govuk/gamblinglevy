using Domain.Users;
using Domain.ValueObjects;

namespace Tests.Data;

public static class AdminsData
{
    public static readonly User MainAdmin = User.New(
        id: UserId.New(),
        name: new Name("Main", "Admin"),
        email: "main.admin@email.com",
        subId: new SubId(Guid.NewGuid()),
        status: UserStatus.Active,
        role: UserRole.Admin,
        utcNow: DateTime.UtcNow);

    public static readonly User SecondAdmin = User.New(
        id: UserId.New(),
        name: new Name("Second", "Admin"),
        email: "second.admin@email.com",
        subId: new SubId(Guid.NewGuid()),
        status: UserStatus.Active,
        role: UserRole.Admin,
        utcNow: DateTime.UtcNow);

    public static readonly User DeactivatedAdmin = User.New(
        id: UserId.New(),
        name: new Name("Deactivated", "Admin"),
        email: "deactivated.admin@email.com",
        subId: new SubId(Guid.NewGuid()),
        status: UserStatus.Deactivated,
        role: UserRole.Admin,
        utcNow: DateTime.UtcNow);
}