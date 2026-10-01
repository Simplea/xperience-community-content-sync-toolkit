using XperienceCommunity.ContentSyncToolkit.Inventory;

namespace XperienceCommunity.ContentSyncToolkit.RequiredObjects;

/// <summary>
/// Pure comparison of this instance's required objects with the target's, kept free of Xperience
/// dependencies like <c>ContentSyncStatusComparer</c>. Follows Content Sync's own matching: content
/// types, channels and workspaces by GUID, languages by code name.
/// </summary>
internal static class RequiredObjectsComparer
{
    public static IReadOnlyList<RequiredObjectIssue> Compare(
        IReadOnlyList<RequiredObject> local, IReadOnlyList<RequiredObject> remote)
    {
        var issues = new List<RequiredObjectIssue>();

        foreach (var localObject in local)
        {
            var problem = ProblemOf(localObject, [.. remote.Where(remoteObject => remoteObject.Kind == localObject.Kind)]);
            if (problem is not null)
            {
                issues.Add(new RequiredObjectIssue(localObject, problem.Value));
            }
        }

        return issues;
    }

    // The issues that stop an item from syncing: its content type, its language, and the channel
    // (pages) or workspace (content hub items) it belongs to. Linked items are synced along with it
    // and can need more, which this doesn't see.
    public static IReadOnlyList<RequiredObjectIssue> IssuesFor(ContentInventoryItem item, IReadOnlyList<RequiredObjectIssue> issues)
    {
        var scopeKind = item.Kind == ContentInventoryItemKind.WebPage ? RequiredObjectKind.WebsiteChannel : RequiredObjectKind.Workspace;

        return [.. issues.Where(issue =>
            (issue.Object.Kind == RequiredObjectKind.ContentType && SameName(issue.Object.Name, item.ContentTypeName))
            || (issue.Object.Kind == RequiredObjectKind.Language && SameName(issue.Object.Name, item.LanguageName))
            || (issue.Object.Kind == scopeKind && SameName(issue.Object.Name, item.ScopeName)))];
    }

    private static RequiredObjectProblem? ProblemOf(RequiredObject localObject, IReadOnlyList<RequiredObject> remoteOfKind)
    {
        if (localObject.Kind == RequiredObjectKind.Language)
        {
            return remoteOfKind.Any(remoteObject => SameName(remoteObject.Name, localObject.Name))
                ? null
                : RequiredObjectProblem.MissingOnTarget;
        }

        var match = remoteOfKind.FirstOrDefault(remoteObject => remoteObject.Guid == localObject.Guid);
        if (match is null)
        {
            return remoteOfKind.Any(remoteObject => SameName(remoteObject.Name, localObject.Name))
                ? RequiredObjectProblem.DifferentGuidOnTarget
                : RequiredObjectProblem.MissingOnTarget;
        }

        // Only compared when both sides sent a hash.
        bool definitionDiffers = localObject.DefinitionHash is not null
            && match.DefinitionHash is not null
            && !string.Equals(localObject.DefinitionHash, match.DefinitionHash, StringComparison.Ordinal);

        return definitionDiffers ? RequiredObjectProblem.DefinitionDiffers : null;
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
