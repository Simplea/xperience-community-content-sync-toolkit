using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncStatusViewBuilderTests
{
    private static readonly IReadOnlyList<ContentSyncScope> twoScopes =
    [
        new(1, "First", "First channel"),
        new(2, "Second", "Second channel"),
    ];

    private readonly List<(string ScopeName, bool ForceRefresh)> statusCalls = [];
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
        string? requestedScopeName = null,
        bool forceRefresh = false,
        string? searchTerm = null,
        string? sortBy = null,
        bool sortDescending = false,
        int pageSize = 50,
        int pageIndex = 0) =>
        new(isSourceConfigured, requestedScopeName, forceRefresh, searchTerm, sortBy, sortDescending, pageSize, pageIndex);

    private static ContentSyncStatusItem Item(string treePath, ContentSyncStatus status = ContentSyncStatus.InSync) =>
        new(Guid.NewGuid(), status,
            new ContentInventoryItem(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "Test.Type", "First", "en", treePath, null, "Published"),
            null);

    private Task<ContentSyncStatusView> Build(
        ContentSyncStatusViewRequest request,
        IReadOnlyList<ContentSyncScope> scopes,
        ContentSyncStatusResult? result = null) =>
        ContentSyncStatusViewBuilder.BuildAsync(
            request,
            _ =>
            {
                scopeCalls++;
                return Task.FromResult(scopes);
            },
            (scopeName, forceRefresh, _) =>
            {
                statusCalls.Add((scopeName, forceRefresh));
                return Task.FromResult(result ?? new ContentSyncStatusResult(true, [Item("/A")]));
            },
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

    [Test]
    public async Task NoRequestedScope_DefaultsToTheFirstScope()
    {
        await Build(Request(requestedScopeName: null), twoScopes);

        Assert.That(statusCalls.Single().ScopeName, Is.EqualTo("First"));
    }

    [Test]
    public async Task RequestedScope_IsUsedCaseInsensitively()
    {
        await Build(Request(requestedScopeName: "second"), twoScopes);

        Assert.That(statusCalls.Single().ScopeName, Is.EqualTo("Second"));
    }

    [Test]
    public async Task RequestedScopeThatNoLongerExists_IsReportedWithoutFallingBackToAnother()
    {
        var view = await Build(Request(requestedScopeName: "Deleted"), twoScopes);

        Assert.That(view.Kind, Is.EqualTo(ContentSyncStatusViewKind.ScopeNotFound));
        Assert.That(statusCalls, Is.Empty);
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
            Item("/Articles/B", ContentSyncStatus.MissingOnTarget),
            Item("/Articles/A", ContentSyncStatus.InSync),
            Item("/Store/X", ContentSyncStatus.MissingOnTarget),
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
