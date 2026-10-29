using System.Text.Json;
using System.Text.Json.Nodes;
using Domain.Users;

namespace Application.Users.Audit;

internal static class UserSnapshot
{
    public static string Serialize(User user)
    {
        return JsonSerializer.Serialize(Shape(user));
    }

    // The same snapshot plus the event the deactivation belongs to (BLUM-7667 AC3). An overload rather than an
    // optional argument, so the rows every other caller writes don't start carrying "cause": null.
    public static string Serialize(User user, UserDeactivationCause cause)
    {
        var snapshot = JsonSerializer.SerializeToNode(Shape(user))!.AsObject();
        snapshot["cause"] = new JsonObject
        {
            ["action"] = cause.Action,
            ["organisationId"] = cause.OrganisationId.Value,
        };
        return snapshot.ToJsonString();
    }

    private static object Shape(User user)
    {
        return new
        {
            id = user.Id.Value,
            email = user.Email,
            firstName = user.Name.FirstName,
            lastName = user.Name.LastName,
            role = user.Role.ToString(),
            status = user.Status.ToString(),
            passwordLoginEnabled = user.PasswordLoginEnabled,
        };
    }
}