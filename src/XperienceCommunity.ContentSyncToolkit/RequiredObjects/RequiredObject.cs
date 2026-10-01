namespace XperienceCommunity.ContentSyncToolkit.RequiredObjects;

/// <summary>
/// The kinds of object Content Sync needs on the target but doesn't transfer. See
/// docs/specs/content-inventory-foundation.md, Required objects.
/// </summary>
public enum RequiredObjectKind
{
    ContentType,
    Language,
    WebsiteChannel,
    Workspace,
}

/// <summary>
/// One object Content Sync needs on the target, as listed by either instance.
/// </summary>
/// <param name="Kind">What kind of object it is.</param>
/// <param name="Guid">
/// Its GUID: Content Sync matches content types, channels and workspaces by GUID, so an object
/// recreated by hand with the same code name doesn't match. Languages are matched by code name only.
/// </param>
/// <param name="Name">Its code name.</param>
/// <param name="DisplayName">Its display name, for messages.</param>
public sealed record RequiredObject(RequiredObjectKind Kind, Guid Guid, string Name, string DisplayName)
{
    /// <summary>
    /// For content types, a SHA-256 hash of the type's field definition, so a type whose fields
    /// differ between the instances is detected; <see langword="null"/> for other kinds.
    /// </summary>
    public string? DefinitionHash { get; init; }
}

public enum RequiredObjectProblem
{
    /// <summary>The target has no object with this GUID (languages: this code name) or code name.</summary>
    MissingOnTarget,

    /// <summary>The target has an object with this code name but a different GUID, e.g. one recreated by hand.</summary>
    DifferentGuidOnTarget,

    /// <summary>The target's content type has the same GUID but a different field definition.</summary>
    DefinitionDiffers,
}

/// <summary>A local object that stops Content Sync from syncing the items that use it.</summary>
/// <param name="Object">The object as it is on this instance.</param>
/// <param name="Problem">How the target's copy is missing or different.</param>
public sealed record RequiredObjectIssue(RequiredObject Object, RequiredObjectProblem Problem);

/// <summary>
/// The result of comparing this instance's required objects with the target's. When
/// <see cref="Checked"/> is <see langword="false"/> (the target couldn't be asked, or runs a
/// toolkit version without the required-objects endpoint), <see cref="Issues"/> is empty and means
/// nothing.
/// </summary>
public sealed record RequiredObjectsCheckResult(bool Checked, IReadOnlyList<RequiredObjectIssue> Issues)
{
    public static RequiredObjectsCheckResult NotChecked { get; } = new(false, []);
}
