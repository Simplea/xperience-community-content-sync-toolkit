using System.Text.Json;

using CMS.ContentEngine;

using Kentico.Xperience.Admin.Base;

using XperienceCommunity.ContentSyncToolkit.Admin;
using XperienceCommunity.ContentSyncToolkit.Inventory;
using XperienceCommunity.ContentSyncToolkit.Sync;
using XperienceCommunity.ContentSyncToolkit.SyncStatus;

namespace XperienceCommunity.ContentSyncToolkit.Tests;

// Xperience's own Sync dialog on the status list: what the tabs answer its commands with, and how they
// read what it sends. See docs/specs/sync-actions.md, Using Xperience's own Sync dialog.
public class SyncDialogTests
{
    // As the dialog sends its data: camel case.
    private static readonly JsonSerializerOptions dialogJson = new(JsonSerializerDefaults.Web);

    private static readonly DateTime now = new(2026, 11, 21, 0, 40, 0, DateTimeKind.Utc);

    private static ContentInventoryItem Inventory(Guid guid, string path = "/Articles/A") =>
        new(guid, ContentInventoryItemKind.WebPage, "DG.Article", "DancingGoatPages", "en", path, DateTime.UtcNow, "Published");

    private static ContentSyncStatusItem Item(ContentSyncStatus status, Guid? guid = null)
    {
        var id = guid ?? Guid.NewGuid();
        return new(id, status, Inventory(id), status == ContentSyncStatus.New ? null : Inventory(id));
    }

    private static ContentSyncPlannedRow Sendable(int contentItemId, ContentSyncStatus status = ContentSyncStatus.Changed, bool addedForOrder = false)
    {
        var item = Item(status);
        return new(item.Guid, "en", contentItemId, null, item, ContentSyncAvailability.Available, addedForOrder);
    }

    private static ContentSyncPlannedRow Skipped(int contentItemId, ContentSyncStatus status, string reason, string summary)
    {
        var item = Item(status);
        return new(item.Guid, "en", contentItemId, null, item, ContentSyncAvailability.Unavailable(reason, summary));
    }

    // The review's Status cell, its last.
    private static ContentItemStatus StatusOf(Row row) =>
        ((ContentVersionStatusNamedComponentCellProps)((NamedComponentCell)row.Cells[row.Cells.Count - 1]).ComponentProps).Status;

    private static SyncDialogCreate Create(string json) => JsonSerializer.Deserialize<SyncDialogCreate>(json, dialogJson)!;

    // ---- What the dialog sends ------------------------------------------------------------------

    [Test]
    public void Create_ReadsTheTickedRowsAndThePostponedRestore()
    {
        var args = Create("""{"identifiers":["a_en","b_es"],"includeLinkedForms":false,"scheduledRestore":"2026-11-21T00:51:00.000Z"}""");

        Assert.That(args.Identifiers!.Select(id => id.GetString()), Is.EqualTo(new[] { "a_en", "b_es" }));
        Assert.That(args.ScheduledRestore, Is.EqualTo(new DateTimeOffset(2026, 11, 21, 0, 51, 0, TimeSpan.Zero)));
    }

    [Test]
    public void Create_FromDetailedSelection_ReadsTheChosenContentItems_AsNumbersOrText()
    {
        var args = Create("""{"selectedIdentifiers":{"ContentItem":[121,"141","folder"]}}""");

        Assert.That(ContentSyncStatusTabBase.SelectedContentItemIds(args), Is.EquivalentTo(new[] { 121, 141 }));
    }

    [Test]
    public void Restore_None_RestoresImmediately()
    {
        bool valid = ContentSyncStatusTabBase.TryReadRestore(Create("{}"), now, out var restore, out string? error);

        Assert.That(valid, Is.True);
        Assert.That(restore, Is.Null);
        Assert.That(error, Is.Null);
    }

    // The dialog sends UTC; the engine is given UTC.
    [Test]
    public void Restore_AFutureTime_IsKeptInUtc()
    {
        bool valid = ContentSyncStatusTabBase.TryReadRestore(
            Create("""{"scheduledRestore":"2026-11-20T20:51:00-05:00"}"""), now, out var restore, out _);

        Assert.That(valid, Is.True);
        Assert.That(restore, Is.EqualTo(new DateTime(2026, 11, 21, 1, 51, 0, DateTimeKind.Utc)));
        Assert.That(restore!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    // The dialog asks for 10 minutes ahead, but the dialog can stay open past that.
    [Test]
    public void Restore_ATimeAlreadyPast_IsRefused()
    {
        bool valid = ContentSyncStatusTabBase.TryReadRestore(
            Create("""{"scheduledRestore":"2026-11-21T00:39:00Z"}"""), now, out _, out string? error);

        Assert.That(valid, Is.False);
        Assert.That(error, Is.EqualTo("Choose a restore time in the future."));
    }

    // The review's rows can be unticked; nothing outside what the editor opened it with can be added.
    [Test]
    public void Ticked_KeepsOnlyTicked_FromTheSelection()
    {
        var selection = new ContentSyncDialogSelection(["a_en", "b_en"]);
        selection.MarkShown(["a_en", "b_en"]);

        Assert.That(selection.Ticked(["B_EN", "c_en"]), Is.EqualTo(new[] { "b_en" }));
    }

    [Test]
    public void Ticked_NoneSent_IsTheWholeSelection() =>
        Assert.That(new ContentSyncDialogSelection(["a_en", "b_en"]).Ticked(null), Is.EqualTo(new[] { "a_en", "b_en" }));

    // The dialog sends only the ticked rows of review pages the editor opened: a row never shown
    // couldn't have been unticked, so it goes.
    [Test]
    public void Ticked_ARowTheReviewNeverShowed_Goes()
    {
        var selection = new ContentSyncDialogSelection(["a_en", "b_en", "c_en"]);
        selection.MarkShown(["a_en", "b_en"]);

        Assert.That(selection.Ticked(["a_en"]), Is.EqualTo(new[] { "a_en", "c_en" }));
    }

    [Test]
    public void Ticked_NothingTicked_SendsNothing()
    {
        var selection = new ContentSyncDialogSelection(["a_en", "b_en"]);
        selection.MarkShown(["a_en"]);

        Assert.That(selection.Ticked([]), Is.Empty);
    }

    // The review sends its rows again with each page; that mustn't forget what was shown.
    [Test]
    public void SelectionStore_TheSameRows_KeepWhatWasShown()
    {
        var store = new ContentSyncDialogSelectionStore();
        store.Remember(1, "pages", ["a_en", "b_en"]).MarkShown(["a_en"]);

        var again = store.Remember(1, "pages", ["a_en", "b_en"]);

        Assert.That(again.Ticked(["b_en"]), Is.EqualTo(new[] { "b_en" }), "a_en was shown and unticked");
    }

    [Test]
    public void SelectionStore_OtherRows_StartOver()
    {
        var store = new ContentSyncDialogSelectionStore();
        store.Remember(1, "pages", ["a_en", "b_en"]).MarkShown(["a_en", "b_en"]);

        var reopened = store.Remember(1, "pages", ["a_en", "c_en"]);

        Assert.That(reopened.Ticked(["c_en"]), Is.EqualTo(new[] { "a_en", "c_en" }), "nothing of the new selection was shown yet");
    }

    // ---- The review list ------------------------------------------------------------------------

    [Test]
    public void ReviewRow_ARowThatGoes_IsTickable_WithItsComparison()
    {
        var row = ContentSyncStatusTabBase.ReviewRow(Sendable(141), withLanguage: false);
        var status = StatusOf(row);

        Assert.That(row.Disabled, Is.False);
        Assert.That(row.InactiveMessage, Is.Null);
        Assert.That(status.Label, Is.EqualTo("Changed"));
        Assert.That(status.IconName, Is.EqualTo(Icons.ArrowSend));
    }

    // The greyed-out row shows the reason itself; a status tooltip would show it twice.
    [Test]
    public void ReviewRow_ARowThatCantGo_IsDisabled_WithTheReason_AndNoStatusTooltip()
    {
        var row = ContentSyncStatusTabBase.ReviewRow(
            Skipped(141, ContentSyncStatus.NotPublished, ContentSyncRules.NoPublishedVersion, ContentSyncRules.NotPublishedSummary), withLanguage: false);
        var status = StatusOf(row);

        Assert.That(row.Disabled, Is.True);
        Assert.That(row.InactiveMessage, Is.EqualTo(ContentSyncRules.NoPublishedVersion));
        Assert.That(status.Label, Is.EqualTo("Not published"));
        Assert.That(status.ParametrizedTooltip?.TooltipTemplate, Is.Null);
    }

    // As the list labels it.
    [Test]
    public void ReviewRow_OnlyOnTarget_InThisInstancesRecycleBin_IsDeletedHere()
    {
        var item = Item(ContentSyncStatus.OnlyOnTarget);
        var row = new ContentSyncPlannedRow(
            item.Guid, "en", null, null, item, ContentSyncAvailability.Unavailable(ContentSyncRules.NotOnThisInstance, ContentSyncRules.OnlyOnTargetSummary));

        Assert.That(StatusOf(ContentSyncStatusTabBase.ReviewRow(row, withLanguage: false, deletedHere: true)).Label, Is.EqualTo("Deleted here"));
        Assert.That(StatusOf(ContentSyncStatusTabBase.ReviewRow(row, withLanguage: false)).Label, Is.EqualTo("Only on target"));
    }

    [Test]
    public void SkippedLabel_InSync_UsesTheComparisonsLabel() =>
        Assert.That(
            ContentSyncStatusTabBase.SkippedLabel(Skipped(1, ContentSyncStatus.InSync, ContentSyncRules.NothingToSync, ContentSyncRules.InSyncSummary)),
            Is.EqualTo("In sync"));

    [Test]
    public void SkippedLabel_OtherwiseTheReason_Capitalized() =>
        Assert.That(
            ContentSyncStatusTabBase.SkippedLabel(Skipped(1, ContentSyncStatus.Changed, "No permission.", ContentSyncRules.NoPermissionSummary)),
            Is.EqualTo("Without the Synchronize permission"));

    [Test]
    public void ReviewRow_ASiblingKeepingTheOrder_SaysWhy()
    {
        var row = ContentSyncStatusTabBase.ReviewRow(Sendable(1, ContentSyncStatus.InSync, addedForOrder: true), withLanguage: true);
        var status = StatusOf(row);

        Assert.That(((StringCell)row.Cells[0]).Value, Is.EqualTo("/Articles/A (sibling, keeps the order) (en)"));
        Assert.That(status.ParametrizedTooltip?.TooltipTemplate, Is.EqualTo("Included to keep the order of the pages on its level."));
    }

    [Test]
    public void ReviewRows_WhatGoesComesFirst()
    {
        var notPublished = Skipped(1, ContentSyncStatus.NotPublished, ContentSyncRules.NoPublishedVersion, ContentSyncRules.NotPublishedSummary);
        var changed = Sendable(2);
        var plan = new ContentSyncPlan([notPublished, changed], []);

        Assert.That(
            ContentSyncStatusTabBase.ReviewRows(plan, withLanguage: false).Select(row => row.Identifier),
            Is.EqualTo(new object[] { changed.RowId, notPublished.RowId }));
    }

    // ---- Detailed selection ---------------------------------------------------------------------

    [Test]
    public void ChooseDetailed_AnUntickedRow_IsLeftOut_AndTickedDependenciesGo()
    {
        var plan = new ContentSyncPlan([Sendable(10), Sendable(20)], []);

        var (chosen, dependencies) = ContentSyncStatusTabBase.ChooseDetailed(plan, new HashSet<int> { 10, 99, 55 });

        Assert.That(chosen.Sendable.Select(row => row.ContentItemId), Is.EqualTo(new int?[] { 10 }));
        Assert.That(chosen.Rows[1].Availability.Reason, Is.EqualTo("Not selected."));
        Assert.That(dependencies, Is.EqualTo(new[] { 55, 99 }));
    }

    // A row that couldn't go keeps its own reason, ticked or not.
    [Test]
    public void ChooseDetailed_ARowThatCantGo_KeepsItsReason()
    {
        var plan = new ContentSyncPlan([Skipped(10, ContentSyncStatus.InSync, ContentSyncRules.NothingToSync, ContentSyncRules.InSyncSummary)], []);

        var (chosen, _) = ContentSyncStatusTabBase.ChooseDetailed(plan, new HashSet<int>());

        Assert.That(chosen.Rows[0].Availability.Reason, Is.EqualTo(ContentSyncRules.NothingToSync));
    }

    [Test]
    public void DetailedRow_WithoutAPublishedVersion_IsDisabled()
    {
        var row = ContentSyncStatusTabBase.DetailedRow(new ContentSyncDialogItem(5, "Draft", "Article", "Pages", null, null, null, false));

        Assert.That(row.Disabled, Is.True);
        Assert.That(row.InactiveMessage, Is.EqualTo("It has no published version, so it isn't sent."));
    }

    // As Xperience's own Detailed selection marks it: the published version goes, not the draft.
    [Test]
    public void DetailedStatus_PublishedWithANewerDraft_SaysThePublishedVersionGoes()
    {
        var status = ContentSyncStatusTabBase.DetailedStatus(
            new ContentSyncDialogItem(5, "Coffee", "Coffee", null, "Default", null, VersionStatus.Published, HasNewerDraft: true));

        Assert.That(status.Label, Is.EqualTo("Published"));
        Assert.That(status.SecondaryIcons, Has.Count.EqualTo(1));
        Assert.That(status.SecondaryIcons![0].ParametrizedTooltip?.TooltipTemplate, Is.EqualTo("The latest published version will be synchronized."));
    }

    [TestCase(VersionStatus.Unpublished, "Unpublished")]
    [TestCase(null, "Not published")]
    public void DetailedStatus_NothingPublished(VersionStatus? sent, string label) =>
        Assert.That(
            ContentSyncStatusTabBase.DetailedStatus(new ContentSyncDialogItem(5, "X", "X", null, null, null, sent, false)).Label,
            Is.EqualTo(label));

    // ---- The restore form -----------------------------------------------------------------------

    [Test]
    public void PublishForm_Immediately_HasOnlyTheChoice()
    {
        var items = ContentSyncStatusTabBase.PublishFormItems(restoreNow: true, scheduledTime: null);

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That((string?)items[0]!["value"], Is.EqualTo("True"));
    }

    [Test]
    public void PublishForm_Postponed_AsksForTheTime_RequiredUntilGiven()
    {
        var empty = ContentSyncStatusTabBase.PublishFormItems(restoreNow: false, scheduledTime: null);
        var given = ContentSyncStatusTabBase.PublishFormItems(restoreNow: false, scheduledTime: "2026-11-21T01:00:00Z");

        Assert.That((string?)empty[0]!["value"], Is.EqualTo("False"));
        Assert.That((string?)empty[1]!["name"], Is.EqualTo("ScheduledTime"));
        Assert.That((bool?)empty[1]!["validationRules"]![2]!["isValid"], Is.False);
        Assert.That((string?)given[1]!["value"], Is.EqualTo("2026-11-21T01:00:00Z"));
        Assert.That((bool?)given[1]!["validationRules"]![2]!["isValid"], Is.True);
    }

    // ---- Progress -------------------------------------------------------------------------------

    private static ContentSyncProgress Progress(ContentSyncProgressState state) => new(state, state.ToString(), null, []);

    [Test]
    public void ShownProgress_AFailure_IsShownFirst() =>
        Assert.That(
            ContentSyncStatusTabBase.ShownProgress([Progress(ContentSyncProgressState.Completed), Progress(ContentSyncProgressState.Failed)]).State,
            Is.EqualTo(ContentSyncProgressState.Failed));

    [Test]
    public void ShownProgress_OtherwiseTheOneFurthestBehind() =>
        Assert.That(
            ContentSyncStatusTabBase.ShownProgress(
                [Progress(ContentSyncProgressState.Completed), Progress(ContentSyncProgressState.Restoring), Progress(ContentSyncProgressState.WaitingForTarget)]).State,
            Is.EqualTo(ContentSyncProgressState.WaitingForTarget));

    [Test]
    public void ShownProgress_AllComplete_IsComplete() =>
        Assert.That(
            ContentSyncStatusTabBase.ShownProgress([Progress(ContentSyncProgressState.Completed), Progress(ContentSyncProgressState.Completed)]).State,
            Is.EqualTo(ContentSyncProgressState.Completed));

    // The dialog's progress panel reads Kentico's status names.
    [TestCase("Starting", "Created")]
    [TestCase("WaitingForTarget", "Ready")]
    [TestCase("Transferring", "ContentDataUploading")]
    [TestCase("Restoring", "Restoring")]
    [TestCase("Completed", "Completed")]
    [TestCase("Failed", "Failed")]
    [TestCase("TimedOut", "PollingTimeout")]
    [TestCase("Unknown", "Created")]
    public void KenticoStatus_IsWhatTheProgressPanelReads(string state, string expected) =>
        Assert.That(ContentSyncStatusTabBase.KenticoStatus(Enum.Parse<ContentSyncProgressState>(state)), Is.EqualTo(expected));

    // ---- Several syncs, one dialog --------------------------------------------------------------

    [Test]
    public void Tracker_OneSync_IsFollowedByItsOwnGuid()
    {
        var tracker = new ContentSyncTracker(TimeProvider.System);
        var sync = Guid.NewGuid();

        Assert.That(tracker.AddGroup([sync]), Is.EqualTo(sync));
        Assert.That(tracker.SyncsOf(sync), Is.EqualTo(new[] { sync }));
    }

    [Test]
    public void Tracker_SeveralSyncs_AreFollowedAsOne()
    {
        var tracker = new ContentSyncTracker(TimeProvider.System);
        Guid[] syncs = [Guid.NewGuid(), Guid.NewGuid()];

        var group = tracker.AddGroup(syncs);

        Assert.That(group, Is.Not.EqualTo(syncs[0]).And.Not.EqualTo(syncs[1]));
        Assert.That(tracker.SyncsOf(group), Is.EqualTo(syncs));
    }

    [Test]
    public void SelectionStore_IsPerEditorAndTab()
    {
        var store = new ContentSyncDialogSelectionStore();
        store.Remember(1, "pages", ["a_en"]);
        store.Remember(2, "pages", ["b_en"]);

        Assert.That(store.Get(1, "pages")!.RowIds, Is.EqualTo(new[] { "a_en" }));
        Assert.That(store.Get(2, "pages")!.RowIds, Is.EqualTo(new[] { "b_en" }));
        Assert.That(store.Get(1, "content-hub"), Is.Null);
    }
}
