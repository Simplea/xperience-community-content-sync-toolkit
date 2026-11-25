# Feature specification: Sync from the status list

Status: **implemented** (November 2026), on `31.9.1`; `30.8.0` checked by the spike. Researched in a
spike and reviewed before building (see Decisions). Where the build differs from the first draft, this
spec says how and why.

## Context

The status pages show what a sync would change, but syncing still happens elsewhere: the editor finds
each page in the page tree or each item in Content hub and uses Xperience's **Sync** there. With dozens of
pending rows across sections and languages, that is the slow part.

This spec covers syncing from the list, following each sync, and recording it. Showing which target the
list compares with has its own spec ([target-banner.md](target-banner.md)).

## Research findings

From a spike on the repository's rig (branch `spike/sync-actions`, not merged): a **Sync** button per
row and a **Sync selected** mass action on both tabs, calling Xperience's sync engine. Checked on
`31.9.1` and `30.8.0`, against Kentico's documentation
([Content sync](https://docs.kentico.com/documentation/business-users/content-sync),
[Content sync configuration](https://docs.kentico.com/documentation/developers-and-admins/configuration/content-sync-configuration),
[Page permission management](https://docs.kentico.com/documentation/developers-and-admins/configuration/users/role-management/page-permission-management)).
Kentico's software was only inspected through its public signatures and by experiment; nothing was
decompiled.

### The sync engine

- **Entry point.** Kentico's own Sync dialogs use internal command managers, which can't be called. The
  usable entry point is `CMS.ContentSynchronization.Internal.ISynchronizationService`: a public interface
  in an unsupported (`.Internal`) namespace, registered in the container.
- **`Create` differs by version.**
  - `30.8.0`: `Create(contentItemCommonDataIds, userName, userId, scheduledRestore, ct)`.
  - `31.9.1`: `Create(retrieveFunc, userName, userId, languageId, cultureCode, includeLinkedForms,
    scheduledRestore, withSelectedDependencies, synchronizationIdentifiers, ct)`. Earlier checks found
    it changed in `30.9.0` and `31.5.3`.
  - The spike binds the call by parameter name at run time, so one build works on both; an unknown
    parameter fails with a clear error.
- **What it syncs.** Language versions: `ContentItemCommonDataInfo` rows, the **published or
  unpublished** one (status 2 or 3). A row with only a draft (**Not published**) has nothing to send.
  Pages are synced the same way, through the page's content item.
- **Dependencies.** Linked items come along, as in Kentico's dialog. **Missing parent pages come along
  too**: syncing only `/Spike_parent/Spike_child` also created the new `/Spike_parent` on the target.
- **One language per sync.** `31.9.1` takes the language as a parameter; Kentico's dialogs also sync one
  language at a time.
- **`GetProgress(syncGuid)`** works on both versions. Statuses seen: `Created` ("The synchronization
  task is currently starting") → `Ready` ("Waiting for restoration … waiting for the target instance to
  start restoring") → `Restoring` → `Completed`, or `Failed` with errors. `PollingTimeout` exists on
  `31.9.1` only.
- **Timing.** 4–32 s from start to `Completed`, nearly all of it waiting for the target to pick the sync
  up (Kentico: "usually starts within 30 seconds"). 300 Content hub items in one sync: 32 s.
- **Failures are immediate and clear.** Syncing the **Incompatible** Home page failed after 0.9 s:
  "Validating … Compatibility error: Content type Home page has different field definitions on the
  source and target instance."
- **Kentico records each sync** in `CMS_Synchronization`, the **List of content synchronizations**: user,
  times, status, direction and result. **Not which items it included.**

### Statuses, synced one row at a time

| Row | Result |
| --- | --- |
| Changed page, Changed item | In sync |
| New page (parent on the target) | In sync |
| New page whose parent is also new | In sync, and so is the parent |
| Unpublished page | Unpublished on the target, no longer pending |
| Changed Spanish version | In sync; only the Spanish version was sent |
| Moved page | In sync (moved on the target) |
| Reordered page | In sync |
| Incompatible page | Fails in validation, with Kentico's message |
| Not published (draft only) | Nothing to send |

**Side effect:** adding a new page under `/Products` shifted its siblings' order on the target, so
`/Products/AeroPress_Filters` then showed **Reordered** (with `ComparePageOrder` on). This is Kentico's
"sync all items on the same level" rule.

### The listing

- **Row buttons.** A row can carry its own actions in a public `ActionCell` (`IList<Action>`, each with
  `Disabled` and `Title`). The listing must also declare them in `PageConfiguration.TableActions`: without
  it, Xperience's client fails ("Cannot read properties of undefined (reading 'visible')") and the page
  shows "Something went wrong". The rows' cells then match the table action.
- **Commands with our identifiers.** Row commands receive the row identifier as a string
  (`{guid}_{language}`); mass actions receive `IEnumerable<string>`.
- **Selection.** Declaring a mass action adds a checkbox column, a select-all checkbox for the page, and
  an item count. **The selection survives paging**: rows ticked on pages 1 and 2 arrived together.
- **Selection isn't filtered.** Ticked **In sync** rows are sent too; the server must check each row's
  status again.

### Permissions

From Kentico's documentation:

- **Content hub items:** the **Synchronize** permission for Content hub in the item's workspace (stored as
  a workspace permission, like View).
- **Pages:** the **Synchronize** page permission (which needs **Read**) on the page, or **Manage
  permissions** on the website channel.
- Administrators have both.

The toolkit already evaluates View per workspace and Display per page; Synchronize is the same check with
another permission name.

## Goal

Let editors sync from the status list, with the same results, permissions and safety as Xperience's own
Sync, and see what happened afterwards:

- sync **one row** or the **selected rows**, seeing what goes before anything is sent;
- only rows that a sync would change, and only for editors allowed to sync them;
- follow each sync until it completes or fails;
- an audit record of who synced which items.

## Design

### Which rows can be synced

A row's Sync button is enabled, and the row is included in a multi-row sync, when its status needs a push
and its version can be sent:

| Status | Sync | Reason shown when disabled |
| --- | --- | --- |
| New, Changed, Unpublished, Moved, Reordered | Yes | |
| In sync (with or without a newer draft) | No | "Nothing to sync." |
| Not published | No | "No published version to sync: publish it first." |
| Incompatible | No | The existing tooltip (the content type must be deployed first) |
| Only on target, Deleted here | No | "It isn't on this instance." |
| Moved here | No | "Sync it from its new workspace, *name*." |

Rows the editor isn't allowed to sync are disabled with "You don't have the Synchronize permission for
this *page / workspace*."

The server applies the same rules again when a command arrives, from a fresh comparison, so a stale page
or a ticked In sync row can't sync anything it shouldn't.

### Page order

Kentico syncs a page's position along with the page, and its guidance is to sync all pages on the same
level of the tree to get the order right.

- **Reordered rows include their siblings.** Syncing a Reordered page also syncs the other pages under
  the same parent, in the same language, that exist here (their published or unpublished versions). The
  confirmation says so: "Includes 6 sibling pages, to keep their order."
- **New pages get a warning.** Adding a page to a level changes its siblings' order on the target (seen
  in the spike: a new `/Products` page made `/Products/AeroPress_Filters` show Reordered). When page order
  is compared (`ComparePageOrder`), the confirmation warns: "Adding these pages changes the order of their
  siblings on the target: *Products* (5 pages). Their siblings will show Reordered; sync them to match."
  Without `ComparePageOrder`, order isn't shown in the list, so there's no warning.

### One way to sync

1. **Row:** the **Sync** button in the row's Actions column.
2. **Selected:** tick rows, then **Sync** (**Sync selected** without Xperience's dialog).

Each shows what goes before anything is sent: Xperience's Sync dialog where the toolkit answers it (see
Using Xperience's own Sync dialog), otherwise a confirmation (below).

**Everything pending** is a selection too, as Kentico's own workflow is ("filter, select all the filtered
items, Sync"): tick **Hide items in sync** (or choose statuses), select all on each page, and Sync once.
A selection carries across pages, as on Content hub (checked on `31.9.1`: select all on page 1, then on
page 2, gives 200 at 100 rows per page). The list offers 10, 25, 50 and 100 rows per page and opens at
25, like Content hub (Xperience would open it at the largest). Any row can be ticked, as on Content hub:
the dialog lists the rows that go first, then those that don't, greyed out with the reason, and sends
only what can go. A large selection is split into syncs of Kentico's recommended size (see Size limits).

Decided in review (November 2026): there's no **Sync all pending** action. It was the only sync confirmed
without seeing what goes, and its confirmation couldn't say how many rows it covered (Xperience fixes the
text when the page loads, before the filters change). Xperience's dialog can't host it either: as a
header action, the dialog fails before sending any command (`Cannot read properties of undefined (reading
'selectedIdentifiers')`), since it needs the rows a row or a selection gives it.

### One sync per language

Content Sync syncs one language at a time. A selection with several languages becomes one sync per
language, started one after the other, and the result message lists each.

### Size limits

Kentico recommends at most about 1,000 content items or 200 pages per sync. Large selections are split into syncs of at most **1,000 items** (Content hub tab) or **200 pages** (Pages tab),
started one after the other. The confirmation says so: "These 340 pages will go in 2 syncs."

### Confirmation

Xperience's confirmation dialog, before anything is sent, where Xperience's own Sync dialog isn't
available (see Versions):

- **What and where:** "Sync this page to *target*?" or "Sync the selected pages to *target*?". The target
  is named by host, with the port when its address has one.
- **A note:** "Content Sync sends the published version, with the items it links to and any missing parent
  pages."
- **When to restore,** for **Sync selected**: **Immediately** (default) or **At a
  scheduled time** (`scheduledRestore`), as in Kentico's dialog, through a confirmation form
  (`[CommandConfirmationModel]`).
- **A row's Sync restores immediately.** A row's own button can only carry a text confirmation: the form
  fields Xperience builds from a confirmation model are generated internally, so a row can't offer the
  scheduled time. To schedule one row, tick it and use **Sync selected**.
- **The page order notes,** when they apply (see Page order).
- **Buttons:** **Sync** and **Cancel**.

The dialog's text is fixed when the page loads, so it can't count the selection. What was sent and what
was skipped come in the result message instead: "Sync started: 2 pages (English) to *target*. … Skipped 2:
1 incompatible, 1 in sync."

### While a sync runs

The command starts the sync(s) and waits up to **5 seconds** for the first result, which catches
validation failures such as an incompatible content type. Then it answers:

- **Started:** "Sync started for 12 items. The target usually restores it within 30 seconds."
- **Failed:** Kentico's error, for example the compatibility message.
- **Scheduled:** "Sync scheduled for *date and time*."

The toolkit keeps a list of the syncs it started (`ContentSyncTracker`, in memory, per instance), each
with its sync GUID, user, language and rows. A background service (`ContentSyncProgressService`) polls
`GetProgress` every 3 seconds for each until it ends (`Completed`, `Failed` or `PollingTimeout`; it gives
up after 2 hours), then:

- writes the audit entry (below);
- clears the cached target inventory, so the next load shows the new statuses without **Refresh**;
- keeps the outcome for 24 hours, for the follow-up below.

**Follow-up in the list:**

- Rows in a running sync show a **Syncing** tag (grey) instead of their status, with the progress as
  tooltip ("Waiting for the target to restore it").
- After a failed sync, a callout at the top of the tab names the failure, how many rows it had and who
  started it, until those rows are synced again or 24 hours pass.
- A row in a running sync can't be synced again until it ends ("A sync of it is in progress.").
- The list doesn't update by itself; reloading or **Refresh** shows the latest state. (A live update
  would need a custom client component; see Out of scope.)

### Audit

Event log entries with source `ContentSyncToolkit`:

- `SYNCSTARTED` (information): user, sync GUID, language, scope (channel or workspace), scheduled time, and
  the items: name or path and GUID, one per line.
- `SYNCCOMPLETED` (information) or `SYNCFAILED` (error): sync GUID, final status, Kentico's messages.

Kentico's own **List of content synchronizations** already records the sync itself; these entries add
which items it included and that it came from the status list.

### Setting

`ContentSyncToolkit:SyncActions` (default **true**) turns all three actions off, for teams that want the
list read-only. Without the Synchronize permission the buttons are disabled anyway.

`ContentSyncToolkit:TargetLabel` names the target in the confirmations and result messages, as in
[target-banner.md](target-banner.md).

### Implementation notes

- **One adapter** wraps `ISynchronizationService`: it binds `Create` by parameter name, maps the request
  to whichever signature the version has, and reads progress. It's the only code touching the
  `.Internal` namespace, so a Kentico Refresh that changes it breaks one class, with a test that fails.
- **Resolving rows** batches the lookups (row GUIDs → content items → published or unpublished language
  versions); the spike did three queries per row. Pages and content items are read by object type name
  (`cms.webpageitem`, `cms.contentitem`, `cms.contentitemcommondata`), as elsewhere in the toolkit, so
  only the engine adapter uses an `.Internal` namespace.
- **Commands** live on `ContentSyncStatusTabBase` with the other commands; the rows get an `ActionCell`,
  and `PageConfiguration.TableActions` declares the matching action.
- **Components:** `ContentSyncEngine` (the adapter), `ContentSyncRules` (which rows can go, batches, page
  order; no Xperience dependencies, unit tested), `ContentSyncActionService` (a command, end to end),
  `ContentSyncTracker` and `ContentSyncProgressService` (follow-up), `ContentSyncConfirmationModel`.

## Using Xperience's own Sync dialog

Decided after the first build (November 2026): the Sync actions open **Xperience's own Sync dialog**, the
one Content hub and the page tree use (restore now or postponed, the list of what goes, Detailed
selection, the progress panel), instead of the toolkit's confirmation and messages. Editors get the
experience they know. The toolkit keeps what Xperience's dialog doesn't do: rows from several languages
and pages of the list, page order, the server-side checks, the Syncing tags and the
audit entries.

### How the dialog works

Observed on `31.9.1` from the administration's own traffic (the configuration Xperience sends to the
browser and the commands the dialog sends); nothing was decompiled.

- **The dialog** is the client component `@kentico/xperience-admin-base/ContentSynchronization`. Its server
  side, `Kentico.Xperience.Admin.Base.UIPages.Internal.ContentSynchronizationComponent`, is public and holds
  only configuration: a table configuration (`ContentSynchronizationTableClientProperties`) and the content
  type (`ContentSynchronizationTypeEnum`: `WebPages`, `ContentItems`, `Forms`). Content hub adds it as a
  mass action of type `customComponent`.
- **All its work is page commands on the page that hosts it.** On Content hub and the page tree, Xperience's
  internal page classes answer them; on the status list, the toolkit's tabs do. Argument types are public
  (`ContentSynchronizationCreateArguments`, `LoadSynchronizationTableItemsCommandArguments`, …); result
  types are internal, so the toolkit returns the same JSON shapes. Arguments are read with
  `System.Text.Json`, camel case.

| Command | Data sent | Result |
| --- | --- | --- |
| `GetPublishForm` | — | `{ items: [ RadioGroup "RestoreNow": "True" (immediately) / "False" (postpone) ] }` |
| `ChangePublishForm` | `{ data: { RestoreNow, ScheduledTime }, changedFieldNames }` | the form again; with `RestoreNow` "False" it adds a required DateTimeInput `ScheduledTime` ("Date and time") |
| `LoadContentSynchronizationInitialData` | — | `{ hasFormSyncPermission }` |
| `GetContentItemIds` (pages) | `{ webPageItemIds: [11] }` | `{ webPageToContentItemIds: { "11": 141 } }` |
| `LoadSynchronizationContentItems` (items) | a listing request (`currentPage`, `pageSize`, `searchTerm`, `columns`, …) plus `identifiers` | `{ rows, totalCount }`: Name, Content type, Status (`ContentVersionStatus` cell) |
| `LoadWebPagesSynchronizationContentItems` (pages) | `{ languageName, includeSubPages, identifiers }` (content item IDs) | `{ root }`: a tree of `{ contentItemId, title, tooltip, disabled, isFolder, status, children }`, parents included |
| `GetContentSynchronizationTable` | — | `{ tableConfiguration }` for Detailed selection: Name, Content type, Status, Channel, Workspace, ObjectType (hidden), Actions |
| `LoadDetailedSynchronizationItems` | a listing request plus `identifiers` (and `languageName` for pages) | `{ rows, totalCount }`: the selected items and everything that goes with them; `ObjectType` "ContentItem" |
| `CreateContentSynchronization` | `{ identifiers, includeLinkedForms }`: the rows ticked in the review; plus `scheduledRestore` (UTC, e.g. `"2026-11-21T00:51:00.000Z"`) when postponed, and `languageName` for pages | `{ success, synchronizationGuid, formItems }` |
| `CreateContentSynchronizationWithSelectedDependencies` | `{ selectedIdentifiers: { ContentItem: [121, 141] } }` (plus `scheduledRestore` when postponed, `languageName` for pages) | as above |
| `GetContentSynchronizationProgress` | `{ synchronizationGuid }` | `{ status, title, description, errors }`, Kentico's `SynchronizationProgress` as JSON |

The language comes from the page's address on Content hub (`content-hub/1/en/…`) and from `languageName`
on pages. The status list has no language in its address, so the tabs carry it in the row identifiers
(`{guid}_{language}`) through every command.

### On the status list

Built and checked on `31.9.1` (November 2026): restore now and postponed, unticking rows in the review,
Detailed selection (only the chosen item went across; the item it links to stayed Changed), pages and
Content hub items, the progress panel to "Sync successfully finished", and the Event log entries.

- **Sync** on ticked rows and **each row's Sync button** open the dialog: the mass action and the table
  action are added with `AddActionWithCustomComponent`, and each row's `ActionCell` carries an action of
  type `CustomComponent` with the dialog's client properties (disabled, with the reason, when the row
  can't be synced). From a row, the dialog sends that row's identifier. Each editor's selection is
  remembered per tab (`ContentSyncDialogSelectionStore`), because Detailed selection's commands don't
  carry the rows.
- **The review list** shows every selected row with the toolkit's comparison status: first what goes,
  then, disabled with the reason, what doesn't (In sync, Not published, no permission, already syncing).
  The siblings a Reordered page takes along are listed too. Create syncs the rows still ticked.
- **The dialog's Sync only sends the ticked rows of review pages the editor opened** (seen on `31.9.1`:
  with a page size of one, a row on page 2 wasn't sent). The review keeps Xperience's 25 rows per page,
  and the server keeps which rows it showed (`ContentSyncDialogSelection`): a row never shown goes, a
  shown row goes only if still ticked, and nothing ticked sends nothing.
- **Opened from a listing, the dialog always uses its list**, on Pages too: the page tree's version (a
  tree of pages with their parents, `GetContentItemIds` and `LoadWebPagesSynchronizationContentItems`)
  is only opened from the page tree, so the tabs don't answer those commands.
- **Detailed selection** lists the selected rows and their dependencies as Xperience's
  `IDependencyRetriever` works them out (by reflection; `31.x` only), one language at a time, with each
  item's channel or workspace and a link to its editor.
- **Several syncs, one progress:** a selection in several languages, or over the size limits, starts
  several syncs; the dialog follows them under one GUID (`ContentSyncTracker.AddGroup`), showing the
  first failure or the one furthest behind.
- **Messages in command responses aren't shown** by the dialog, so the plan's notes (siblings added, new
  pages changing the order of their level) only appear as the rows' statuses and tooltips. The new-pages
  order warning is not shown in the dialog yet.
- **The Pages tab's info bar is empty,** a known limitation. The bar's text comes from Xperience's client
  by content type: the list version has one only for `ContentItems` ("Selected reusable content items are
  synchronized…", wrong for pages), and the page tree's version, a different dialog, has the pages text.
  Xperience itself never opens the list version for pages (its Pages application has no list view), the
  component has no property for the text, and messages in command responses don't reach the bar (checked on
  `31.9.1`). Hiding the bar would need styling Xperience's markup, which can change with any Refresh. The
  Usage Guide explains what goes with a page instead.

### Versions

The protocol above is `31.9.1`'s. `30.8.0`'s dialog differs (no `ContentSynchronizationTypeEnum`, no
Include forms, no Detailed selection with dependencies). On a version whose protocol the toolkit doesn't
know, the Sync actions use the toolkit's own confirmation and messages, as first built, so they keep
working. Each Xperience Refresh must be checked against this table.

## Decisions

Agreed in review (November 2026):

1. **Reordered pages** include their siblings automatically (see Page order).
2. **New pages** get a warning about their siblings' order, rather than syncing the siblings too.
3. **Forms used on pages** stay out of these syncs (`includeLinkedForms: false`). Forms will get their own
   tab next to Pages and Content hub, with the same Sync actions, along with any other kind of content
   Content Sync can transfer. See [Sync status beyond pages and content hub
   items](sync-status-more-object-types.md), Forms.
4. **The actions are on by default;** `ContentSyncToolkit:SyncActions` turns them off.
5. **Detailed selection** (excluding linked content) was left out of the first build; with Xperience's
   dialog it's supported (see On the status list).

## Compatibility

- `30.8.0` and later, through the adapter. The spike synced Content hub items on `30.8.0` (3.9 s and
  24.5 s) and pages and items on `31.9.1`.
- `PollingTimeout` and the language and scheduled-restore parameters exist on `31.9.1`; on `30.8.0` the
  language comes from the language versions themselves, and scheduling is a parameter on both.
- Each Xperience Refresh must be checked: the `.Internal` interface has changed twice since `30.8.0`.

## Security

- Every command checks the Synchronize permission per row on the server, not only through disabled
  buttons.
- The tab's View permission alone doesn't allow syncing.
- Users only see rows they can already see (existing page and workspace checks), so they can't sync
  content they can't open.
- The audit entry records the user who started the sync.

## Out of scope

- Live progress without reloading (a custom React component).
- Deleting content on the target: Content Sync can't, and the toolkit keeps pointing to the recycle bin
  and the target editor.
- Forms used on pages (decision 3). Forms get their own tab later, with these actions.

## Test strategy

- **Unit tests:**
  - which statuses are syncable, and the disabled reasons;
  - splitting by language and by size limit;
  - the adapter's mapping for each known `Create` signature;
  - the permission checks (workspace Synchronize, page Synchronize, Manage permissions, administrator).
- **Rig, `31.9.1`:**
  - row and selected, on both tabs, with a review longer than one page;
  - a Spanish version, a moved page, a new page with a new parent, an Incompatible page (fails cleanly);
  - a Reordered page (its siblings go too), and new pages with `ComparePageOrder` on (the warning);
  - a restricted editor with and without Synchronize;
  - a scheduled restore;
  - Syncing tags and the failure callout;
  - the audit entries and Kentico's List of content synchronizations.
- **Rig, `30.8.0`:** row and selected syncs on both tabs.
- **Scale:** a selection of more than 200 pages, across list pages, checking it splits into syncs of 200.

## Acceptance criteria

- An editor with Synchronize can sync a row or a selection (across pages of the list), and the rows show In sync
  after the sync completes and the list reloads.
- Rows that can't be synced are disabled with the reason, and are never sent, even if ticked.
- An editor without Synchronize can't sync, through the UI or by calling the command.
- A failed sync shows Kentico's message, at once or in the callout.
- Each sync writes a started and a completed or failed Event log entry listing its items.
- Works on `30.8.0` and the latest verified version.
