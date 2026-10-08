# Feature specification: Sync status admin page

Status: Implemented and runtime-tested on Xperience by Kentico `31.7.2`; `30.8.0` run pending (release gate)
Repository: `Simplea/xperience-community-content-sync-toolkit`
Supported baseline: Xperience by Kentico `30.8.0` or newer (the first version
where Content Sync covers both content-hub items and website channel pages);
build-verified through `31.7.2`; .NET 8
Research date: 2026-08-11
Depends on: [content-inventory-foundation](content-inventory-foundation.md)

## Context

Xperience Community Content Sync Toolkit is an independently owned integration
maintained by Simplea for Xperience by Kentico. It is a reusable library, not an
Xperience website and not a Kentico-owned product. The Dancing Goat project
under `examples/` is the integration host used to run and verify the library.

Content Sync lets an editor push pages and content-hub items to a target
instance, but Xperience gives no way to see, before or after a push, which
items are missing on the target. An editor currently has to know their content
structure well enough to guess what might be missing, or sync broadly "just in
case." This feature adds a dedicated admin page where an editor picks a website
channel or content-hub workspace and sees every item's sync status against the
configured target, backed by the
[content inventory foundation](content-inventory-foundation.md).

## Research findings

Xperience by Kentico's own comparable UI is the **Content synchronization**
application, but it only exists on a target instance and only shows a history
of completed/scheduled sync tasks — not a diff of current state. There is no
existing product page this feature extends; it is a new admin application, not
a page extender on an existing one.

Two structurally different content scopes are relevant: website channel pages
(tree-structured, identified by tree path) and content-hub items (grouped by
workspace, no tree path). Kentico's own admin applications treat these
separately (the page tree and the Content hub listing are distinct
applications), and this feature follows the same split rather than forcing one
UI to represent both.

## Goal

Let an authorized editor select a website channel or a content-hub workspace
and see, for every local item in that scope, what a sync would do on the
configured target (create, update, publish, unpublish, move, or reorder it),
whether it's already in sync or only on the target, and whether the target
can accept it yet — without leaving the administration UI or needing to
know Xperience's internal sync mechanics.

## Entry point and interaction

Add a new top-level administration application, **Sync status**, in the
**Content management** category, with its own icon and a permission-gated menu
entry (see Security and privacy).

Name and place: an earlier version was **Content sync status** under
**Configuration**, next to Kentico's own **Content synchronization**
application, which lists the syncs a *target* has received. Similar names side
by side suggested the two were related. This application answers a different
question for editors on the *source* ("what still needs syncing?"), and covers
pages and content hub items alike, so it's **Sync status** and sits with the
other content tools. Its identifier and the `content-sync-status` URL segment
didn't change, so bookmarks and granted permissions keep working. Its icon is
`Icons.ClipboardChecklist`: a constant, because an icon name that doesn't exist
(the first version used `xp-refresh`) renders nothing, without an error. The
application has two sub-pages, matching the two scopes the foundation
supports, presented as Xperience's native section sub-navigation — a small
vertical list within the section (confirmed against a live instance: this is
how Xperience's own built-in Forms application presents its "List of forms"
and "Featured fields" sub-pages, the real precedent this feature's admin
integration follows), not horizontal pill-style tabs. The channel/workspace
selector is Xperience's native listing **filter panel** (a "FILTER" button
that opens a side panel; an applied selection shows as a removable chip), not
an always-visible dropdown row — confirmed against a live instance, this is
how the table's filter form actually renders, and building a custom
always-visible selector row would mean not using the native filter mechanism
this design otherwise depends on:

```text
Sync status

┌────────────────┐  Pages                          [Refresh]  [⚗ FILTER]
│ Content sync    │
│ ──────────────  │  Applied filters:  [ Channel: DancingGoatCore ✕ ]
│ Pages           │
│ Content hub     │  Search: [                   ]
│                 │
└────────────────┘  ┌───────────────────────────┬──────────────┬───────────┬─────┐
                     │ Path / Name                │ Content type │ Status    │ ... │
                     ├───────────────────────────┼──────────────┼───────────┼─────┤
                     │ /Store/Coffee-beans        │ Product      │ New       │ ... │
                     │ /Store/Brewers/Chemex      │ Product      │ Changed   │ ... │
                     │ /Home                      │ Home         │ In sync   │ ... │
                     │ /Articles/Coffee-processing│ Article      │ In sync   │ ... │
                     └───────────────────────────┴──────────────┴───────────┴─────┘

                     Showing 24 of 24 items
```

- The **Content hub** sub-page replaces the **Channel** filter with a
  **Workspace** filter and drops the **Path / Name** column's path prefix in
  favor of a flat item name (content-hub items have no tree path): the name
  the Content hub shows ("Guatemala Finca El Injerto"), not the code name
  (`GuatemalaFincaElInjerto-k3bwkxk3`). See the foundation's
  [Display names](content-inventory-foundation.md#display-names).
- The **Content type** column shows the type's display name ("Coffee
  product"), and sorts by it. Both fall back to code names for items from a
  target on an older toolkit version.
- **Search** uses the listing's own built-in search box (`LoadDataSettings.SearchTerm`,
  applied server-side in `LoadData` when the editor presses Enter) — not
  a custom form field, and not purely client-side JavaScript filtering over one
  fetched payload. It matches what the Path / Name column shows: the path for
  pages, the display name for content hub items.
- No channel/workspace selection yet (first load, filter never applied)
  defaults to the first channel/workspace the scope provider returns, so the
  table is never empty-by-default on first visit.
- **Refresh** forces a fresh remote fetch, bypassing the foundation's inventory
  cache (see Server workflow). It does not affect the local half, which is
  already always fresh. Its tooltip (`ActionConfiguration.Title`) explains
  when to use it: the target's list is otherwise reused for the configured
  `InventoryCacheDuration` ("up to 90 seconds" by default), and a target
  applies a sync within about 30 seconds (Kentico's restoration task), so a
  status checked right after a sync can still show the old state. Clearing the
  cache when a sync is sent was considered and rejected: there's no public
  "sync sent" event, and the target applies the sync later anyway, so an
  immediate refetch would cache the pre-sync list.
- Clicking a row opens the item in Xperience's own editor (see Filters and
  item navigation). The page itself stays read-only — it never triggers a sync
  (see Out of scope).

### Filters and item navigation

The filter panel holds, besides the channel/workspace, these fields. All are
optional and combine with AND; an empty field adds no condition (confirmed
live: Xperience omits it from the compiled filter entirely).

| Filter | Component | Values and behavior |
| --- | --- | --- |
| **Language** | Dropdown of the instance's content languages | No selection compares the instance's **default** content language. A language deleted after the filter was applied shows a "no longer exists" row, like a deleted channel/workspace. Each language is compared separately; there is no "all languages" view. |
| **Hide items in sync** | Checkbox | Leaves out In sync items; everything else stays, Incompatible and Only on target included, so an item never disappears just because it can't be synced yet. Combines with Status like every other field (AND). Read back as a `bool` parameter; unchecked adds no condition and no "Applied filters" chip (confirmed live on 31.9.1). Two drafts were dropped: a *Needs sync* option in the Status list (a group mixed in with statuses), and a *Show* dropdown with *Not in sync* and *Ready to sync*, which differ only for Incompatible and Only on target items, so most of the time they showed the same list. |
| **Status** | General selector (multi-select with search): *Incompatible*, then each status in the sort order (placeholder *All*) | Items matching **any** selected option. *Incompatible* = items whose required objects the target lacks. Each status option matches the tag shown, so an incompatible item matches *Incompatible* only. Kentico's general selector returns the selected values as a list, so `ContentSyncStatusMultiValueConditionBuilder` (the field's `FilterCondition` builder) compiles them into one parameter, comma-separated, which is read back like the other fields. Its options come from `ContentSyncStatusFilterOptionsDataProvider`. Verified live on 31.9.1, and the general selector, its data provider interface and `FilterCondition` exist in 30.8.0 (the library compiles against it). |
| **Content type** | Dropdown of website content types (Pages) or reusable content types (Content hub) | Lists every type of that kind, not only the ones present in the selected scope, because an options provider can't see the selected channel/workspace. Options show the type's display name; the value is its code name. |
| **Published from** / **Published to** | Two date inputs | Filter on the date in the Last published column (the local date, or the target's for Only on target). Both bounds are inclusive whole days, taken in the **server's** time zone: the date inputs carry no time zone and the server can't see the editor's, so near midnight a day boundary can differ from the column, which shows the editor's time zone. Items without a publish date are excluded while either bound is set. |

Every filter value is read back from `LoadDataSettings.FilterWhereCondition`
the same way as the channel/workspace (see Server workflow): each field
compiles to a named parameter, strings as `string`, dates as `DateTime` and
the Hide items in sync checkbox as `bool`; the Status selector's values arrive
as one comma-separated string (see the Status row above).
Filtering, like search, runs in memory on the classified result before sorting
and paging.

Not included, by decision: a **Section** filter (search already matches the
page path, and a dropdown can't list sections of the selected channel), and a
**summary line** of counts per status (banners are built before the data
loads, so a summary couldn't follow the applied filter; the Status filter
answers the same question).

**Statuses.** One Status column, one tag per item. The color says what to
do, and the label says what's different (see the foundation's
[Comparison rules](content-inventory-foundation.md#comparison-rules)):

| Tag | Shown for | Tag color |
| --- | --- | --- |
| Incompatible | `HasCompatibilityIssues`, whatever the status | Red (`AlertBackgroundHighEmphasis`) |
| Unpublished | `Unpublished` | Kentico orange |
| New | `New` | Kentico orange |
| Changed | `Changed` | Kentico orange |
| Moved | `Moved` | Kentico orange |
| Reordered | `Reordered` | Kentico orange |
| Not published | `NotPublished` | Grey |
| Only on target | `OnlyOnTarget` | Grey |
| In sync | `InSync` | Green |

Four colors, one per thing to do: red means a developer has to update the
target first, orange means sync it, grey means a sync can't change it right
now (not published here, or only on the target), green means done. The
default sort follows the same order. A color per status would be too many to learn, so the label says
what changes.

Incompatible takes the place of the item's status rather than sitting next to it:
until the target is fixed, the editor can't act on the status, so it would
only add a second thing to read. The tooltip still says what the sync will do
afterwards. Beta.2 drafts called it *Blocked*, first as a separate column (an
icon and "Blocked") and then as a status. The column was dropped (two places
to look, and it took about 110px from the path at 1440px), and the label was
renamed because it said that an item couldn't sync but not why. Kentico's own
Content Sync dialog calls this a *Compatibility error* ("Content type Image has
different field definitions on the source and target instance."), so the tag
says **Incompatible**, which fits the Status column, and its tooltip repeats
Kentico's sentence.

Each label means a different outcome or action for the editor, which is why
Publish pending (same action as Changed) was merged into Changed, while Moved
and Reordered stay apart (they need different syncs) and Unpublished stays
apart (retracted content still live on the target is the riskiest gap, so it
sorts first among the orange statuses).

Before 1.0.0-beta.2 there were four statuses (Missing on target, Out of date
on target with a reason, Extra on target, In sync), and the reason only showed
in the tooltip, so editors had to hover to tell an edit from an unpublish or a
move.

**Column widths.** `MinWidth`/`MaxWidth` are 8px grid units. At a 1440px-wide
window the grid is 920px, so the minimums add up to 93 (Name 40, Content type
18, Status 17, Last published 18): every status label and a full date and
time fit. Long paths and content type names are cut with an ellipsis. Verified
live on 31.9.1.

**Column tooltips.** The Status and Last published column headers carry
tooltips (`ColumnConfiguration.Tooltip`): what each status means and which
instance the date comes from. Plain text cells have no tooltip, but the status
tag does (`TagTableCellComponentProps.TooltipText`). It says what a sync would
do for that item, and what to do where Content Sync needs something unusual:

| Case | Tooltip |
| --- | --- |
| Incompatible | "Content type Event doesn't exist on the target instance. Content type Image has different field definitions on the source and target instance. A developer needs to deploy them to the target first. Then a sync creates it." One sentence per compatibility error, worded like Kentico's Content Sync dialog; an object recreated on the target instead of deployed reads "…has a different identity on the target instance: it was recreated there instead of deployed." The last sentence follows the item's status (creates, updates, unpublishes, or which pages to sync). |
| Unpublished | "Unpublished here, still published on the target. A sync unpublishes it there." |
| New | "Not on the target yet. A sync creates it." / "Not on the target yet. It's unpublished here, so a sync creates it unpublished." |
| Changed | "Published here after the target's copy. A sync updates it." / "Published here, unpublished on the target. A sync publishes it there." |
| Moved | "At a different place in the tree on the target. To move it, sync all pages on its old and new level." |
| Reordered | "Page order on this level differs on the target: Coffee Beverages Explained is in a different position there. This usually happens when only some pages of a level are synced. To fix it, use Sync with all subpages on Articles." It names up to 3 out-of-place pages, then "and N more", by display name; on the channel's top level it says to sync all pages on the level. Positions aren't given as numbers, because the page tree also shows drafts, which aren't compared. |
| Not published | "Unpublished here, then edited again, so it has no published version. Content Sync can't sync it until it's published again." or, for an item never published here that the target has, "Never published here: only a draft exists on this instance. Content Sync can't sync it until it's published.", followed by "The target still has it published.", "The target has it unpublished." or "The target doesn't have it." |
| Only on target | "Only on the target. Content Sync can't delete content: if it was deleted here, delete it on the target." |
| In sync, unpublished on both | "Unpublished on both instances." (A published item in sync has no tooltip.) |
| Newer draft (any status of a published item) | Adds "A newer draft here isn't published yet, and a sync only sends the published version." For an item in sync, which otherwise has no tooltip: "The target has the published version. A newer draft here isn't published yet, and a sync only sends the published version." The status doesn't change, because Content Sync sends the published version; the note is for the editor who just saved changes and would otherwise read In sync as "my changes are on the target". It comes from the per-page item ID query (`IContentSyncItemIdResolver`, latest versions), whose `VersionStatus.Draft` marks a draft of a published item, so it adds no query. |

See the foundation's
[Comparison rules](content-inventory-foundation.md#comparison-rules),
[Publication-state scope](content-inventory-foundation.md#publication-state-scope) and
[Required objects](content-inventory-foundation.md#required-objects).

**Dates.** The Last published column shows each editor's own time zone, like
the rest of the administration ("All time values in the administration are
displayed in the local time zone of each user"). Kentico's listings do this
client-side: they send a cell named `@kentico/xperience-admin-base/LocalDateTime`
with the date and its offset, and the browser formats it. That cell's C# helper
(`LocalDateTimeCellGenerator`) and props type are internal, so the tabs build
the same named cell with their own props (`{ value }`) holding the UTC date.
Like the `webpages-{id}` link segment, the component name is a client-side
convention rather than a public C# API; it's present in the `30.8.0` and
`31.7.2` admin assemblies, and the two-version runtime check covers it.
Verified live on 31.7.2: the column renders in the same format as the Content
hub's own date column.

**Click to open.** Each row with a local item links (`Row.Action`, a link
action) to that item in Xperience's own editor: the page in its website
channel application for Pages, the content item's editor for Content hub.
*Only on target* rows have no link — the item doesn't exist on this instance.
Content Sync's own actions differ by tab: a page can be synced from the page
tree (**Sync this page**, **Sync with all subpages**), but a content item only
from the Content hub list, by selecting it and using **Sync**; the content item
editor has no Sync action (verified live on 31.9.1). Linking Content hub rows
to that list instead was tried and dropped: the list can't be opened with an
item searched or selected (Kentico keeps both in browser session state, not in
the URL, and no URL parameter is read), so the editor had to search for the
item again, and lost the direct link to edit it. Kentico has no public API to
start a sync, so a Sync action on this page isn't possible either.

Links are generated with `IPageLinkGenerator` against Kentico's public page
types (`WebPageLayout`, `ContentItemEdit`), not hardcoded admin URLs. Each
generated path segment is filled from the value's string form: for Content
hub the workspace ID, language name, the "all content items" folder, and the
content item ID; for Pages the website channel application's slug, taken from
Kentico's public `WebPagesApplicationUrlIdentifier`, and the page's
language-and-ID identifier. The slug is the one place the implementation
depends on a URL convention (`webpages-{websiteChannelId}`); it's covered by a
unit test and the two-version runtime check. Item IDs aren't in the inventory
(the inventory is also the target's wire contract), so they're looked up with
one content query per load, for the rows on the current page only. If the
lookup fails, the rows render without links and the error is logged.

### Sorting

Every column is sortable from its header. The default is **Status**,
ascending, set as the Status column's `SortingConfiguration.DefaultDirection`
so the header shows the sort icon on first load and one click reverses it
(confirmed live on 31.7.2). Status ascending means urgency, not alphabetical:
Incompatible first, then Unpublished, New, Changed, Moved, Reordered, Not published,
Only on target, In sync — the foundation's
[status ordering convention](content-inventory-foundation.md#status-ordering-for-consumers).
Reversing it puts Incompatible last.

| Sort | Tie-break |
| --- | --- |
| Status (either direction) | Most recently published first, then path/name. Never-published items come last within their status. The tie-break doesn't reverse with the sort. |
| Path / Name, Content type, Last published | Path/name, ascending. Never-published items sort as the earliest date. |

Why Status: the page's main question is "what still needs syncing?", so the
items needing action belong on page 1. Newest first within a status puts what
editors are currently working on at the top of each group. The alternatives
were weaker defaults: Last published (recent In sync items push older Missing
ones off page 1), Path/Name (search already finds a specific item), and
Content type (the Content type filter groups better).

The chosen sort isn't remembered. Xperience's listing template keeps only the
applied filters and search, per browser tab (`kxp.listingState` in session
storage), and nothing about sorting; leaving the page and coming back, or
Refresh, returns to the default. Remembering the sort (for example in a
cookie, applied through `DefaultDirection`) was considered and decided
against for now: the Status default covers the main use.

### Empty and unavailable states

Each state must look distinct from the others, not just differ in wording.
Xperience's callout banners (`ListingConfiguration.Callouts`) are part of the
page configuration, which is built before `LoadData` runs, so only states known
before the table loads can be banners. On `30.8.0` callouts offer two styles,
`FriendlyWarning` and `QuickTip`.

| State | Known before the table loads? | Presentation |
| --- | --- | --- |
| **Not configured as a source** (Content Sync's source role not enabled with a target URL) | Yes | `FriendlyWarning` banner explaining that the toolkit isn't configured as a source, linking to the [Usage Guide](../Usage-Guide.md). The table loads no rows and no fetch is attempted. |
| **No channels or workspaces** (a brand-new instance, or none the user can access) | Yes | `QuickTip` banner pointing to **Configuration → Channel management** or **Workspaces**, as plain text. Kentico's admin URLs aren't a public API, so the banner doesn't hardcode a link. |
| **Target is missing objects Content Sync needs** (see the foundation's [Required objects](content-inventory-foundation.md#required-objects)) | No | The affected items show the Incompatible status (see Statuses), sort first, and can be filtered. |
| **Target unavailable** (`TargetAvailable = false`) | No — depends on the selected scope | A single table row with a red "Target unavailable" status tag and an explanation. The table must not fall back to showing local content as if it were unclassified. |
| **Selected scope no longer exists** (deleted after the filter was applied) | No | A single table row, "The selected channel/workspace no longer exists", with a grey "Not available" tag. |
| **Loading failed** (any unexpected exception in `LoadData`) | No | A single table row, "Sync status couldn't be loaded. Try Refresh; details are in the event log.", with a red "Error" tag. The exception is written to the event log, never shown. |
| **No items in the selected scope** | No | Xperience's native empty state (no rows returned). With a channel/workspace filter applied, Xperience words it as "We couldn't find any matches"; that's accepted for consistency with other admin listings. |

Before 1.0.0-beta.2, missing objects were also a `FriendlyWarning` banner
listing up to 8 objects. It was removed: it grew with the number of issues and
pushed the table down, it was built before the rows loaded, so it showed on a
tab with no affected items (an Image content type problem on the Pages tab),
and the Incompatible status now says the same per item, where the editor looks.
The developer's question, what to deploy, is answered by the Incompatible tooltips,
which name each object.

The table stays visible under a banner, so the not-configured state also shows
the native empty state beneath the warning. That duplication is accepted.

## Administration integration

Register as a new `[UIApplication]` with two sibling `[UIPage]`s — not a page
extender (there is no existing Kentico application this naturally extends),
and not a single page with a client-side tab switcher. This mirrors a real,
shipped Kentico precedent: `Kentico.Xperience.Admin.DigitalMarketing.UIPages.FormsApplication`
is a `[UIApplication(..., TemplateNames.SECTION_LAYOUT)]` root with two
sibling `[UIPage(..., TemplateNames.LISTING)]` children — pure attributes, no
custom React.

Access uses Xperience's standard `SystemPermissions.VIEW`, not a custom
permission. A custom permission was tried first and can't work: `[UIPermission]`
only *declares* which permissions Role management can grant for an
application, it doesn't enforce them, and the listing template itself always
requires VIEW — so a role granted only a custom permission still gets Access
Denied (confirmed live). The implementation therefore:

- declares `[UIPermission(SystemPermissions.VIEW)]` on the application, so
  Role management offers **Sync status → View**;
- enforces it with `[UIEvaluatePermission(SystemPermissions.VIEW)]` on each
  tab page, so direct navigation is refused, not just the menu entry hidden;
- sets `Permission = SystemPermissions.VIEW` on the `Refresh` page command.

Each tab page inherits `ListingPageBase<ListingConfiguration, ListingTemplateClientProperties>`
— Xperience's native listing template — not `ListingPage<TInfo>`, which is
specific to Kentico Info-objects; this feature's data is a synthetic list from
`IContentSyncStatusService`, not a database-backed Info-object type.
`ListingTemplateClientProperties` is an existing, ready-made class; no new
client-properties type or custom React component is authored for the default
table/column/filter/badge rendering.

Register the scope-enumeration service (channel/workspace listing for the
selectors) through a new `AddContentSyncToolkitAdmin()` startup extension,
called in addition to `AddContentSyncToolkit()` — kept separate because it is
admin-UI-only surface, not something every source/target installation needs
regardless of role the way the foundation's own registration is.

## Server workflow

Implemented on Xperience's native listing-page data pipeline rather than a
custom command/client pair: each tab is a `ListingPageBase`-derived page whose
`LoadData` override runs once per selector, search, sort, or page change —
there is no separate client-side filtering layer.

### How the channel/workspace selection reaches `LoadData`

This was the highest-risk piece of the design and needed live verification
against a real instance before committing to it. The two override points that
would let a page read a filter form's *raw* submitted values directly —
`ListingPageBase<,>.BuildFilterWhereCondition` and `.GetFilterModel` — are not
virtual, so they cannot be overridden. `LoadDataSettings.FilterWhereCondition`
is the only other avenue, but its declared type, the public `IWhereCondition`
interface, exposes only a compiled SQL fragment as a string (confirmed live:
selecting a channel produces `WhereCondition == "[Channel] = @Channel"`) with
no structured accessor for the value — extracting it from that string would
mean fragile SQL-text parsing.

The value actually *is* available in a structured, public way: the concrete
object Kentico returns for `FilterWhereCondition` is `CMS.DataEngine.WhereCondition`,
a public class (unlike the interface it's exposed through) whose public
`Parameters` collection holds the real parameterized values — confirmed live,
`Parameters` contained `'@Channel'=DancingGoatPages`. `ContentSyncStatusFilterValueExtractor`
casts `FilterWhereCondition` to this concrete type and reads the named
parameter directly; no SQL parsing involved. A dynamic-application-per-channel
design (mirroring how Kentico's own "Pages" and "Content hub" applications are
scoped — one menu entry per channel/workspace, no selector at all) was also
investigated as an alternative, but the APIs that pattern depends on
(`UIDynamicApplicationAttribute`, `IDynamicApplicationProvider`,
`DynamicApplicationDescriptor`) are all `internal` to Kentico's assemblies and
not usable by third-party code — ruled out, not merely deprioritized.

1. On tab/selector/search/sort/page change, the framework invokes the page's
   `LoadData(LoadDataSettings settings, CancellationToken)`; `settings.FilterWhereCondition`
   carries the compiled filter (read via `ContentSyncStatusFilterValueExtractor`
   as above), and `settings.SearchTerm`/`SortBy`/`SortType`/`PageSize`/`SelectedPage`
   carry the listing's own built-in search/sort/paging state — no custom form
   fields needed for those.
2. `LoadData` calls `IContentSyncStatusService`, which serves the remote half
   from cache when available (default 90-second TTL) or fetches fresh; the
   local half is always re-queried fresh on every call.
3. `LoadData` applies search, sort, and paging to the classified result in
   memory before mapping it to table rows — cheap, because the expensive part
   (the remote fetch) is already cache-served in the common case, but this is
   still a server call per interaction, not client-side JavaScript filtering
   over a single fetched payload.
4. **Refresh** invokes a `[PageCommand]` that records a "bypass cache on the
   next load" flag for that tab in a small in-memory `ContentSyncStatusRefreshRequestStore`
   (keyed per tab, not per channel or per user — a page command has no direct
   access to `LoadDataSettings`, so it cannot itself call `IContentSyncStatusService`
   with the currently-selected channel). Header actions don't evaluate a
   command's result — there is no automatic reload-after-command for them,
   and `UseCommand("LoadData")` re-invokes `LoadData` without the table's
   paging/sort/filter state (fails) — so the command returns
   `NavigateTo(<tab's own path>, refetchAllTemplates: true)`, Kentico's
   documented way to refresh a listing after a header action (confirmed live).
   That reload's `LoadData` consumes (reads and clears) the flag and passes
   `forceRefresh: true` for that one call — confirmed live as a second target
   fetch within the cache TTL; this does not change the TTL for other users or
   tabs. The per-tab (rather than per-channel or per-user) scoping is a
   deliberate low-stakes tradeoff: a concurrent viewer on the same tab could
   get an unrequested cache bypass in a narrow race window. Confirmed live:
   after Refresh the applied channel/workspace filter and search term are
   kept, but a column sort resets to the default (Status) order — see Sorting.

## Security and privacy

- Require the VIEW permission for the application (see Administration
  integration) for menu visibility, every tab page, and every server command;
  do not rely on menu-hiding alone.
- List only channels and workspaces the signed-in user can access in
  Xperience itself. View on Sync status alone would otherwise show the
  names and paths of every channel's pages and every workspace's items, leaking
  past Xperience's own channel and workspace permissions. `IContentSyncScopeAccess`
  decides, per request:
  - **Workspaces:** Xperience's public `IWorkspacePermissionEvaluator`, View for
    the Content hub application (`ContentHubApplication`), the same check the
    Content hub uses.
  - **Website channels:** each channel is its own Pages application, and
    Xperience's evaluator for an arbitrary application isn't public (nor are
    `ICurrentUserWorkspaceRetriever` and `PermissionConfiguration`; the compiled
    types are internal even though their XML documentation is shipped). So the
    check reads what Role management writes: administrators (`UserInfo.IsAdministrator()`)
    see every channel; anyone else needs a role with View on the application
    named `Kentico.Xperience.Application.WebPages_<WebsiteChannelGUID>`
    (`ApplicationPermissionInfo`, `UserRoleInfo`). Role management shows that
    permission as **Access channel**. That name is a convention, like the
    `webpages-{id}` URL segment, pinned by a unit test.

  A channel or workspace the user can't access is simply absent: not in the
  filter, not the default, and requesting it shows the "no longer exists" row.

- List only the pages the user can see in Xperience's own page tree. Within a
  channel, Kentico's page permissions (an access-control list per page, set on
  the channel root and inherited until a page breaks inheritance) decide which
  pages each role sees: the tree shows a page to users whose roles have
  **Display** on it. Administrators and roles with **Manage permissions** on the
  channel bypass page permissions (Kentico's page permission management).
  `IContentSyncScopeAccess.GetPageVisibilityAsync` applies the same rules, and
  the Pages tab filters each comparison with `ContentSyncPageVisibility` before
  it's counted, sorted or paged, so counts don't reveal hidden pages either:
  - A page on this instance follows its own list.
  - A page only on the target follows its nearest ancestor that exists here
    (as it would once synced), or the channel root's list.
  - A reorder tooltip never names a page the user can't see ("a page you can't
    see"), nor a hidden parent.

  Kentico's public `IWebPageAclManager.GetPermissions` reads one page's list in
  about three queries and doesn't cache (identical in 30.8.0 and 31.7.2), too
  slow for every page of a large channel. So the page-to-list mapping is read in
  one batched query by object type name (`cms.webpageaclmapping`; its Info class
  is internal, as for display names), and the public API is called once per
  distinct list. If the mapping query fails, the public API is called for every
  page instead (slower, never wrong). If permissions can't be read at all, the
  load fails with the generic error row: a failure hides pages, it never shows
  them.

  Verified live on 31.7.2 with `sync-status-tester` (role Article reviewer, with
  Access channel on Dancing Goat Pages), each state compared with what Kentico's
  own page tree shows the same user:
  1. With Manage permissions on the channel: every page (65 rows), as in the
     page tree.
  2. Without it, and with no role on the channel's page permissions: no pages,
     and the page tree shows only the channel root.
  3. With Display and Read on the channel root and broken inheritance on
     Articles without the role: 56 rows, everything except the 9 under
     Articles, which the page tree also hides. The administrator still sees 65.
  The rig's permissions were restored afterwards.

  Verified live on 31.7.2: the administrator sees all three Dancing Goat
  workspaces; `sync-status-tester`, whose role has Content hub View on Events
  and Ltd. only, sees just those two, and the Content hub tab opens on Events.
  Both see the Dancing Goat Pages channel, which both roles can view. A user
  whose roles can't view any channel isn't verified live (the rig's roles all
  can); it would show the existing "No website channels to compare" tip.
- The page never displays field-level content values — only the metadata
  already defined in the foundation's `ContentInventoryItem` contract (path or
  name, content type, status, publish timestamps).
- The page does not expose the configured shared secret or target URL to the
  client in any form, including error messages.

## Compatibility and API gate

This feature's only dependency beyond the foundation is Xperience's UI page
and administration menu registration APIs (`[UIApplication]`,
`[UIPermission]`, page commands). Verify the exact supported listing/table
client components available in `30.8.0` before implementation; prefer a native
listing/table component over a custom-built one if Xperience's admin
component library already provides sortable, filterable client-side tables
(the sibling Forms Toolkit project did not need to build one from scratch for
its listings).

Findings from live verification on `31.7.2` that constrain the implementation:

- The toolkit assembly needs `[assembly: AssemblyDiscoverable]`, or Xperience
  never scans it for `[UIApplication]`/`[UIPage]` registrations.
- `[UIPermission]` declares, it doesn't enforce, and listing pages always
  require VIEW, so a custom permission alone can't grant access (see
  Administration integration). Custom permission names are also capped at 50
  characters; the application fails to register otherwise.
- `LoadDataSettings.SelectedPage` is zero-based.
- Callouts (`ListingConfiguration.Callouts`) must be set in `ConfigurePage()`,
  which runs before `LoadData`; `CalloutType`, `CalloutPlacement`,
  `SortTypeEnum`, and `ActionType` have the same values in `30.8.0` and `31.x`.
- `ListingConfiguration`'s list properties (`PageSizes`, `HeaderActions`,
  `TableActions`, `MassActions`) must be set explicitly — left `null`, the
  listing template throws. `ColumnConfiguration.MinWidth`/`MaxWidth` default
  to `0` (collapsed columns) and are in 8px grid units, not pixels.
- A header action's command must set both `Name` (used for the permission
  check) and `Parameter` (sent as the command name), and `[PageCommand]`
  handlers must be asynchronous.
- `Kentico.Xperience.Admin.Base.Color`'s numeric values differ between
  versions (31.x inserted a member, shifting later values by one), and enum
  constants compile to integers — built against `30.8.0` but run on `31.x`,
  `Color.SuccessBackgroundHighEmphasis` rendered as `SkeletonContent`. Status
  badge colors are therefore resolved with `Enum.Parse<Color>(name)` at
  runtime. Any other Xperience enum used from this assembly carries the same
  risk if its members aren't at a stable position.

## Error behavior

- Selected channel or workspace no longer exists (deleted after the selector
  loaded): safe "no longer exists" row (see Empty and unavailable states); the
  selector's options come from `IContentSyncScopeProvider` when the page
  loads, so reloading the page drops the deleted scope.
- Failure unrelated to target availability (for example, a transient error in
  the local query): safe generic error row pointing to Refresh; the exception is
  logged via `EventLogService.LogException` and raw details are never shown.
  Cancellation is not treated as an error.
- Permission denied: the application does not appear in the administration
  menu; a direct navigation attempt receives Xperience's standard forbidden
  administration response.

## Suggested component boundaries

- `ContentSyncStatusApplication`: root page, registers the application,
  permission, and menu entry (`TemplateNames.SECTION_LAYOUT`).
- `ContentSyncStatusTabBase`: the shared `ListingPageBase`-derived base for
  both tabs. It builds the listing configuration and the state callouts,
  hosts the `Refresh` command, and overrides `LoadData` to extract the selected
  channel/workspace (via `ContentSyncStatusFilterValueExtractor`), run
  `ContentSyncStatusViewBuilder`, and map the result to `Row`s. The status
  column uses Kentico's native tagged-cell mechanism (`NamedComponentCell`
  with the shared `Tag` component) for the colored status badge, not a
  custom-rendered one.
- `ContentSyncStatusPagesTab` / `ContentSyncStatusContentHubTab`: thin
  subclasses (`TemplateNames.LISTING`), one per scope kind, supplying the
  filter model, captions, scope list, and the matching
  `IContentSyncStatusService` call.
- `ContentSyncStatusViewBuilder`: pure, unit-testable decision of which state
  to show (not configured, no scopes, scope not found, target unavailable,
  empty, items) plus search/sort/paging, independent of Xperience's page types.
- `ContentSyncStatusPagesFilterModel` / `ContentSyncStatusContentHubFilterModel`:
  each tab's filter-form model, with a single `[DropDownComponent]`-decorated
  `Channel`/`Workspace` property.
- `ContentSyncStatusChannelOptionsProvider` / `ContentSyncStatusWorkspaceOptionsProvider`:
  `IDropDownOptionsProvider` implementations supplying each dropdown's options
  dynamically from `IContentSyncScopeProvider` (a dropdown's options can't be
  set from an instance value at compile time via the attribute alone, since
  attribute arguments are compile-time constants — a `DataProviderType`
  pointing at one of these is Kentico's supported way to populate options at
  render time instead).
- `ContentSyncStatusFilterValueExtractor`: reads the selected channel/workspace
  and every other filter field back out of
  `LoadDataSettings.FilterWhereCondition` (see Server workflow).
- `ContentSyncStatusFilterOptionsDataProvider`: the Status filter's options
  (an `IGeneralSelectorDataProvider`), and
  `ContentSyncStatusMultiValueConditionBuilder`: the `IWhereConditionBuilder`
  that compiles the selected statuses into one parameter (see Filters and
  item navigation).
- `IContentSyncItemIdResolver`: per listing page of rows, each local item's ID
  (for its editor link) and whether its latest version is a newer draft (for
  the status tooltip), in one content query.
- `ContentSyncStatusListingSupport`: pure, unit-testable search/sort/paging and
  status-presentation helpers shared by both tabs.
- `ContentSyncStatusRefreshRequestStore`: the small in-memory per-tab
  cache-bypass flag described under Server workflow.
- `IContentSyncScopeProvider`: enumerates the website channels (joining
  `WebsiteChannelInfo`/`ChannelInfo`) and workspaces (`WorkspaceInfo`) the
  signed-in user can access, for the selectors — new code, since the
  foundation deliberately excludes a "discover all channels/workspaces"
  convenience method. Scoped per request, with `IContentSyncScopeAccess` (see
  Security and privacy) deciding access.
- One `[PageCommand] Refresh` per tab, calling `IContentSyncStatusService`'s
  cache-bypassing overload on the next `LoadData`.

No React client and no other custom page commands are needed for the default
design. Keep all business logic (status classification, caching) in the
foundation; this feature's C# surface is limited to the native listing-page
plumbing above and channel/workspace enumeration for the selectors.

## Test strategy

### Unit and component tests

- permission gating of the application and every command;
- command marshaling: correct foundation calls for each tab/scope combination;
- Refresh bypasses cache for the current request without altering the shared
  TTL;
- empty-scope, unavailable-target, and not-configured states each produce
  their own distinct client state (not a shared generic error).

### Integration and runtime tests

Run against both the minimum supported and latest verified Xperience versions,
using two Dancing Goat instances (source and target) configured with the
toolkit. The minimum-version run is a release gate (see
[Release process](../Release-Process.md#publishing-a-release)), not a
requirement for every pull request. It exists because the minimum and latest
versions can behave differently even when both compile — `Color`'s enum values
shifted between `30.8.0` and `31.x`. Cover:

- selecting a channel with items in every status (see the foundation's
  [Scenarios](content-inventory-foundation.md#scenarios)) and verifying each
  renders with the correct status, color and tooltip;
- selecting a workspace and verifying the same for content-hub items;
- publishing new content on the source and confirming it appears as New
  until Refresh (or TTL expiry) reflects an updated target state, if also
  synced;
- stopping the target instance and verifying the Target unavailable row
  renders instead of a stale or misleading table;
- unconfigured source instance shows the not-configured banner;
- unauthorized user cannot see the menu entry or invoke the commands directly.

Verified live on `31.7.2` (source and target Dancing Goat instances): all four
statuses on Pages; Missing, Out of date, and In sync on Content hub; search,
header sorting, and the default urgency order; Refresh bypassing the cache and
keeping filter and search; the not-configured banner; the target-unavailable
row; and permission gating (menu hidden, direct URL and commands refused with
403 without VIEW; everything works once a role is granted VIEW). Not verified
live: the no-scopes banner (same mechanism as the not-configured banner), the
scope-not-found and error rows (unit-tested), and the `30.8.0` run (release
gate).

Filters and item navigation, verified live on `31.7.2`: all six filter fields
render in order (shared fields inherited from the base filter model); the
Status column is the visible default sort, with the newest items first within
each status, and one click on its header reverses it; Status
(Needs action, Extra), Content type, and Published from filter correctly,
alone and combined; an empty result uses the native empty state; Refresh keeps
applied filters; the header tooltips render; and clicking a row opens the page
or content item in its editor, on both tabs. Not verified live: choosing a
second language (the rig has only English), the language-not-found row, and
that Extra on target rows have no link (the rig had no such items at the time;
the rows are built without an action when there's no local item).

The 1.0.0-beta.2 statuses, filters and tooltips were verified live on
`31.9.1`, including a full round trip with Kentico's own Content Sync; see the
[Compatibility](../Compatibility.md#100-beta2-statuses) record.

## Acceptance criteria

- An authorized editor can select a website channel or content-hub workspace
  and see every local item's sync status against the configured target.
- Status values match the foundation's classification exactly (see
  [Comparison rules](content-inventory-foundation.md#comparison-rules)), and
  incompatible items are marked whatever their status.
- Target-unavailable and source-not-configured states are visually distinct
  from "no items in this scope" and from each other.
- Refresh reflects current target state without a full browser reload (it
  re-navigates within the admin to the tab's own path — see Server workflow).
- The application is inaccessible, both in the menu and via direct navigation,
  to a user without the required permission.

## Out of scope

- Items that have never been published, on their own. The table lists what
  Content Sync can act on — a newly created or cloned item appears only once
  published. The exception is an item the target has: it's listed as Not
  published, so it doesn't look deleted here. See
  [Publication-state scope](content-inventory-foundation.md#publication-state-scope).
- Following the admin's own language switcher. `ListingPageBase`'s
  `GetCurrentContentLanguage()` is `private`, not `protected`, so it isn't
  reachable from a derived page; the Language filter is used instead.
- Triggering an actual Content Sync operation (push) from this page. The page
  is read-only status visibility; initiating a sync remains Xperience's own
  **Sync this page**/**Sync with all subpages** (page tree) and **Sync**
  (Content hub list) actions. A page's row link opens it where those page tree
  actions are; a content item's opens its editor, and Sync is in the Content
  hub list (see Click to open).
- A Section filter and a per-status summary line (see Filters and item
  navigation).
- Server-side search or pagination for very large scopes; this version loads
  the full scope result and searches, sorts, and pages it in memory on the
  server, matching the foundation's "no wire-level pagination" scope.
- A true hierarchical tree visualization for pages; this version uses a flat,
  sortable table with the tree path as a column. A dedicated tree view is a
  candidate for a later iteration, not this one.
- Bulk status export (CSV, etc.).
- Cross-workspace or cross-channel combined views; one scope at a time.

## References

- [Content sync](https://docs.kentico.com/documentation/business-users/content-sync)
- [UI applications](https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/reference-ui-page-templates)
- [content-inventory-foundation spec](content-inventory-foundation.md)
- [Architecture overview](../Architecture.md)
