namespace XperienceCommunity.ContentSyncToolkit.Inventory;

/// <summary>
/// Normalizes inventory timestamps to UTC. Xperience stores date and time values in the time zone
/// of the server the application runs on (confirmed live: the database holds server-local time and
/// the content query returns it with an unspecified kind), so two instances in different time zones
/// can only be compared after converting both to UTC.
/// </summary>
internal static class ContentInventoryTime
{
    /// <summary>
    /// Converts <paramref name="value"/> to UTC. An unspecified kind is taken as this server's local
    /// time: that's what Xperience's own queries return, and what a target on an older toolkit
    /// version sends (which then compares exactly as it did before, assuming both servers share a
    /// time zone).
    /// </summary>
    public static DateTime? ToUtc(DateTime? value) => value?.Kind switch
    {
        null => null,
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.Value.ToUniversalTime(),
        DateTimeKind.Unspecified or _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Local).ToUniversalTime(),
    };
}
