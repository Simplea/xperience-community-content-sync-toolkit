using Kentico.Xperience.Admin.Base;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

public class ContentSyncStatusListingSupportTests
{
    // Unspecified, as the filter's date inputs send them.
    private static DateTime Day(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

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

        var result = ContentSyncStatusListingSupport.ApplyStatusFilter(items, [ContentSyncStatus.MissingOnTarget]);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Has.All.Matches<ContentSyncStatusItem>(item => item.Status == ContentSyncStatus.MissingOnTarget));
    }

    [Test]
    public void ParseStatusFilter_NeedsAction_IsMissingPlusOutOfDate()
    {
        var statuses = ContentSyncStatusListingSupport.ParseStatusFilter(ContentSyncStatusListingSupport.NeedsActionStatusFilter);

        Assert.That(statuses, Is.EquivalentTo(new[] { ContentSyncStatus.MissingOnTarget, ContentSyncStatus.OutOfDateOnTarget }));
    }

    [TestCase(nameof(ContentSyncStatus.ExtraOnTarget), ContentSyncStatus.ExtraOnTarget)]
    [TestCase("inSync", ContentSyncStatus.InSync)]
    public void ParseStatusFilter_StatusName_IsThatStatusOnly(string value, ContentSyncStatus expected) =>
        Assert.That(ContentSyncStatusListingSupport.ParseStatusFilter(value), Is.EqualTo(new[] { expected }));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-a-status")]
    [TestCase("42")]
    public void ParseStatusFilter_EmptyOrUnknown_IsNoFilter(string? value) =>
        Assert.That(ContentSyncStatusListingSupport.ParseStatusFilter(value), Is.Null);

    // Every dropdown option must parse, or picking it would silently show everything.
    [Test]
    public void StatusFilterOptions_EveryValueParses()
    {
        var values = ContentSyncStatusListingSupport.StatusFilterOptions
            .Split("\r\n")
            .Select(line => line.Split(';')[0]);

        Assert.That(values, Has.All.Matches<string>(value => ContentSyncStatusListingSupport.ParseStatusFilter(value) is not null));
        Assert.That(values.Count(), Is.EqualTo(Enum.GetValues<ContentSyncStatus>().Length + 1));
    }

    [Test]
    public void ApplyContentTypeFilter_MatchesCodeNameCaseInsensitively()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(contentTypeName: "DancingGoat.ArticlePage")),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(contentTypeName: "DancingGoat.ProductPage")),
            CreateStatusItem(ContentSyncStatus.ExtraOnTarget, remote: CreateInventoryItem(contentTypeName: "DancingGoat.ArticlePage")),
        };

        var result = ContentSyncStatusListingSupport.ApplyContentTypeFilter(items, "dancinggoat.articlepage");

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(ContentSyncStatusListingSupport.ApplyContentTypeFilter(items, null), Has.Count.EqualTo(3));
    }

    [Test]
    public void ApplyPublishedFilter_BoundsAreInclusiveWholeDays()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/before", lastPublishedWhen: Day(2026, 1, 9, 23, 59))),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/first-day", lastPublishedWhen: Day(2026, 1, 10))),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/last-day", lastPublishedWhen: Day(2026, 1, 20, 23, 59))),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/after", lastPublishedWhen: Day(2026, 1, 21))),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/never")),
        };

        var result = ContentSyncStatusListingSupport.ApplyPublishedFilter(items, Day(2026, 1, 10), Day(2026, 1, 20));

        Assert.That(result.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[] { "/first-day", "/last-day" }));
    }

    [Test]
    public void ApplyPublishedFilter_SingleBound_ExcludesItemsWithoutAPublishDate()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/dated", lastPublishedWhen: Day(2026, 3, 1))),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/never")),
        };

        Assert.That(ContentSyncStatusListingSupport.ApplyPublishedFilter(items, null, Day(2026, 12, 31)).Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[] { "/dated" }));
        Assert.That(ContentSyncStatusListingSupport.ApplyPublishedFilter(items, null, null), Has.Count.EqualTo(2));
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

    // The Status column is the tabs' default sort, so this is the order editors first see.
    [TestCase(null)]
    [TestCase(ContentSyncStatusListingSupport.StatusColumn)]
    public void ApplySort_ByStatus_PutsTheMostRecentlyPublishedFirstWithinEachStatus(string? sortBy)
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/in-sync-new", lastPublishedWhen: Day(2026, 9, 1))),
            CreateStatusItem(ContentSyncStatus.MissingOnTarget, local: CreateInventoryItem("/missing-old", lastPublishedWhen: Day(2025, 1, 1))),
            CreateStatusItem(ContentSyncStatus.MissingOnTarget, local: CreateInventoryItem("/missing-never")),
            CreateStatusItem(ContentSyncStatus.MissingOnTarget, local: CreateInventoryItem("/missing-new-b", lastPublishedWhen: Day(2026, 9, 1))),
            CreateStatusItem(ContentSyncStatus.MissingOnTarget, local: CreateInventoryItem("/missing-new-a", lastPublishedWhen: Day(2026, 9, 1))),
            CreateStatusItem(ContentSyncStatus.OutOfDateOnTarget, local: CreateInventoryItem("/out-of-date", lastPublishedWhen: Day(2024, 1, 1))),
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, sortBy, descending: false);

        Assert.That(result.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[]
        {
            "/missing-new-a", "/missing-new-b", "/missing-old", "/missing-never",
            "/out-of-date",
            "/in-sync-new",
        }), "newest first within a status, then by name; never-published last");
    }

    // Reversing the status order doesn't reverse the tie-break: newest first either way.
    [Test]
    public void ApplySort_ByStatusDescending_KeepsNewestFirstWithinEachStatus()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/in-sync-old", lastPublishedWhen: Day(2025, 1, 1))),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/in-sync-new", lastPublishedWhen: Day(2026, 1, 1))),
            CreateStatusItem(ContentSyncStatus.MissingOnTarget, local: CreateInventoryItem("/missing", lastPublishedWhen: Day(2026, 1, 1))),
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, ContentSyncStatusListingSupport.StatusColumn, descending: true);

        Assert.That(result.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[] { "/in-sync-new", "/in-sync-old", "/missing" }));
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
