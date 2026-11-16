namespace Alveara.Api.Architecture.Time;

/// <summary>
/// ALV-N021 A3 (briefing section 4.1-1 and 4.1-2): the server-owned identifiers of the practice time zone.
/// <para>
/// The API publishes ONE client-neutral identifier, the canonical <b>IANA</b> id (<c>America/Chicago</c>), which browser <c>Intl</c> supports. The configured
/// <c>PracticeTimeZone</c> may be a Windows id (<c>Central Standard Time</c>, which <see cref="TimeZoneInfo.FindSystemTimeZoneById"/> accepts on Windows); it is
/// converted here, and a zone that has no IANA form is refused at start-up rather than published as a platform-specific value.
/// </para>
/// <para>
/// A future Windows/WPF client whose native time-zone APIs need a Windows id gets it from <see cref="TryWindowsId"/> on the server; no client maps, selects,
/// infers or stores the practice zone itself. Only the IANA id is published by A3.
/// </para>
/// </summary>
public static class PracticeZoneIds
{
    /// <summary>The canonical IANA id of <paramref name="zone"/>; throws <see cref="PracticeZoneConfigurationException"/> when it has none.</summary>
    public static string CanonicalIanaId(TimeZoneInfo zone)
    {
        if (zone.HasIanaId) return zone.Id;
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) && !string.IsNullOrEmpty(iana)) return iana;
        throw new PracticeZoneConfigurationException(zone.Id);
    }

    /// <summary>The Windows id for an IANA id (for a future Windows client); false when the platform knows no mapping.</summary>
    public static bool TryWindowsId(string ianaId, out string? windowsId)
    {
        windowsId = null;
        if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out var w) || string.IsNullOrEmpty(w)) return false;
        windowsId = w;
        return true;
    }
}

/// <summary>The configured practice time zone has no IANA identifier, so it cannot be published to clients; the API refuses to start.</summary>
public sealed class PracticeZoneConfigurationException(string configuredId)
    : Exception($"The practice time zone '{configuredId}' has no IANA identifier, so clients cannot be told which zone to show times in. Configure PracticeTimeZone with an IANA id such as America/Chicago.")
{
    public string ConfiguredId { get; } = configuredId;
}
