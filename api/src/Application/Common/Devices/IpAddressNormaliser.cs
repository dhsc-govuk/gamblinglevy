using System.Net;

namespace Application.Common.Devices;

/// <summary>
/// Turns whatever arrived in a proxy header into something safe to store and show.
///
/// The client IP is read from X-Azure-ClientIP or X-Forwarded-For, both of which any caller can set to any
/// string at all. Since BLUM-7241 that value is persisted in a varchar(45) column, so an over-long header
/// would fail the INSERT and take the whole MFA verification down with it — a trivially reachable
/// denial of sign-in. Legitimate traffic can trip the same wire: proxies routinely append a comma-separated
/// chain, and IPv6 arrives bracketed and with a port.
///
/// So nothing is trusted through: a candidate has to parse as an IP address to be kept, and anything else
/// becomes "unknown" rather than being stored raw or truncated into a plausible-looking lie.
/// </summary>
public static class IpAddressNormaliser
{
    public const string Unknown = "unknown";

    // The longest possible IPv6 literal ("ffff:…:255.255.255.255") is 45 characters, which is where the
    // column width comes from. A scope id can push ToString() past that, hence the final length check.
    private const int MaxLength = 45;

    public static string Normalise(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Unknown;
        }

        var value = candidate.Trim();

        // A forwarding chain names the original client first; the rest are the proxies that relayed it.
        var separator = value.IndexOf(',');
        if (separator >= 0)
        {
            value = value[..separator].Trim();
        }

        value = StripPort(value);

        if (!IPAddress.TryParse(value, out var parsed))
        {
            return Unknown;
        }

        // Re-rendered from the parsed address rather than echoed back, so what is stored and shown is the
        // canonical form and never an attacker's choice of spelling.
        var normalised = parsed.ToString();

        return normalised.Length <= MaxLength ? normalised : Unknown;
    }

    private static string StripPort(string value)
    {
        // "[2001:db8::1]:443" — brackets exist precisely to tell the address apart from the port.
        if (value.StartsWith('['))
        {
            var closing = value.IndexOf(']');
            return closing > 0 ? value[1..closing] : value;
        }

        // "203.0.113.10:443". A bare IPv6 also contains colons, so only strip when there is exactly one —
        // which cannot be an IPv6 address.
        var firstColon = value.IndexOf(':');
        if (firstColon > 0 && value.IndexOf(':', firstColon + 1) < 0)
        {
            return value[..firstColon];
        }

        return value;
    }
}