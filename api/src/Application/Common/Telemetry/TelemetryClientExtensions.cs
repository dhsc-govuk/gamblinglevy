using Microsoft.ApplicationInsights;

namespace Application.Common.Telemetry;

public static class TelemetryClientExtensions
{
    // Every custom-event call site should go through here rather than TelemetryClient.TrackEvent directly: with no
    // ApplicationInsights:ConnectionString configured (dev/test, or before the resource is provisioned), the SDK
    // still runs the full pipeline for an unconfigured client, it just has nowhere to send to. Checking the
    // connection string once, centrally, avoids that pointless work everywhere events are fired.
    public static void TrackEventIfEnabled(this TelemetryClient telemetryClient, string eventName, IDictionary<string, string>? properties = null)
    {
        if (string.IsNullOrEmpty(telemetryClient.TelemetryConfiguration.ConnectionString))
        {
            return;
        }

        telemetryClient.TrackEvent(eventName, properties);
    }
}