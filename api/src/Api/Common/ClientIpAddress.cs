using Application.Common.Devices;

namespace Api.Common;

/// <summary>
/// Works out which IP a request came from, in one place.
///
/// The resolution order used to be spelled out privately in both MfaController and MfaRequiredMiddleware.
/// They have to agree exactly — the value is hashed into the session's device binding, so if the two ever
/// disagreed about which header wins, every session would be minted under one fingerprint and judged against
/// another, and nobody would stay signed in.
///
/// This deliberately returns the address as the client stated it, NOT a canonicalised form. The value is fed
/// straight into the device-binding hash, and every session already in the database was bound using the
/// unaltered string; canonicalising here would change the hash for anything that does not round-trip
/// byte-identically and throw every signed-in user back to the code-entry screen the moment this deployed.
/// Making the value safe to store and show is a separate job, done once at the point it is persisted —
/// see IpAddressNormaliser and MfaSessionService.CreateSessionAsync.
/// </summary>
public static class ClientIpAddress
{
    public static string Resolve(HttpRequest request)
    {
        var azureClientIp = request.Headers["X-Azure-ClientIP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(azureClientIp))
        {
            return azureClientIp.Trim();
        }

        var forwardedFor = request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // A forwarding chain names the original client first; the rest are the proxies that relayed it.
            return forwardedFor.Split(',')[0].Trim();
        }

        return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? IpAddressNormaliser.Unknown;
    }
}