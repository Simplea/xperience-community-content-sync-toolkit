using Kentico.Xperience.Admin.Base;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncStatusListingSupportTests
{
    private static ContentInventoryItem CreateInventoryItem(
        string? treePath = null,
        string name = "",
        string contentTypeName = "Test.Type",
        DateTime? lastPublishedWhen = null) =>
        new(Guid.NewGuid(), ContentInventoryItemKind.WebPage, contentTypeName, "Scope", "en-US", treePath, lastPublishedWhen, "Published")
        {
            Name = name,
        };

    private static ContentSyncStatusItem CreateStatusItem(
        ContentSyncStatus status,
        ContentInventoryItem? local = null,
        ContentInventoryItem? remote = null) =>
        new(Guid.NewGuid(), status, local, remote);

    [Test]
    public void ApplyStatusFilter_ReturnsAllItems_WhenFilterIsNull()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync),
            CreateStatusItem(ContentSyncStatus.MissingOnTarget),
        };

        var result = ContentSyncStatusListingSupport.ApplyStatusFilter(items, null);

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void ApplyStatusFilter_ReturnsOnlyMatchingItems_WhenFilterIsSet()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync),
            CreateStatusItem(ContentSyncStatus.MissingOnTarget),
            CreateStatusItem(ContentSyncStatus.MissingOnTarget),
        };

        var result = ContentSyncStatusListingSupport.ApplyStatusFilter(items, ContentSyncStatus.MissingOnTarget);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Has.All.Matches<ContentSyncStatusItem>(item => item.Status == ContentSyncStatus.MissingOnTarget));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ApplySearch_ReturnsAllItems_WhenSearchTermIsEmptyOrWhitespace(string? searchTerm)
    {
        var items = new[] { CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/Home")) };

        var result = ContentSyncStatusListingSupport.ApplySearch(items, searchTerm);

        Assert.That(result, Has.Count.EqualTo(1));
    }

    [Test]
    public void ApplySearch_MatchesCaseInsensitiveSubstring_OfDisplayName()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/Store/Coffee-beans")),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/Articles/Brewing")),
        };

        var result = ContentSyncStatusListingSupport.ApplySearch(items, "COFFEE");

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(ContentSyncStatusListingSupport.DisplayName(result[0]), Is.EqualTo("/Store/Coffee-beans"));
    }

    [Test]
    public void ApplyPaging_TreatsSelectedPageAsZeroBased()
    {
        var items = Enumerable.Range(1, 25)
            .Select(i => CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: $"/Item{i}")))
            .ToList();

        var page1 = ContentSyncStatusListingSupport.ApplyPaging(items, pageSize: 10, selectedPage: 0);
        var page2 = ContentSyncStatusListingSupport.ApplyPaging(items, pageSize: 10, selectedPage: 1);
        var page3 = ContentSyncStatusListingSupport.ApplyPaging(items, pageSize: 10, selectedPage: 2);

        Assert.That(page1.Select(ContentSyncStatusListingSupport.DisplayName).First(), Is.EqualTo("/Item1"));
        Assert.That(page2.Select(ContentSyncStatusListingSupport.DisplayName).First(), Is.EqualTo("/Item11"));
        Assert.That(page3.Select(ContentSyncStatusListingSupport.DisplayName).First(), Is.EqualTo("/Item21"));
        Assert.That(page3, Has.Count.EqualTo(5), "the last page should contain the remainder");
    }

    [Test]
    public void ApplyPaging_TreatsZeroOrNegativePageSize_AsNoPaging()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync),
            CreateStatusItem(ContentSyncStatus.InSync),
        };

        var result = ContentSyncStatusListingSupport.ApplyPaging(items, pageSize: 0, selectedPage: 1);

        Assert.That(result, Has.Count.EqualTo(2));
    }

    [Test]
    public void ApplySort_WithNoSortColumn_OrdersByStatusUrgencyThenName()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/A")),
            CreateStatusItem(ContentSyncStatus.ExtraOnTarget, remote: CreateInventoryItem(treePath: "/B")),
            CreateStatusItem(ContentSyncStatus.OutOfDateOnTarget, local: CreateInventoryItem(treePath: "/C")),
            CreateStatusItem(ContentSyncStatus.MissingOnTarget, local: CreateInventoryItem(treePath: "/E")),
            CreateStatusItem(ContentSyncStatus.MissingOnTarget, local: CreateInventoryItem(treePath: "/D")),
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, sortBy: null, descending: false);

        Assert.That(result.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[] { "/D", "/E", "/C", "/B", "/A" }));
    }

    [Test]
    public void ApplySort_ByStatusDescending_PutsInSyncFirst()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.MissingOnTarget),
            CreateStatusItem(ContentSyncStatus.InSync),
            CreateStatusItem(ContentSyncStatus.OutOfDateOnTarget),
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, ContentSyncStatusListingSupport.StatusColumn, descending: true);

        Assert.That(result.Select(i => i.Status), Is.EqualTo(new[]
        {
            ContentSyncStatus.InSync,
            ContentSyncStatus.OutOfDateOnTarget,
            ContentSyncStatus.MissingOnTarget,
        }));
    }

    [TestCase("name")]
    [TestCase("NAME")]
    public void ApplySort_ByName_IsCaseInsensitiveOnColumnAndValue(string sortBy)
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/b")),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/A")),
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, sortBy, descending: false);

        Assert.That(result.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[] { "/A", "/b" }));
    }

    [Test]
    public void ApplySort_ByNameDescending_ReversesOrder()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/A")),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/B")),
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, ContentSyncStatusListingSupport.NameColumn, descending: true);

        Assert.That(result.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[] { "/B", "/A" }));
    }

    [Test]
    public void ApplySort_ByContentType_BreaksTiesByName()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/Z", contentTypeName: "Article")),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/Y", contentTypeName: "Product")),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/X", contentTypeName: "Article")),
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, ContentSyncStatusListingSupport.ContentTypeColumn, descending: false);

        Assert.That(result.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[] { "/X", "/Z", "/Y" }));
    }

    [Test]
    public void ApplySort_ByLastPublished_TreatsNullAsEarliest()
    {
        var earlier = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(lastPublishedWhen: earlier)),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(lastPublishedWhen: null)),
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, ContentSyncStatusListingSupport.LastPublishedColumn, descending: false);

        Assert.That(ContentSyncStatusListingSupport.LastPublishedWhen(result[0]), Is.Null);
        Assert.That(ContentSyncStatusListingSupport.LastPublishedWhen(result[1]), Is.EqualTo(earlier));
    }

    [Test]
    public void DisplayName_PrefersTreePath_OverName()
    {
        var item = CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: "/Home", name: "Home page"));

        Assert.That(ContentSyncStatusListingSupport.DisplayName(item), Is.EqualTo("/Home"));
    }

    [Test]
    public void DisplayName_FallsBackToName_WhenTreePathIsNull()
    {
        var item = CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(treePath: null, name: "Coffee article"));

        Assert.That(ContentSyncStatusListingSupport.DisplayName(item), Is.EqualTo("Coffee article"));
    }

    [Test]
    public void DisplayName_PrefersLocal_ButFallsBackToRemote_WhenLocalIsNull()
    {
        var item = CreateStatusItem(
            ContentSyncStatus.ExtraOnTarget,
            local: null,
            remote: CreateInventoryItem(treePath: "/OnlyOnTarget"));

        Assert.That(ContentSyncStatusListingSupport.DisplayName(item), Is.EqualTo("/OnlyOnTarget"));
    }

    [TestCase(ContentSyncStatus.InSync, "In sync")]
    [TestCase(ContentSyncStatus.MissingOnTarget, "Missing on target")]
    [TestCase(ContentSyncStatus.OutOfDateOnTarget, "Out of date on target")]
    [TestCase(ContentSyncStatus.ExtraOnTarget, "Extra on target")]
    public void StatusLabel_ReturnsExpectedText(ContentSyncStatus status, string expected) =>
        Assert.That(ContentSyncStatusListingSupport.StatusLabel(status), Is.EqualTo(expected));

    [TestCase(ContentSyncStatus.InSync, Color.SuccessBackgroundHighEmphasis)]
    [TestCase(ContentSyncStatus.MissingOnTarget, Color.AlertBackgroundHighEmphasis)]
    [TestCase(ContentSyncStatus.OutOfDateOnTarget, Color.WarningBackgroundHighEmphasis)]
    [TestCase(ContentSyncStatus.ExtraOnTarget, Color.BackgroundTagGrey)]
    public void StatusColor_ReturnsExpectedToken(ContentSyncStatus status, Color expected) =>
        Assert.That(ContentSyncStatusListingSupport.StatusColor(status), Is.EqualTo(expected));
}
