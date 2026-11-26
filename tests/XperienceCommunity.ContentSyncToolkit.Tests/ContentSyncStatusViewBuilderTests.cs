using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncStatusViewBuilderTests
{
    // Unspecified, as the filter's date inputs send them.
    private static DateTime Day(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static readonly IReadOnlyList<ContentSyncScope> twoScopes =
    [
        new(1, "First", "First channel"),
        new(2, "Second", "Second channel"),
    ];

    // Deliberately not alphabetical and with the default second, so "default" and "first" differ.
    private static readonly IReadOnlyList<ContentSyncLanguage> twoLanguages =
    [
        new("es", "Spanish", false),
        new("en", "English", true),
    ];

    private readonly List<(string ScopeName, string LanguageName, bool ForceRefresh)> statusCalls = [];
    private int scopeCalls;

    // NUnit reuses one fixture instance across tests.
    [SetUp]
    public void ResetCalls()
    {
        statusCalls.Clear();
        scopeCalls = 0;
    }

    private static ContentSyncStatusViewRequest Request(
        bool isSourceConfigured = true,
        int scopeId = 1,
        string languageName = "en",
        bool forceRefresh = false,
        ContentSyncStatusFilter? filter = null,
        string? searchTerm = null,
        string? sortBy = null,
        bool sortDescending = false,
        int pageSize = 50,
        int pageIndex = 0) =>
        new(isSourceConfigured, scopeId, languageName, forceRefresh, filter ?? new ContentSyncStatusFilter(),
            searchTerm, sortBy, sortDescending, pageSize, pageIndex);

    private static ContentSyncStatusItem Item(
        string treePath,
        ContentSyncStatus status = ContentSyncStatus.InSync,
        string contentTypeName = "Test.Type",
        DateTime? lastPublishedWhen = null) =>
        new(Guid.NewGuid(), status,
            new ContentInventoryItem(Guid.NewGuid(), ContentInventoryItemKind.WebPage, contentTypeName, "First", "en", treePath, lastPublishedWhen, "Published"),
            null);

    private Task<ContentSyncStatusView> Build(
        ContentSyncStatusViewRequest request,
        IReadOnlyList<ContentSyncScope> scopes,
        ContentSyncStatusResult? result = null,
        IReadOnlyList<ContentSyncLanguage>? languages = null) =>
        ContentSyncStatusViewBuilder.BuildAsync(
            request,
            _ =>
            {
                scopeCalls++;
                return Task.FromResult(scopes);
            },
            _ => Task.FromResult(languages ?? twoLanguages),
            (scopeName, languageName, forceRefresh, _) =>
            {
                statusCalls.Add((scopeName, languageName, forceRefresh));
                return Task.FromResult(result ?? new ContentSyncStatusResult(true, [Item("/A")]));
            },
            (_, _, _) => Task.FromResult<IReadOnlyDictionary<Guid, ContentSyncModifier>>(new Dictionary<Guid, ContentSyncModifier>()),
            CancellationToken.None);

    [Test]
    public async Task NotConfigured_IsReportedBeforeAnyLookup()
    {
        var view = await Build(Request(isSourceConfigured: false), twoScopes);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.NotConfigured));
        Assert.That(scopeCalls, Is.Zero, "no scope lookup should happen without a configured target");
        Assert.That(statusCalls, Is.Empty, "no fetch should be attempted without a configured target");
    }

    [Test]
    public async Task NoScopes_IsReportedWithoutFetchingStatus()
    {
        var view = await Build(Request(), []);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.NoScopes));
        Assert.That(statusCalls, Is.Empty);
    }

    // The address's channel or workspace ID, chosen in Kentico's dropdown in the top bar.
    [Test]
    public async Task TheAddressesScope_IsCompared()
    {
        await Build(Request(scopeId: 2), twoScopes);

        Assert.That(statusCalls.Single().ScopeName, Is.EqualTo("Second"));
    }

    // Deleted, or one the editor can't open: never replaced with another scope.
    [TestCase(3)]
    [TestCase(0)]
    public async Task AScopeNotInTheEditorsList_IsReportedWithoutFallingBackToAnother(int scopeId)
    {
        var view = await Build(Request(scopeId: scopeId), twoScopes);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.ScopeNotFound));
        Assert.That(statusCalls, Is.Empty);
    }

    // One language at a time: the one in the top bar.
    [Test]
    public async Task OnlyTheAddressesLanguage_IsCompared()
    {
        await Build(Request(languageName: "es"), twoScopes);

        Assert.That(statusCalls.Select(call => call.LanguageName), Is.EqualTo(new[] { "es" }));
    }

    [Test]
    public async Task TheLanguage_IsMatchedCaseInsensitively()
    {
        await Build(Request(languageName: "ES"), twoScopes);

        Assert.That(statusCalls.Single().LanguageName, Is.EqualTo("es"));
    }

    [Test]
    public async Task ALanguageThatNoLongerExists_IsReportedWithoutFetching()
    {
        var view = await Build(Request(languageName: "fr"), twoScopes);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.LanguageNotFound));
        Assert.That(statusCalls, Is.Empty);
    }

    [Test]
    public async Task ItemsView_CarriesTheResolvedScopeAndLanguage_ForBuildingLinks()
    {
        var view = await Build(Request(scopeId: 2, languageName: "es"), twoScopes);

        Assert.That(view.Scope, Is.EqualTo(twoScopes[1]));
        Assert.That(view.LanguageName, Is.EqualTo("es"));
    }

    [Test]
    public async Task Filters_AreAppliedBeforePaging_AndCombineWithSearch()
    {
        var result = new ContentSyncStatusResult(true,
        [
            Item("/Articles/Missing-new", ContentSyncStatus.New, "Test.Article", Day(2026, 3, 1)),
            Item("/Articles/OutOfDate-new", ContentSyncStatus.Changed, "Test.Article", Day(2026, 3, 2)),
            Item("/Articles/Missing-old", ContentSyncStatus.New, "Test.Article", Day(2025, 3, 1)),
            Item("/Articles/Extra-new", ContentSyncStatus.OnlyOnTarget, "Test.Article", Day(2026, 3, 1)),
            Item("/Store/Missing-new", ContentSyncStatus.New, "Test.Product", Day(2026, 3, 1)),
            Item("/Articles/InSync-new", ContentSyncStatus.InSync, "Test.Article", Day(2026, 3, 1)),
        ]);
        var filter = new ContentSyncStatusFilter(
            Status: "New,Changed",
            ContentTypeName: "Test.Article",
            PublishedFrom: Day(2026, 1, 1));

        var view = await Build(Request(filter: filter, searchTerm: "new", pageSize: 1), twoScopes, result);

        Assert.That(view.TotalCount, Is.EqualTo(2), "only new, article items of the selected statuses match");
        Assert.That(view.Items.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[] { "/Articles/Missing-new" }));
    }

    private static readonly ContentSyncStatusResult pendingAndInSync = new(true,
    [
        Item("/Pending", ContentSyncStatus.New),
        Item("/Synced", ContentSyncStatus.InSync),
    ]);

    private static string[] Paths(ContentSyncStatusView view) =>
        [.. view.Items.Select(ContentSyncStatusListingSupport.DisplayName)];

    // Without Hide items in sync (cleared, or Kentico before 31.2.0), everything is listed, pending items first.
    [Test]
    public async Task WithoutHideInSync_ListsEverything_PendingFirst()
    {
        var view = await Build(Request(filter: new ContentSyncStatusFilter()), twoScopes, pendingAndInSync);

        Assert.That(Paths(view), Is.EqualTo(new[] { "/Pending", "/Synced" }));
    }

    [Test]
    public async Task HideInSync_ListsWhatIsPending()
    {
        var view = await Build(Request(filter: new ContentSyncStatusFilter(HideInSync: true)), twoScopes, pendingAndInSync);

        Assert.That(Paths(view), Is.EqualTo(new[] { "/Pending" }));
    }

    // Selecting In sync must show it, even with Hide items in sync checked.
    [Test]
    public async Task StatusSelection_DecidesOnItsOwn()
    {
        var view = await Build(
            Request(filter: new ContentSyncStatusFilter(Status: nameof(ContentSyncStatus.InSync), HideInSync: true)), twoScopes, pendingAndInSync);

        Assert.That(Paths(view), Is.EqualTo(new[] { "/Synced" }));
    }

    // Searching answers "is this item synced?", so an item in sync must be found.
    [Test]
    public async Task Search_LooksAtItemsInSyncToo()
    {
        var view = await Build(Request(filter: new ContentSyncStatusFilter(HideInSync: true), searchTerm: "Synced"), twoScopes, pendingAndInSync);

        Assert.That(Paths(view), Is.EqualTo(new[] { "/Synced" }));
    }

    [Test]
    public async Task HideInSync_WithNothingPending_SaysEverythingIsInSync()
    {
        var result = new ContentSyncStatusResult(true, [Item("/Synced", ContentSyncStatus.InSync)]);

        var view = await Build(Request(filter: new ContentSyncStatusFilter(HideInSync: true)), twoScopes, result);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.AllInSync));
    }

    // Pending items that a filter leaves out aren't "everything is in sync".
    [Test]
    public async Task DefaultView_WithPendingItemsFilteredOut_IsAnEmptyItemsView()
    {
        var view = await Build(
            Request(filter: new ContentSyncStatusFilter(ContentTypeName: "Test.Other", HideInSync: true)), twoScopes, pendingAndInSync);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.Items));
        Assert.That(view.TotalCount, Is.Zero);
    }

    [Test]
    public async Task FiltersMatchingNothing_ReturnAnEmptyItemsView()
    {
        var view = await Build(Request(filter: new ContentSyncStatusFilter(Status: nameof(ContentSyncStatus.OnlyOnTarget))), twoScopes);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.Items));
        Assert.That(view.TotalCount, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ForceRefresh_IsPassedThroughToTheStatusQuery(bool forceRefresh)
    {
        await Build(Request(forceRefresh: forceRefresh), twoScopes);

        Assert.That(statusCalls.Single().ForceRefresh, Is.EqualTo(forceRefresh));
    }

    [Test]
    public async Task TargetUnavailable_IsDistinctFromEmpty()
    {
        var view = await Build(Request(), twoScopes, new ContentSyncStatusResult(false, []));

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.TargetUnavailable));
    }

    [Test]
    public async Task ScopeWithNoContent_IsReportedAsEmpty()
    {
        var view = await Build(Request(), twoScopes, new ContentSyncStatusResult(true, []));

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.Empty));
    }

    [Test]
    public async Task Items_AreSearchedSortedAndPaged_WithTotalCountBeforePaging()
    {
        var result = new ContentSyncStatusResult(true,
        [
            Item("/Articles/C", ContentSyncStatus.InSync),
            Item("/Articles/B", ContentSyncStatus.New),
            Item("/Articles/A", ContentSyncStatus.InSync),
            Item("/Store/X", ContentSyncStatus.New),
        ]);

        var view = await Build(Request(searchTerm: "articles", pageSize: 2, pageIndex: 0), twoScopes, result);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.Items));
        Assert.That(view.TotalCount, Is.EqualTo(3), "total reflects the search, not the page");
        Assert.That(view.Items.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[] { "/Articles/B", "/Articles/A" }));
    }

    [Test]
    public async Task SearchWithNoMatches_ReturnsAnEmptyItemsView()
    {
        var view = await Build(Request(searchTerm: "nothing-matches"), twoScopes);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.Items));
        Assert.That(view.Items, Is.Empty);
        Assert.That(view.TotalCount, Is.Zero);
    }
}
