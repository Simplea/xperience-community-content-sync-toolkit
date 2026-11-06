namespace XperienceCommunity.ContentSyncToolkit.RequiredObjects;

/// <summary>
/// Which content items link to which, in one language, by content item ID. Content Sync syncs an item
/// together with everything it links to, however deep, so a linked item's content type can stop a sync
/// just as the item's own can. Pure, like <see cref="RequiredObjectsComparer"/>.
/// </summary>
/// <param name="ItemIds">The content item ID of each inventory GUID (<c>WebPageItemGUID</c> or <c>ContentItemGUID</c>).</param>
/// <param name="ContentTypes">The content type code name of each content item.</param>
/// <param name="Links">The content items each content item links to.</param>
internal sealed record ContentLinkGraph(
    IReadOnlyDictionary<Guid, int> ItemIds,
    IReadOnlyDictionary<int, string> ContentTypes,
    IReadOnlyDictionary<int, IReadOnlyCollection<int>> Links)
{
    public static ContentLinkGraph Empty { get; } = new(
        new Dictionary<Guid, int>(), new Dictionary<int, string>(), new Dictionary<int, IReadOnlyCollection<int>>());

    /// <summary>
    /// For each item that links, directly or not, to an item of the given content type: the nearest
    /// such item. Walks the links backwards from the items of that type, so its cost depends on the
    /// links, not on how many items are checked; links can form cycles.
    /// </summary>
    public IReadOnlyDictionary<int, int> FindItemsLinkingTo(string contentTypeName)
    {
        var linkedFrom = new Dictionary<int, List<int>>();
        foreach (var (source, targets) in Links)
        {
            foreach (int target in targets)
            {
                if (!linkedFrom.TryGetValue(target, out var sources))
                {
                    linkedFrom[target] = sources = [];
                }

                sources.Add(source);
            }
        }

        var found = new Dictionary<int, int>();
        var visited = new HashSet<int>();
        var pending = new Queue<(int ItemId, int Cause)>();

        foreach (var (itemId, typeName) in ContentTypes)
        {
            if (string.Equals(typeName, contentTypeName, StringComparison.OrdinalIgnoreCase))
            {
                visited.Add(itemId);
                pending.Enqueue((itemId, itemId));
            }
        }

        while (pending.TryDequeue(out var current))
        {
            foreach (int source in (linkedFrom.GetValueOrDefault(current.ItemId) ?? []).Where(visited.Add))
            {
                found[source] = current.Cause;
                pending.Enqueue((source, current.Cause));
            }
        }

        return found;
    }
}
