using System.Text.Json;
using Domain.Users;

namespace Application.Users.Audit;

internal static class UserSnapshot
{
    public static string Serialize(User user)
    {
        return JsonSerializer.Serialize(new
        {
            id = user.Id.Value,
            email = user.Email,
            firstName = user.Name.FirstName,
            lastName = user.Name.LastName,
            role = user.Role.ToString(),
            status = user.Status.ToString(),
        });
    }
}