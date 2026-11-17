using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// Who last modified each item here: the Modified by column and the Only my changes filter.
public class OnlyMyChangesTests
{
    private static readonly IReadOnlyList<ContentSyncScope> scopes = [new(7, "First", "First channel")];

    private static readonly IReadOnlyList<ContentSyncLanguage> languages =
    [
        new("es", "Spanish", false),
        new("en", "English", true),
    ];

    private static readonly ContentSyncModifier me = new("Global Administrator", IsCurrentUser: true);
    private static readonly ContentSyncModifier ana = new("Ana Editor", IsCurrentUser: false);

    private static ContentInventoryItem Inventory(Guid guid, string languageName, string path) =>
        new(guid, ContentInventoryItemKind.WebPage, "Test.Type", "First", languageName, path, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Published");

    private static ContentSyncStatusItem Local(Guid guid, string languageName = "en", ContentSyncStatus status = ContentSyncStatus.Changed, string path = "/A") =>
        new(guid, status, Inventory(guid, languageName, path), Inventory(guid, languageName, path));

    private static ContentSyncStatusItem OnlyOnTarget(Guid guid, string languageName = "en") =>
        new(guid, ContentSyncStatus.OnlyOnTarget, null, Inventory(guid, languageName, "/Target"));

    private readonly List<(int ScopeId, string LanguageName)> lookups = [];

    [SetUp]
    public void ResetLookups() => lookups.Clear();

    private Task<ContentSyncStatusView> Build(
        bool onlyMyChanges,
        Func<string, IReadOnlyList<ContentSyncStatusItem>> items,
        Func<string, Dictionary<Guid, ContentSyncModifier>> modifiers,
        string? languageName = null,
        bool showInSync = true,
        string? sortBy = null,
        bool sortDescending = false) =>
        ContentSyncStatusViewBuilder.BuildAsync(
            new ContentSyncStatusViewRequest(
                true, null, languageName, false, new ContentSyncStatusFilter(ShowInSync: showInSync, OnlyMyChanges: onlyMyChanges),
                null, sortBy, sortDescending, 50, 0),
            _ => Task.FromResult(scopes),
            _ => Task.FromResult(languages),
            (_, language, _, _) => Task.FromResult(new ContentSyncStatusResult(true, items(language))),
            (scope, language, _) =>
            {
                lookups.Add((scope.Id, language));
                return Task.FromResult<IReadOnlyDictionary<Guid, ContentSyncModifier>>(modifiers(language));
            },
            CancellationToken.None);

    [Test]
    public async Task OnlyMyChanges_ListsOnlyTheUsersItems()
    {
        Guid mine = Guid.NewGuid(), theirs = Guid.NewGuid();

        var view = await Build(true, _ => [Local(mine), Local(theirs)], _ => new() { [mine] = me, [theirs] = ana }, "en");

        Assert.That(view.Items.Select(item => item.Guid), Is.EqualTo(new[] { mine }));
        Assert.That(lookups, Is.EqualTo(new[] { (7, "en") }));
    }

    // Everyone's changes by default.
    [Test]
    public async Task ByDefault_ListsEveryonesItems()
    {
        Guid mine = Guid.NewGuid(), theirs = Guid.NewGuid();

        var view = await Build(false, _ => [Local(mine), Local(theirs)], _ => new() { [mine] = me, [theirs] = ana }, "en");

        Assert.That(view.Items, Has.Count.EqualTo(2));
    }

    // It has no copy here, so it has no modifier here and can't be the user's.
    [Test]
    public async Task OnlyOnTarget_HasNoModifier_AndNeverMatches()
    {
        var guid = Guid.NewGuid();

        var all = await Build(false, _ => [OnlyOnTarget(guid)], _ => new() { [guid] = me }, "en");
        var mine = await Build(true, _ => [OnlyOnTarget(guid)], _ => new() { [guid] = me }, "en");

        Assert.That(all.ModifierOf(all.Items.Single()), Is.Null);
        Assert.That(mine.Items, Is.Empty);
    }

    // Each language version has its own last modifier.
    [Test]
    public async Task AllLanguages_AreMatchedPerLanguage()
    {
        var guid = Guid.NewGuid();

        var view = await Build(
            true,
            language => [Local(guid, language)],
            language => new() { [guid] = language == "es" ? me : ana });

        Assert.That(view.Items.Select(ContentSyncStatusListingSupport.LanguageName), Is.EqualTo(new[] { "es" }));
        Assert.That(lookups.Select(lookup => lookup.LanguageName), Is.EqualTo(new[] { "es", "en" }));
    }

    [Test]
    public async Task ModifierOf_NamesWhoLastEditedEachLanguageVersion()
    {
        var guid = Guid.NewGuid();

        var view = await Build(
            false,
            language => [Local(guid, language)],
            language => new() { [guid] = language == "es" ? me : ana });

        Assert.That(
            view.Items.Select(item => $"{ContentSyncStatusListingSupport.LanguageName(item)} {view.ModifierOf(item)?.Name}"),
            Is.EquivalentTo(new[] { "es Global Administrator", "en Ana Editor" }));
    }

    [Test]
    public async Task SortByModifiedBy_SortsByName()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();

        var view = await Build(
            false,
            _ => [Local(second, path: "/A"), Local(first, path: "/B")],
            _ => new() { [first] = ana, [second] = me },
            "en",
            sortBy: ContentSyncStatusListingSupport.ModifiedByColumn);

        Assert.That(view.Items.Select(item => item.Guid), Is.EqualTo(new[] { first, second }));
    }

    // In sync items still follow Show items in sync.
    [Test]
    public async Task MyItemsInSync_AreHiddenByDefault()
    {
        Guid synced = Guid.NewGuid(), pending = Guid.NewGuid();

        var view = await Build(
            true,
            _ => [Local(synced, status: ContentSyncStatus.InSync), Local(pending)],
            _ => new() { [synced] = me, [pending] = me },
            "en",
            showInSync: false);

        Assert.That(view.Items.Select(item => item.Guid), Is.EqualTo(new[] { pending }));
    }
}
