namespace Application.Common.Devices;

/// <summary>
/// Turns a User-Agent header into the short description shown on a user's own sessions list
/// ("Chrome on Windows"). It exists so the raw header never has to be stored: the label is resolved once,
/// when the session is created, and the header is discarded.
///
/// This is a recognition aid, not an identity. It answers "was that me, on my work laptop?" and nothing
/// more — the device binding is judged on the SHA-256 hashes, never on this string. Being wrong about an
/// unusual browser therefore costs a vaguer label, not a security decision, which is why crude substring
/// matching is preferred here over taking on a User-Agent parsing dependency.
/// </summary>
public static class DeviceLabelResolver
{
    public const string UnknownDevice = "Unknown device";

    // Order is load-bearing. Every Chromium browser also says "Chrome", and Chrome itself also says
    // "Safari", so the more specific tokens have to be tested before the ones they impersonate.
    private static readonly (string Token, string Name)[] Browsers =
    [
        ("Edg/", "Edge"),
        ("OPR/", "Opera"),
        ("SamsungBrowser/", "Samsung Internet"),
        ("Chrome/", "Chrome"),
        ("Firefox/", "Firefox"),
        ("Safari/", "Safari"),
        ("Trident/", "Internet Explorer"),
        ("MSIE ", "Internet Explorer"),
    ];

    private static readonly (string Token, string Name)[] Platforms =
    [
        ("iPhone", "iPhone"),
        ("iPad", "iPad"),
        ("Android", "Android"),
        ("Windows NT", "Windows"),
        ("Macintosh", "macOS"),
        ("Mac OS X", "macOS"),
        ("CrOS", "ChromeOS"),
        ("Linux", "Linux"),
    ];

    public static string Resolve(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return UnknownDevice;
        }

        var browser = Match(userAgent, Browsers);
        var platform = Match(userAgent, Platforms);

        if (browser is not null && platform is not null)
        {
            return $"{browser} on {platform}";
        }

        return browser ?? platform ?? UnknownDevice;
    }

    private static string? Match(string userAgent, (string Token, string Name)[] candidates)
    {
        foreach (var (token, name) in candidates)
        {
            if (userAgent.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }
}