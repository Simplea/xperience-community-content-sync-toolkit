using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// The Language filter, which lists every language when nothing is selected, and the Language column.
public class AllLanguagesTests
{
    private static readonly IReadOnlyList<ContentSyncScope> scopes = [new(1, "First", "First channel")];

    private static readonly IReadOnlyList<ContentSyncLanguage> languages =
    [
        new("es", "Spanish", false),
        new("en", "English", true),
    ];

    private static readonly Guid sharedGuid = Guid.NewGuid();

    private static ContentSyncStatusItem Item(string languageName, ContentSyncStatus status = ContentSyncStatus.Changed, string name = "/A", Guid? guid = null)
    {
        var id = guid ?? Guid.NewGuid();
        return new(id, status,
            new ContentInventoryItem(id, ContentInventoryItemKind.WebPage, "Test.Type", "First", languageName, name, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Published"),
            null);
    }

    private static ContentSyncStatusViewRequest Request(string? languageName) =>
        new(true, null, languageName, false, new ContentSyncStatusFilter(ShowInSync: true), null, null, false, 50, 0);

    private static Task<ContentSyncStatusView> Build(
        string? languageName, List<string> calls, Func<string, ContentSyncStatusResult>? result = null) =>
        ContentSyncStatusViewBuilder.BuildAsync(
            Request(languageName),
            _ => Task.FromResult(scopes),
            _ => Task.FromResult(languages),
            (_, language, _, _) =>
            {
                calls.Add(language);
                return Task.FromResult(result?.Invoke(language) ?? new ContentSyncStatusResult(true, [Item(language, guid: sharedGuid)]));
            },
            CancellationToken.None);

    [Test]
    public async Task NoLanguageSelected_ComparesEachLanguage_AndListsTheirItemsTogether()
    {
        List<string> calls = [];

        var view = await Build(null, calls);

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(new[] { "es", "en" }));
            Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.Items));
            Assert.That(view.Items.Select(ContentSyncStatusListingSupport.LanguageName), Is.EquivalentTo(new[] { "es", "en" }));
            Assert.That(view.LanguageName, Is.Null);
            Assert.That(view.Languages, Is.EqualTo(languages));
        });
    }

    // One language's missing answer would make its items look in sync or missing.
    [Test]
    public async Task AllLanguages_WhenTheTargetFailsForOneLanguage_IsUnavailable()
    {
        var view = await Build(
            null,
            [],
            language => language == "en" ? new ContentSyncStatusResult(false, []) : new ContentSyncStatusResult(true, [Item(language)]));

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.TargetUnavailable));
    }

    [Test]
    public async Task OneLanguage_ComparesOnlyThatLanguage()
    {
        List<string> calls = [];

        var view = await Build("es", calls);

        Assert.That(calls, Is.EqualTo(new[] { "es" }));
        Assert.That(view.LanguageName, Is.EqualTo("es"));
    }

    // A language deleted after it was selected isn't silently replaced by every language.
    [Test]
    public async Task ALanguageThatNoLongerExists_IsNotFound()
    {
        List<string> calls = [];

        var view = await Build("fr", calls);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.LanguageNotFound));
        Assert.That(calls, Is.Empty);
    }

    [Test]
    public void IsAllLanguages_WhenNothingIsSelected()
    {
        Assert.That(ContentSyncStatusListingSupport.IsAllLanguages(null), Is.True);
        Assert.That(ContentSyncStatusListingSupport.IsAllLanguages(string.Empty), Is.True);
        Assert.That(ContentSyncStatusListingSupport.IsAllLanguages("en"), Is.False);
    }

    [TestCase(1, false)]
    [TestCase(2, true)]
    public void ShowsLanguageColumn_OnlyWithSeveralLanguages(int count, bool expected) =>
        Assert.That(ContentSyncStatusListingSupport.ShowsLanguageColumn(count), Is.EqualTo(expected));

    [Test]
    public void SortByLanguage_UsesTheDisplayedNames()
    {
        // "es" sorts before "fr" by code name, but Spanish after French.
        IReadOnlyList<ContentSyncStatusItem> items = [Item("es"), Item("fr")];
        var names = new Dictionary<string, string> { ["es"] = "Spanish", ["fr"] = "French" };

        var sorted = ContentSyncStatusListingSupport.ApplySort(items, ContentSyncStatusListingSupport.LanguageColumn, false, names);

        Assert.That(sorted.Select(ContentSyncStatusListingSupport.LanguageName), Is.EqualTo(new[] { "fr", "es" }));
    }

    // An item's languages stay next to each other when everything else ties.
    [Test]
    public void DefaultSort_KeepsAnItemsLanguagesTogether_InLanguageOrder()
    {
        var guid = Guid.NewGuid();
        IReadOnlyList<ContentSyncStatusItem> items = [Item("es", name: "/A", guid: guid), Item("en", name: "/B"), Item("en", name: "/A", guid: guid)];
        var names = new Dictionary<string, string> { ["es"] = "Spanish", ["en"] = "English" };

        var sorted = ContentSyncStatusListingSupport.ApplySort(items, null, false, names);

        Assert.That(
            sorted.Select(item => $"{ContentSyncStatusListingSupport.DisplayName(item)} {ContentSyncStatusListingSupport.LanguageName(item)}"),
            Is.EqualTo(new[] { "/A en", "/A es", "/B en" }));
    }

    [Test]
    public void LanguageDisplayName_FallsBackToTheCodeName() =>
        Assert.That(ContentSyncStatusListingSupport.LanguageDisplayName(Item("de"), new Dictionary<string, string>()), Is.EqualTo("de"));

    // All languages is the empty selection, not an option.
    [Test]
    public async Task LanguageOptions_AreTheLanguages()
    {
        var options = await new ContentSyncStatusLanguageOptionsProvider(new FakeLanguages(languages)).GetOptionItems();

        Assert.That(options.Select(option => option.Text), Is.EqualTo(new[] { "Spanish", "English" }));
    }

    private sealed class FakeLanguages(IReadOnlyList<ContentSyncLanguage> languages) : IContentSyncFilterOptionsProvider
    {
        public Task<IReadOnlyList<ContentSyncLanguage>> GetContentLanguagesAsync(CancellationToken cancellationToken) => Task.FromResult(languages);

        public Task<IReadOnlyList<ContentSyncContentType>> GetContentTypesAsync(ContentInventoryItemKind kind, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ContentSyncContentType>>([]);
    }
}
