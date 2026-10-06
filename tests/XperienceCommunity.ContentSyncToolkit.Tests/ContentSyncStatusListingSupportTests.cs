using Kentico.Xperience.Admin.Base;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;
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

    private static ContentInventoryItem Versioned(string versionStatus) =>
        new(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "T", "S", "en", "/p", null, versionStatus);

    // Each status's tooltip says what it's based on and what a sync does.
    [TestCase(ContentSyncStatus.New, "Published", null, "Not on the target yet. A sync creates it.")]
    [TestCase(ContentSyncStatus.New, "Unpublished", null, "Not on the target yet. It's unpublished here, so a sync creates it unpublished.")]
    [TestCase(ContentSyncStatus.Changed, "Published", "Published", "Published here after the target's copy. A sync updates it.")]
    [TestCase(ContentSyncStatus.Changed, "Published", "Unpublished", "Published here, unpublished on the target. A sync publishes it there.")]
    [TestCase(ContentSyncStatus.Unpublished, "Unpublished", "Published", "Unpublished here, still published on the target. A sync unpublishes it there.")]
    [TestCase(ContentSyncStatus.InSync, "Unpublished", "Unpublished", "Unpublished on both instances.")]
    public void StatusTooltip_ExplainsTheStatus(ContentSyncStatus status, string? local, string? remote, string expected)
    {
        var item = CreateStatusItem(status,
            local: local is null ? null : Versioned(local),
            remote: remote is null ? null : Versioned(remote));

        Assert.That(ContentSyncStatusListingSupport.StatusTooltip(item), Is.EqualTo(expected));
    }

    // A draft after unpublishing can't be synced; the tooltip says why and what the target has.
    [TestCase("Published", "The target still has it published.")]
    [TestCase("Unpublished", "The target has it unpublished.")]
    [TestCase(null, "The target doesn't have it.")]
    public void StatusTooltip_ForAnUnpublishedDraft_SaysItCantSyncUntilPublished_AndWhatTheTargetHas(string? remote, string end) =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(CreateStatusItem(ContentSyncStatus.NotPublished,
                local: Versioned("UnpublishedDraft"), remote: remote is null ? null : Versioned(remote))),
            Is.EqualTo("Unpublished here, then edited again, so it has no published version. Content Sync can't sync it until it's published again. " + end));

    // Never published here, while the target has it: it exists, so it isn't "only on the target".
    [Test]
    public void StatusTooltip_ForANeverPublishedItem_SaysOnlyADraftExistsHere() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(CreateStatusItem(ContentSyncStatus.NotPublished,
                local: Versioned("NeverPublished"), remote: Versioned("Published"))),
            Is.EqualTo("Never published here: only a draft exists on this instance. Content Sync can't sync it until it's published. The target still has it published."));

    [Test]
    public void StatusTooltip_ForAMovedPage_SaysWhichLevelsToSync() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(CreateStatusItem(ContentSyncStatus.Moved, local: Versioned("Published"), remote: Versioned("Published"))),
            Does.Contain("sync all pages on its old and new level"));

    // Content Sync can't delete, so an item only on the target always says how to remove it.
    [TestCase("Published", "Only on the target.")]
    [TestCase("Unpublished", "Unpublished on the target, and not on this instance.")]
    public void StatusTooltip_OnlyOnTarget_ExplainsThatContentSyncCannotDelete(string remote, string start)
    {
        var item = CreateStatusItem(ContentSyncStatus.OnlyOnTarget, remote: Versioned(remote));

        Assert.That(ContentSyncStatusListingSupport.StatusTooltip(item), Does.StartWith(start).And.Contain("delete it on the target"));
    }

    [Test]
    public void StatusTooltip_IsNull_ForAPublishedItemInSync() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(CreateStatusItem(ContentSyncStatus.InSync, local: Versioned("Published"), remote: Versioned("Published"))),
            Is.Null);

    // A newer draft isn't synced, so the status stays; the tooltip says why an edit doesn't show.
    [Test]
    public void StatusTooltip_ForAnItemInSyncWithANewerDraft_SaysOnlyThePublishedVersionIsSynced() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(CreateStatusItem(ContentSyncStatus.InSync, local: Versioned("Published"), remote: Versioned("Published")), hasNewerDraft: true),
            Is.EqualTo("The target has the published version. A newer draft here isn't published yet, and a sync only sends the published version."));

    [Test]
    public void StatusTooltip_WithANewerDraft_AddsTheNoteToTheStatusTooltip() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(CreateStatusItem(ContentSyncStatus.Changed, local: Versioned("Published"), remote: Versioned("Published")), hasNewerDraft: true),
            Is.EqualTo("Published here after the target's copy. A sync updates it. A newer draft here isn't published yet, and a sync only sends the published version."));

    // An unpublished item's latest version isn't a newer draft of a published one.
    [Test]
    public void StatusTooltip_ForAnUnpublishedItem_IgnoresTheDraftFlag() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(CreateStatusItem(ContentSyncStatus.Unpublished, local: Versioned("Unpublished"), remote: Versioned("Published")), hasNewerDraft: true),
            Is.EqualTo("Unpublished here, still published on the target. A sync unpublishes it there."));

    private static readonly RequiredObjectIssue contentTypeIssue =
        new(new RequiredObject(RequiredObjectKind.ContentType, Guid.NewGuid(), "T", "Type"), RequiredObjectProblem.MissingOnTarget);

    private static ContentSyncStatusItem Incompatible(ContentSyncStatus status) =>
        CreateStatusItem(status) with { RequiredObjectIssues = [contentTypeIssue] };

    // An incompatible item shows Incompatible instead of its status, in red, since nothing can be synced until
    // the target is updated.
    [Test]
    public void IncompatibleItem_ShowsIncompatible_InsteadOfItsStatus()
    {
        var item = Incompatible(ContentSyncStatus.Changed);

        Assert.That(ContentSyncStatusListingSupport.StatusLabel(item), Is.EqualTo("Incompatible"));
        Assert.That(ContentSyncStatusListingSupport.StatusColor(item), Is.EqualTo(Color.AlertBackgroundHighEmphasis));
        Assert.That(ContentSyncStatusListingSupport.StatusLabel(CreateStatusItem(ContentSyncStatus.Changed)), Is.EqualTo("Changed"));
    }

    // The tooltip says what the target lacks, who fixes it, and what the sync does afterwards.
    [TestCase(ContentSyncStatus.New, "Then a sync creates it.")]
    [TestCase(ContentSyncStatus.Changed, "Then a sync updates it.")]
    [TestCase(ContentSyncStatus.Unpublished, "Then a sync unpublishes it there.")]
    [TestCase(ContentSyncStatus.Moved, "Then sync all pages on its old and new level to move it.")]
    [TestCase(ContentSyncStatus.Reordered, "Then sync all pages on its level to fix the order.")]
    public void StatusTooltip_ForAnIncompatibleItem_SaysWhatTheTargetNeeds_ThenWhatASyncDoes(ContentSyncStatus status, string end) =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(Incompatible(status)),
            Is.EqualTo("Content type Type doesn't exist on the target instance. A developer needs to deploy it to the target first. " + end));

    [Test]
    public void ApplyStatusFilter_ReturnsAllItems_WhenFilterIsNull()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync),
            CreateStatusItem(ContentSyncStatus.New),
        };

        var result = ContentSyncStatusListingSupport.ApplyStatusFilter(items, null);

        Assert.That(result, Has.Count.EqualTo(2));
    }

    // Worded like Kentico's Content Sync dialog, so editors see the same message in both places.
    [TestCase(RequiredObjectKind.ContentType, "Image", RequiredObjectProblem.DefinitionDiffers, "Content type Image has different field definitions on the source and target instance.")]
    [TestCase(RequiredObjectKind.Language, "Spanish", RequiredObjectProblem.MissingOnTarget, "Language Spanish doesn't exist on the target instance.")]
    [TestCase(RequiredObjectKind.WebsiteChannel, "Shop", RequiredObjectProblem.MissingOnTarget, "Website channel Shop doesn't exist on the target instance.")]
    [TestCase(RequiredObjectKind.Workspace, "<R&D>", RequiredObjectProblem.DifferentGuidOnTarget, "Workspace <R&D> has a different identity on the target instance: it was recreated there instead of deployed.")]
    public void IssueSentence_DescribesTheCompatibilityError(RequiredObjectKind kind, string name, RequiredObjectProblem problem, string expected) =>
        Assert.That(
            ContentSyncStatusListingSupport.IssueSentence(new RequiredObjectIssue(new RequiredObject(kind, Guid.NewGuid(), "code", name), problem)),
            Is.EqualTo(expected));

    // Hiding items in sync keeps everything that differs, so an incompatible item never disappears.
    [Test]
    public void ApplyHideInSync_KeepsEverythingExceptInSync_IncludingIncompatibleAndOnlyOnTarget()
    {
        var items = ContentSyncStatusListingSupport.StatusOrder.Select(status => CreateStatusItem(status))
            .Append(Incompatible(ContentSyncStatus.New))
            .ToList();

        var hidden = ContentSyncStatusListingSupport.ApplyHideInSync(items, hideInSync: true);

        Assert.That(hidden.Select(item => ContentSyncStatusListingSupport.StatusLabel(item)), Is.EqualTo(new[]
        {
            "Unpublished", "New", "Changed", "Moved", "Reordered", "Not published", "Only on target", "Incompatible",
        }));
        Assert.That(ContentSyncStatusListingSupport.ApplyHideInSync(items, hideInSync: false), Is.SameAs(items));
    }

    [Test]
    public void ParseStatusFilter_Incompatible_IsItemsWithRequiredObjectIssues_WhateverTheirStatus()
    {
        var incompatibleNew = Incompatible(ContentSyncStatus.New);
        var incompatibleChanged = Incompatible(ContentSyncStatus.Changed);
        var compatible = CreateStatusItem(ContentSyncStatus.New);

        var result = ContentSyncStatusListingSupport.ApplyStatusFilter(
            [incompatibleNew, compatible, incompatibleChanged], ContentSyncStatusListingSupport.ParseStatusFilter(ContentSyncStatusListingSupport.IncompatibleStatusFilter));

        Assert.That(result, Is.EqualTo(new[] { incompatibleNew, incompatibleChanged }));
    }

    // A status option matches what the tag shows, so an incompatible item doesn't match its status.
    [TestCase(nameof(ContentSyncStatus.OnlyOnTarget), ContentSyncStatus.OnlyOnTarget)]
    [TestCase("inSync", ContentSyncStatus.InSync)]
    [TestCase("unpublished", ContentSyncStatus.Unpublished)]
    public void ParseStatusFilter_StatusName_IsThatStatusOnly_Compatible(string value, ContentSyncStatus expected)
    {
        var items = ContentSyncStatusListingSupport.StatusOrder.Select(status => CreateStatusItem(status))
            .Append(Incompatible(expected))
            .ToList();

        var result = ContentSyncStatusListingSupport.ApplyStatusFilter(items, ContentSyncStatusListingSupport.ParseStatusFilter(value));

        Assert.That(result.Select(item => (item.Status, item.HasCompatibilityIssues)), Is.EqualTo(new[] { (expected, false) }));
    }

    // The filter allows several options and shows items matching any of them.
    [Test]
    public void ParseStatusFilter_SeveralValues_MatchesAnyOfThem()
    {
        var items = ContentSyncStatusListingSupport.StatusOrder.Select(status => CreateStatusItem(status))
            .Append(Incompatible(ContentSyncStatus.Changed))
            .ToList();

        var result = ContentSyncStatusListingSupport.ApplyStatusFilter(items, ContentSyncStatusListingSupport.ParseStatusFilter("New, incompatible,OnlyOnTarget,not-a-status,needs-sync"));

        Assert.That(result.Select(item => ContentSyncStatusListingSupport.StatusLabel(item)), Is.EqualTo(new[] { "New", "Only on target", "Incompatible" }));
    }

    // Only the tags editors see: groups of statuses are in the Show filter.
    [Test]
    public void StatusFilterOptions_ListIncompatible_ThenEveryStatusInOrder()
    {
        var expected = new[] { ("incompatible", "Incompatible") }
            .Concat(ContentSyncStatusListingSupport.StatusOrder.Select(status => (status.ToString(), ContentSyncStatusListingSupport.StatusLabel(status))));

        Assert.That(ContentSyncStatusListingSupport.StatusFilterOptions, Is.EqualTo(expected));
        Assert.That(ContentSyncStatusListingSupport.StatusOrder, Is.EquivalentTo(Enum.GetValues<ContentSyncStatus>()), "every status is listed");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" , ")]
    [TestCase("not-a-status")]
    [TestCase("42")]
    public void ParseStatusFilter_EmptyOrUnknown_IsNoFilter(string? value) =>
        Assert.That(ContentSyncStatusListingSupport.ParseStatusFilter(value), Is.Null);

    // Every option must parse, or picking it would silently show everything.
    [Test]
    public void StatusFilterOptions_EveryValueParses() =>
        Assert.That(
            ContentSyncStatusListingSupport.StatusFilterOptions.Select(option => option.Value),
            Has.All.Matches<string>(value => ContentSyncStatusListingSupport.ParseStatusFilter(value) is not null));

    [Test]
    public void ApplyContentTypeFilter_MatchesCodeNameCaseInsensitively()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(contentTypeName: "DancingGoat.ArticlePage")),
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem(contentTypeName: "DancingGoat.ProductPage")),
            CreateStatusItem(ContentSyncStatus.OnlyOnTarget, remote: CreateInventoryItem(contentTypeName: "DancingGoat.ArticlePage")),
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
            CreateStatusItem(ContentSyncStatus.OnlyOnTarget, remote: CreateInventoryItem(treePath: "/B")),
            CreateStatusItem(ContentSyncStatus.Changed, local: CreateInventoryItem(treePath: "/C")),
            CreateStatusItem(ContentSyncStatus.New, local: CreateInventoryItem(treePath: "/E")),
            CreateStatusItem(ContentSyncStatus.New, local: CreateInventoryItem(treePath: "/D")),
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
            CreateStatusItem(ContentSyncStatus.New, local: CreateInventoryItem("/missing-old", lastPublishedWhen: Day(2025, 1, 1))),
            CreateStatusItem(ContentSyncStatus.New, local: CreateInventoryItem("/missing-never")),
            CreateStatusItem(ContentSyncStatus.New, local: CreateInventoryItem("/missing-new-b", lastPublishedWhen: Day(2026, 9, 1))),
            CreateStatusItem(ContentSyncStatus.New, local: CreateInventoryItem("/missing-new-a", lastPublishedWhen: Day(2026, 9, 1))),
            CreateStatusItem(ContentSyncStatus.Changed, local: CreateInventoryItem("/out-of-date", lastPublishedWhen: Day(2024, 1, 1))),
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
            CreateStatusItem(ContentSyncStatus.New, local: CreateInventoryItem("/missing", lastPublishedWhen: Day(2026, 1, 1))),
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, ContentSyncStatusListingSupport.StatusColumn, descending: true);

        Assert.That(result.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(new[] { "/in-sync-new", "/in-sync-old", "/missing" }));
    }

    [Test]
    public void ApplySort_ByStatusDescending_PutsInSyncFirst()
    {
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.New),
            CreateStatusItem(ContentSyncStatus.InSync),
            CreateStatusItem(ContentSyncStatus.Changed),
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, ContentSyncStatusListingSupport.StatusColumn, descending: true);

        Assert.That(result.Select(i => i.Status), Is.EqualTo(new[]
        {
            ContentSyncStatus.InSync,
            ContentSyncStatus.Changed,
            ContentSyncStatus.New,
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

    // Content hub items show the name editors see in the Content hub.
    [Test]
    public void DisplayName_PrefersTheItemsDisplayName_OverItsCodeName()
    {
        var item = CreateStatusItem(
            ContentSyncStatus.InSync,
            local: CreateInventoryItem(treePath: null, name: "CoffeeBeans-x7k2") with { DisplayName = "Coffee beans" });

        Assert.That(ContentSyncStatusListingSupport.DisplayName(item), Is.EqualTo("Coffee beans"));
    }

    [Test]
    public void ApplySearch_MatchesTheDisplayName_NotTheCodeName()
    {
        var item = CreateStatusItem(
            ContentSyncStatus.InSync,
            local: CreateInventoryItem(treePath: null, name: "CoffeeBeans-x7k2") with { DisplayName = "Coffee beans" });

        Assert.That(ContentSyncStatusListingSupport.ApplySearch([item], "coffee b"), Has.Count.EqualTo(1));
    }

    [TestCase("Coffee product", "Coffee product")]
    [TestCase(null, "DG.Coffee")]
    public void ContentTypeDisplayName_FallsBackToTheCodeName(string? displayName, string expected)
    {
        var item = CreateStatusItem(
            ContentSyncStatus.InSync,
            local: CreateInventoryItem(contentTypeName: "DG.Coffee") with { ContentTypeDisplayName = displayName });

        Assert.That(ContentSyncStatusListingSupport.ContentTypeDisplayName(item), Is.EqualTo(expected));
        Assert.That(ContentSyncStatusListingSupport.ContentTypeName(item), Is.EqualTo("DG.Coffee"));
    }

    // Sorted by what the column shows, not the code name.
    [Test]
    public void ApplySort_ByContentType_UsesTheDisplayName()
    {
        var zebra = CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/a", contentTypeName: "A.Type") with { ContentTypeDisplayName = "Zebra" });
        var apple = CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/b", contentTypeName: "Z.Type") with { ContentTypeDisplayName = "Apple" });

        var result = ContentSyncStatusListingSupport.ApplySort([zebra, apple], ContentSyncStatusListingSupport.ContentTypeColumn, descending: false);

        Assert.That(result, Is.EqualTo(new[] { apple, zebra }));
    }

    [Test]
    public void StatusTooltip_ForAnIncompatibleItem_ListsEveryObjectTheTargetNeeds()
    {
        var type = new RequiredObject(RequiredObjectKind.ContentType, Guid.NewGuid(), "DG.Article", "Article");
        var language = new RequiredObject(RequiredObjectKind.Language, Guid.NewGuid(), "es", "Spanish");
        var item = CreateStatusItem(ContentSyncStatus.Changed, local: Versioned("Published"), remote: Versioned("Published")) with
        {
            RequiredObjectIssues =
            [
                new RequiredObjectIssue(type, RequiredObjectProblem.DefinitionDiffers),
                new RequiredObjectIssue(language, RequiredObjectProblem.MissingOnTarget),
            ],
        };

        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(item),
            Is.EqualTo("Content type Article has different field definitions on the source and target instance. Language Spanish doesn't exist on the target instance. "
                + "A developer needs to deploy them to the target first. Then a sync updates it."));
    }

    // Incompatible items come first in the default sort, whatever their status, since they need a
    // developer before anyone can sync them; reversing the sort puts them last.
    [TestCase(null, false, new[] { "/incompatible-changed", "/incompatible-reordered", "/new", "/in-sync" })]
    [TestCase(ContentSyncStatusListingSupport.StatusColumn, false, new[] { "/incompatible-changed", "/incompatible-reordered", "/new", "/in-sync" })]
    [TestCase(ContentSyncStatusListingSupport.StatusColumn, true, new[] { "/in-sync", "/new", "/incompatible-reordered", "/incompatible-changed" })]
    public void ApplySort_ByStatus_PutsIncompatibleItemsFirst(string? sortBy, bool descending, string[] expected)
    {
        var issue = new RequiredObjectIssue(new RequiredObject(RequiredObjectKind.ContentType, Guid.NewGuid(), "T", "Type"), RequiredObjectProblem.MissingOnTarget);
        var items = new[]
        {
            CreateStatusItem(ContentSyncStatus.New, local: CreateInventoryItem("/new")),
            CreateStatusItem(ContentSyncStatus.Reordered, local: CreateInventoryItem("/incompatible-reordered")) with { RequiredObjectIssues = [issue] },
            CreateStatusItem(ContentSyncStatus.InSync, local: CreateInventoryItem("/in-sync")),
            CreateStatusItem(ContentSyncStatus.Changed, local: CreateInventoryItem("/incompatible-changed")) with { RequiredObjectIssues = [issue] },
        };

        var result = ContentSyncStatusListingSupport.ApplySort(items, sortBy, descending);

        Assert.That(result.Select(ContentSyncStatusListingSupport.DisplayName), Is.EqualTo(expected));
    }

    [Test]
    public void DisplayName_PrefersLocal_ButFallsBackToRemote_WhenLocalIsNull()
    {
        var item = CreateStatusItem(
            ContentSyncStatus.OnlyOnTarget,
            local: null,
            remote: CreateInventoryItem(treePath: "/OnlyOnTarget"));

        Assert.That(ContentSyncStatusListingSupport.DisplayName(item), Is.EqualTo("/OnlyOnTarget"));
    }

    private static ContentInventoryItem Article(string slug, string? displayName = null) =>
        new(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "T", "S", "en", "/Articles/" + slug, null, "Published")
        {
            DisplayName = displayName,
        };

    private static ContentSyncStatusItem Reordered(ContentSyncReorder reorder) =>
        CreateStatusItem(ContentSyncStatus.Reordered, local: Versioned("Published"), remote: Versioned("Published")) with
        {
            Reorder = reorder,
        };

    [Test]
    public void StatusTooltip_ForAReorder_NamesThePageAndTheFix()
    {
        var reorder = new ContentSyncReorder("/Articles", [Article("Coffee_Beverages_Explained", "Coffee Beverages Explained")])
        {
            Parent = new ContentInventoryItem(Guid.NewGuid(), ContentInventoryItemKind.WebPage, "T", "S", "en", "/Articles", null, "Published") { DisplayName = "Articles" },
        };

        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(Reordered(reorder)),
            Is.EqualTo("Page order on this level differs on the target: Coffee Beverages Explained is in a different position there. "
                + "This usually happens when only some pages of a level are synced. To fix it, use Sync with all subpages on Articles."));
    }

    // Without a display name, a page is named by its path's last segment; without the parent page in
    // the inventory, so is the parent.
    [Test]
    public void StatusTooltip_ForAReorder_NamesUpToThreePages_ThenACount()
    {
        var reorder = new ContentSyncReorder("/Articles", [Article("A"), Article("B"), Article("C"), Article("D"), Article("E")]);

        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(Reordered(reorder)),
            Does.StartWith("Page order on this level differs on the target: A, B, C and 2 more are in a different position there.")
                .And.EndWith("use Sync with all subpages on Articles."));
    }

    // Pages the user can't see are counted, never named.
    [TestCase(1, "Page order on this level differs on the target: A and a page you can't see are in a different position there.")]
    [TestCase(2, "Page order on this level differs on the target: A and 2 pages you can't see are in a different position there.")]
    public void StatusTooltip_ForAReorder_CountsPagesTheUserCantSee(int hidden, string start)
    {
        var reorder = new ContentSyncReorder("/Articles", [Article("A")]) { HiddenMisplacedCount = hidden };

        Assert.That(ContentSyncStatusListingSupport.StatusTooltip(Reordered(reorder)), Does.StartWith(start));
    }

    [Test]
    public void StatusTooltip_ForAReorderOfOnlyHiddenPages_SaysSoWithoutNames()
    {
        var reorder = new ContentSyncReorder("/Articles", []) { HiddenMisplacedCount = 1 };

        Assert.That(
            ContentSyncStatusListingSupport.StatusTooltip(Reordered(reorder)),
            Does.StartWith("Page order on this level differs on the target: a page you can't see is in a different position there."));
    }

    [Test]
    public void StatusTooltip_ForAReorderOfTwoPages_JoinsTheirNamesWithAnd()
    {
        var reorder = new ContentSyncReorder("/Articles", [Article("A"), Article("B")]);

        Assert.That(ContentSyncStatusListingSupport.StatusTooltip(Reordered(reorder)), Does.Contain(": A and B are in a different position"));
    }

    // The channel's top level has no parent page to sync from.
    [Test]
    public void StatusTooltip_ForAReorderAtTheTopLevel_SaysToSyncTheLevel()
    {
        var reorder = new ContentSyncReorder(string.Empty, [Article("A")]);

        Assert.That(ContentSyncStatusListingSupport.StatusTooltip(Reordered(reorder)), Does.EndWith("To fix it, sync all pages on this level."));
    }

    [TestCase(90, "which can be up to 90 seconds old.")]
    [TestCase(1, "which can be up to 1 second old.")]
    [TestCase(300, "which can be up to 5 minutes old.")]
    [TestCase(7200, "which can be up to 2 hours old.")]
    public void RefreshTooltip_SaysHowLongTheTargetsListIsReused(int seconds, string expected)
    {
        string tooltip = ContentSyncStatusListingSupport.RefreshTooltip(TimeSpan.FromSeconds(seconds));

        Assert.That(tooltip, Does.Contain(expected));
        Assert.That(tooltip, Does.StartWith("Reloads the target's status, "));
        Assert.That(tooltip, Does.EndWith(" A sync can take about 30 seconds to reach the target."));
    }

    [Test]
    public void RefreshTooltip_WithCachingOff_OnlyMentionsTheSyncDelay() =>
        Assert.That(
            ContentSyncStatusListingSupport.RefreshTooltip(TimeSpan.Zero),
            Is.EqualTo("Reloads the target's status. A sync can take about 30 seconds to reach the target."));

    [TestCase(ContentSyncStatus.Unpublished, "Unpublished")]
    [TestCase(ContentSyncStatus.New, "New")]
    [TestCase(ContentSyncStatus.Changed, "Changed")]
    [TestCase(ContentSyncStatus.Moved, "Moved")]
    [TestCase(ContentSyncStatus.Reordered, "Reordered")]
    [TestCase(ContentSyncStatus.NotPublished, "Not published")]
    [TestCase(ContentSyncStatus.OnlyOnTarget, "Only on target")]
    [TestCase(ContentSyncStatus.InSync, "In sync")]
    public void StatusLabel_ReturnsExpectedText(ContentSyncStatus status, string expected) =>
        Assert.That(ContentSyncStatusListingSupport.StatusLabel(status), Is.EqualTo(expected));

    // Everything a sync changes shares one color; in sync and only on target stand apart. (Incompatible,
    // red, is per item; see IncompatibleItem_ShowsIncompatible_InsteadOfItsStatus.)
    [TestCase(ContentSyncStatus.InSync, Color.SuccessBackgroundHighEmphasis)]
    [TestCase(ContentSyncStatus.OnlyOnTarget, Color.BackgroundTagGrey)]
    [TestCase(ContentSyncStatus.NotPublished, Color.BackgroundTagGrey)]
    [TestCase(ContentSyncStatus.Unpublished, Color.BackgroundTagKenticoOrange)]
    [TestCase(ContentSyncStatus.New, Color.BackgroundTagKenticoOrange)]
    [TestCase(ContentSyncStatus.Changed, Color.BackgroundTagKenticoOrange)]
    [TestCase(ContentSyncStatus.Moved, Color.BackgroundTagKenticoOrange)]
    [TestCase(ContentSyncStatus.Reordered, Color.BackgroundTagKenticoOrange)]
    public void StatusColor_ReturnsExpectedToken(ContentSyncStatus status, Color expected) =>
        Assert.That(ContentSyncStatusListingSupport.StatusColor(status), Is.EqualTo(expected));
}
