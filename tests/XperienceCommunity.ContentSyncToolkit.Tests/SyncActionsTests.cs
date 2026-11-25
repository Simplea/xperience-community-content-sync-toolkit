using System.Reflection;

using CMS.DataEngine;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.RequiredObjects;
using XperienceCommunity.ContentSyncToolkit.Sync;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// The Sync actions: which rows can go, how they're grouped, and how Kentico's sync engine is called on
// each supported version. See docs/specs/sync-actions.md.
public class SyncActionsTests
{
    private static readonly Guid itemGuid = Guid.NewGuid();

    private static ContentInventoryItem Inventory(string language = "en") =>
        new(itemGuid, ContentInventoryItemKind.WebPage, "DG.Article", "DancingGoatPages", language, "/Articles/A", DateTime.UtcNow, "Published");

    private static ContentSyncStatusItem Item(ContentSyncStatus status, bool local = true) =>
        new(itemGuid, status, local ? Inventory() : null, status == ContentSyncStatus.New ? null : Inventory());

    private static ContentSyncAvailability Evaluate(ContentSyncStatusItem item, bool canSynchronize = true, ContentSyncMovedItem? movedTo = null) =>
        ContentSyncRules.Evaluate(item, canSynchronize, movedTo, "page");

    [TestCase(ContentSyncStatus.New)]
    [TestCase(ContentSyncStatus.Changed)]
    [TestCase(ContentSyncStatus.Unpublished)]
    [TestCase(ContentSyncStatus.Moved)]
    [TestCase(ContentSyncStatus.Reordered)]
    public void Evaluate_AStatusASyncWouldChange_CanBeSynced(ContentSyncStatus status) =>
        Assert.That(Evaluate(Item(status)).CanSync, Is.True);

    [Test]
    public void Evaluate_InSync_HasNothingToSync()
    {
        var availability = Evaluate(Item(ContentSyncStatus.InSync));

        Assert.That(availability.CanSync, Is.False);
        Assert.That(availability.Reason, Is.EqualTo(ContentSyncRules.NothingToSync));
        Assert.That(availability.Summary, Is.EqualTo(ContentSyncRules.InSyncSummary));
    }

    [Test]
    public void Evaluate_NotPublished_AsksToPublishFirst() =>
        Assert.That(Evaluate(Item(ContentSyncStatus.NotPublished)).Reason, Is.EqualTo(ContentSyncRules.NoPublishedVersion));

    [Test]
    public void Evaluate_OnlyOnTarget_IsntOnThisInstance() =>
        Assert.That(Evaluate(Item(ContentSyncStatus.OnlyOnTarget, local: false)).Reason, Is.EqualTo(ContentSyncRules.NotOnThisInstance));

    [Test]
    public void Evaluate_MovedHere_PointsToTheNewWorkspace()
    {
        var availability = Evaluate(Item(ContentSyncStatus.OnlyOnTarget, local: false), movedTo: new ContentSyncMovedItem(itemGuid, "Events"));

        Assert.That(availability.Reason, Is.EqualTo("Sync it from its new workspace, Events."));
        Assert.That(availability.Summary, Is.EqualTo(ContentSyncRules.MovedSummary));
    }

    [Test]
    public void Evaluate_MovedToAWorkspaceTheUserCantSee_DoesntNameIt() =>
        Assert.That(
            Evaluate(Item(ContentSyncStatus.OnlyOnTarget, local: false), movedTo: new ContentSyncMovedItem(itemGuid, null)).Reason,
            Is.EqualTo("Sync it from the workspace it moved to."));

    [Test]
    public void Evaluate_Incompatible_CantBeSynced()
    {
        var item = Item(ContentSyncStatus.Changed) with
        {
            RequiredObjectIssues = [new RequiredObjectIssue(new RequiredObject(RequiredObjectKind.ContentType, Guid.NewGuid(), "DG.Article", "Article"), RequiredObjectProblem.MissingOnTarget)],
        };

        Assert.That(Evaluate(item).Summary, Is.EqualTo(ContentSyncRules.IncompatibleSummary));
    }

    // The permission is checked last: a row with nothing to sync says so, whoever looks at it.
    [Test]
    public void Evaluate_WithoutThePermission_SaysWhichPermission()
    {
        var availability = Evaluate(Item(ContentSyncStatus.Changed), canSynchronize: false);

        Assert.That(availability.CanSync, Is.False);
        Assert.That(availability.Reason, Is.EqualTo("You don't have the Synchronize permission for this page."));
    }

    [Test]
    public void Batches_SplitByLanguage_KeepingTheOrder()
    {
        var batches = ContentSyncRules.Batches(["a/en", "b/es", "c/EN"], row => row.Split('/')[1], ContentInventoryItemKind.ContentHubItem);

        Assert.That(batches.Select(batch => string.Join(",", batch)), Is.EqualTo(new[] { "a/en,c/EN", "b/es" }));
    }

    [Test]
    public void Batches_Pages_NoMoreThanKenticosRecommendedSize()
    {
        var batches = ContentSyncRules.Batches(Enumerable.Range(0, 450), _ => "en", ContentInventoryItemKind.WebPage);

        Assert.That(batches.Select(batch => batch.Count), Is.EqualTo(new[] { 200, 200, 50 }));
    }

    [Test]
    public void Batches_ContentItems_NoMoreThanKenticosRecommendedSize()
    {
        var batches = ContentSyncRules.Batches(Enumerable.Range(0, 1001), _ => "en", ContentInventoryItemKind.ContentHubItem);

        Assert.That(batches.Select(batch => batch.Count), Is.EqualTo(new[] { 1000, 1 }));
    }

    [Test]
    public void ParseRowId_ReadsTheGuidAndLanguage() =>
        Assert.That(ContentSyncRules.ParseRowId(ContentSyncRules.RowId(itemGuid, "en-US")), Is.EqualTo((itemGuid, "en-US")));

    [TestCase("message")]
    [TestCase("not-a-guid_en")]
    [TestCase("")]
    public void ParseRowId_AnythingElse_IsIgnored(string rowId) =>
        Assert.That(ContentSyncRules.ParseRowId(rowId), Is.Null);

    [TestCase("https://production.example.com/", "production.example.com")]
    [TestCase("https://localhost:27311", "localhost:27311")]
    public void TargetName_IsTheHost_WithAPortWhenThereIsOne(string url, string expected) =>
        Assert.That(ContentSyncRules.TargetName(new Uri(url)), Is.EqualTo(expected));

    [Test]
    public void DescribeSkipped_CountsEachReason() =>
        Assert.That(
            ContentSyncActionService.DescribeSkipped(new Dictionary<string, int> { ["in sync"] = 2, ["incompatible"] = 1 }),
            Is.EqualTo("Skipped 3: 2 in sync, 1 incompatible."));

    // Kentico's Create, as each supported version declares it; only the parameter names matter.
#pragma warning disable S1172, IDE0060 // Unused parameters: these stand-ins exist for their parameter names.
    private static Task<Guid> Create30(IReadOnlyCollection<int> contentItemCommonDataIds, string userName, int userId, DateTime? scheduledRestore, CancellationToken cancellationToken) =>
        Task.FromResult(Guid.Empty);

    private static Task<Guid> Create31(
        Func<IReadOnlyCollection<BaseInfo>> retrieveFunc, string userName, int userId, int languageId, string cultureCode, bool includeLinkedForms,
        DateTime? scheduledRestore, bool withSelectedDependencies, IReadOnlyCollection<int> synchronizationIdentifiers, CancellationToken cancellationToken) =>
        Task.FromResult(Guid.Empty);

    private static Task<Guid> CreateFuture(string somethingNew) => Task.FromResult(Guid.Empty);
#pragma warning restore S1172, IDE0060

    private static ParameterInfo[] Parameters(string method) =>
        typeof(SyncActionsTests).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters();

    private static readonly DateTime restoreAt = new(2026, 12, 1, 20, 0, 0, DateTimeKind.Utc);

    private static ContentSyncEngineRequest Request() => new([], [11, 12], 3, "editor", 53, restoreAt);

    [Test]
    public void CreateArguments_On30_PassesTheVersionIds()
    {
        object?[] arguments = ContentSyncEngine.CreateArguments(Parameters(nameof(Create30)), Request(), CancellationToken.None);

        Assert.That(arguments[0], Is.EqualTo(new[] { 11, 12 }));
        Assert.That(arguments[1], Is.EqualTo("editor"));
        Assert.That(arguments[2], Is.EqualTo(53));
        Assert.That(arguments[3], Is.EqualTo(restoreAt));
    }

    [Test]
    public void CreateArguments_On31_PassesTheLanguage_WithoutFormsOrDetailedSelection()
    {
        object?[] arguments = ContentSyncEngine.CreateArguments(Parameters(nameof(Create31)), Request(), CancellationToken.None);

        Assert.That(arguments[0], Is.InstanceOf<Func<IReadOnlyCollection<BaseInfo>>>());
        Assert.That(arguments[3], Is.EqualTo(3), "languageId");
        Assert.That(arguments[5], Is.False, "includeLinkedForms");
        Assert.That(arguments[6], Is.EqualTo(restoreAt), "scheduledRestore");
        Assert.That(arguments[7], Is.False, "withSelectedDependencies");
        Assert.That(arguments[8], Is.EqualTo(new[] { 11, 12 }), "synchronizationIdentifiers");
    }

    // Detailed selection: only the chosen items and what they can't go without.
    [Test]
    public void CreateArguments_On31_FromDetailedSelection_SendsOnlyTheSelectedDependencies()
    {
        object?[] arguments = ContentSyncEngine.CreateArguments(
            Parameters(nameof(Create31)), Request() with { WithSelectedDependencies = true }, CancellationToken.None);

        Assert.That(arguments[7], Is.True, "withSelectedDependencies");
    }

    // A future Xperience version with a parameter the toolkit doesn't know fails clearly, never guesses.
    [Test]
    public void CreateArguments_AnUnknownParameter_FailsClearly() =>
        Assert.That(
            () => ContentSyncEngine.CreateArguments(Parameters(nameof(CreateFuture)), Request(), CancellationToken.None),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("somethingNew"));

    [TestCase("Created", "Starting")]
    [TestCase("Ready", "WaitingForTarget")]
    [TestCase("Validating", "Transferring")]
    [TestCase("Restoring", "Restoring")]
    [TestCase("Completed", "Completed")]
    [TestCase("Failed", "Failed")]
    [TestCase("PollingTimeout", "TimedOut")]
    [TestCase("SomethingNew", "Unknown")]
    public void ToState_MapsKenticosStatus(string status, string expected) =>
        Assert.That(ContentSyncEngine.ToState(status).ToString(), Is.EqualTo(expected));

    // Kentico lists "The synchronization task is currently starting" under errors; that isn't a failure.
    [Test]
    public void Progress_MessagesWhileStarting_ArentAFailure() =>
        Assert.That(
            new ContentSyncProgress(ContentSyncProgressState.Starting, "Starting", null, [": The synchronization task is currently starting."]).Failed,
            Is.False);

    private static ContentSyncStatusItem Page(string path, ContentSyncStatus status, string? remotePath = null)
    {
        var guid = Guid.NewGuid();
        var local = new ContentInventoryItem(guid, ContentInventoryItemKind.WebPage, "DG.Article", "DancingGoatPages", "en", path, DateTime.UtcNow, "Published");
        var remote = status == ContentSyncStatus.New ? null : local with { TreePath = remotePath ?? path };
        return new ContentSyncStatusItem(guid, status, local, remote);
    }

    [Test]
    public void SiblingsToInclude_AReorderedPage_BringsTheRestOfItsLevel()
    {
        var reordered = Page("/Products/Espro", ContentSyncStatus.Reordered);
        var sibling = Page("/Products/AeroPress", ContentSyncStatus.InSync);
        var elsewhere = Page("/Articles/Coffee", ContentSyncStatus.InSync);
        var child = Page("/Products/Espro/Filters", ContentSyncStatus.InSync);

        var siblings = ContentSyncRules.SiblingsToInclude([reordered], [reordered, sibling, elsewhere, child]);

        Assert.That(siblings, Is.EqualTo(new[] { sibling }));
    }

    [Test]
    public void SiblingsToInclude_LeavesOutPagesThatCantBeSent()
    {
        var reordered = Page("/Products/Espro", ContentSyncStatus.Reordered);
        var draftOnly = Page("/Products/Draft", ContentSyncStatus.NotPublished);

        Assert.That(ContentSyncRules.SiblingsToInclude([reordered], [reordered, draftOnly]), Is.Empty);
    }

    [Test]
    public void SiblingsToInclude_NoReorderedPage_AddsNothing()
    {
        var changed = Page("/Products/Espro", ContentSyncStatus.Changed);

        Assert.That(ContentSyncRules.SiblingsToInclude([changed], [changed, Page("/Products/AeroPress", ContentSyncStatus.InSync)]), Is.Empty);
    }

    [Test]
    public void LevelsReorderedByNewPages_ANewPageNextToPagesOnTheTarget_NamesTheLevel()
    {
        var added = Page("/Products/Clone", ContentSyncStatus.New);

        Assert.That(
            ContentSyncRules.LevelsReorderedByNewPages([added], [added, Page("/Products/AeroPress", ContentSyncStatus.InSync)]),
            Is.EqualTo(new[] { "/Products" }));
    }

    // A new page whose level is empty on the target (a new parent, sent along) shifts nothing.
    [Test]
    public void LevelsReorderedByNewPages_ANewPageAloneOnItsLevel_NamesNothing()
    {
        var added = Page("/New_parent/Child", ContentSyncStatus.New);

        Assert.That(ContentSyncRules.LevelsReorderedByNewPages([added], [added, Page("/Products/AeroPress", ContentSyncStatus.InSync)]), Is.Empty);
    }

    [TestCase(null, "production.example.com")]
    [TestCase("  ", "production.example.com")]
    [TestCase("Production", "Production (production.example.com)")]
    public void InstanceName_ALabel_GoesWithTheHost(string? label, string expected) =>
        Assert.That(ContentSyncRules.InstanceName("production.example.com", label), Is.EqualTo(expected));

    [Test]
    public void RestoreTimeText_ShowsAUtcTimeInTheServersZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test-5", TimeSpan.FromHours(-5), "Test-5", "Test-5");

        string text = ContentSyncRules.RestoreTimeText(new DateTime(2026, 11, 21, 1, 2, 0, DateTimeKind.Utc), zone);

        Assert.That(text, Does.StartWith(new DateTime(2026, 11, 20, 20, 2, 0, DateTimeKind.Unspecified).ToString("g")).And.EndWith("(UTC-05:00)"));
    }

    [Test]
    public void RestoreTimeText_KeepsALocalTimeAsItIs()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

        string text = ContentSyncRules.RestoreTimeText(new DateTime(2026, 11, 20, 20, 2, 0, DateTimeKind.Unspecified), zone);

        Assert.That(text, Does.StartWith(new DateTime(2026, 11, 20, 20, 2, 0, DateTimeKind.Unspecified).ToString("g")).And.EndWith("(UTC+01:00)"));
    }

    [Test]
    public void InstancesCaption_NamesBothInstances() =>
        Assert.That(
            ContentSyncRules.InstancesCaption("staging.example.com", "Staging", new Uri("https://production.example.com/"), "Production"),
            Is.EqualTo("Staging (staging.example.com) → Production (production.example.com)"));

    // Without a request (no host to read), only the target is named.
    [Test]
    public void InstancesCaption_WithoutThisInstancesHost_NamesTheTarget() =>
        Assert.That(
            ContentSyncRules.InstancesCaption(null, null, new Uri("https://localhost:27311/"), null),
            Is.EqualTo("Compared with localhost:27311"));
}
