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
and see, for every local item in that scope, whether it is missing on the
configured target, out of date on the target, in sync, or present on the
target but not locally — without leaving the administration UI or needing to
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
                     │ /Home                      │ Home         │ ✓ In sync │ ... │
                     │ /Store/Coffee-beans        │ Product      │ ● Missing │ ... │
                     │ /Store/Brewers/Chemex      │ Product      │ ▲ Out of  │ ... │
                     │                             │              │   date    │     │
                     │ /Articles/Coffee-processing│ Article      │ ✓ In sync │ ... │
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
  already always fresh.
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
| **Status** | Dropdown: *Needs action*, *Missing on target*, *Out of date on target*, *Extra on target*, *In sync* (placeholder *All*) | *Needs action* = Missing plus Out of date — what Content Sync still has to push. Extra is excluded because Content Sync only pushes source → target. A single dropdown was chosen over a multi-select: it covers the main question in one choice, and reading back a multi-select needs a custom condition builder. |
| **Content type** | Dropdown of website content types (Pages) or reusable content types (Content hub) | Lists every type of that kind, not only the ones present in the selected scope, because an options provider can't see the selected channel/workspace. Options show the type's display name; the value is its code name. |
| **Published from** / **Published to** | Two date inputs | Filter on the date in the Last published column (the local date, or the target's for Extra on target). Both bounds are inclusive whole days, taken in the **server's** time zone: the date inputs carry no time zone and the server can't see the editor's, so near midnight a day boundary can differ from the column, which shows the editor's time zone. Items without a publish date are excluded while either bound is set. |

Every filter value is read back from `LoadDataSettings.FilterWhereCondition`
the same way as the channel/workspace (see Server workflow): each field
compiles to a named parameter, strings as `string` and dates as `DateTime`.
Filtering, like search, runs in memory on the classified result before sorting
and paging.

Not included, by decision: a **Section** filter (search already matches the
page path, and a dropdown can't list sections of the selected channel), and a
**summary line** of counts per status (banners are built before the data
loads, so a summary couldn't follow the applied filter; the Status filter
answers the same question).

**Column tooltips.** The Status and Last published column headers carry
tooltips (`ColumnConfiguration.Tooltip`) explaining the four statuses and which
instance the date comes from. Plain text cells have no tooltip, but the status
tag does (`TagTableCellComponentProps.TooltipText`). It says why an item has
its status, and what to do where Content Sync needs something unusual, using
the comparer's `Reason` and each side's publication state:

| Case | Tooltip |
| --- | --- |
| Unpublished on one side | "Unpublished here, still published on the target." / "Published here, unpublished on the target." |
| Moved | "Moved here. To move it on the target, sync all pages on its old and new level." |
| Reordered | The tag reads **Order differs on target** (the status is still Out of date, so sorting and filters are unchanged). Tooltip: "Page order on this level differs on the target: Coffee Beverages Explained is in a different position there. This usually happens when only some pages of a level are synced. To fix it, use Sync with all subpages on Articles." It names up to 3 out-of-place pages, then "and N more", by display name; on the channel's top level it says to sync all pages on the level. Positions aren't given as numbers, because the page tree also shows drafts, which aren't compared. |
| Published more recently | "Published here after the target's copy." |
| Only on the target | "Only on the target. Content Sync can't delete content: if it was deleted here, delete it on the target." |
| Unpublished on both, or only here | "Unpublished on both instances." / "Unpublished here, and not on the target yet." |
| Can't sync yet (Missing or Out of date, and the target lacks an object the item needs) | "Can't sync yet: the target has no content type Event; has different fields for content type Image. A developer needs to deploy it to the target first." Comes before the item's other tooltip, if any. |

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
*Extra on target* rows have no link — the item doesn't exist on this instance.
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
Missing on target, Out of date on target, Extra on target, In sync — the
foundation's [status ordering convention](content-inventory-foundation.md#status-ordering-for-consumers).

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
| **Target is missing objects Content Sync needs** (see the foundation's [Required objects](content-inventory-foundation.md#required-objects)) | Yes — independent of the selected scope | `FriendlyWarning` banner, "Some items can't be synced until the target is updated", listing up to 8 objects (then "and N more"): content types and languages on both tabs, plus website channels on Pages and workspaces on Content hub, limited to the ones the user can see. Each says how the target differs (missing, different fields, or recreated with another GUID). The page waits at most 5 seconds for the target; if it can't answer in time, or runs an older toolkit version, the page shows without the banner. Refresh also refreshes the banner. |
| **Target unavailable** (`TargetAvailable = false`) | No — depends on the selected scope | A single table row with a red "Target unavailable" status tag and an explanation. The table must not fall back to showing local content as if it were unclassified. |
| **Selected scope no longer exists** (deleted after the filter was applied) | No | A single table row, "The selected channel/workspace no longer exists", with a grey "Not available" tag. |
| **Loading failed** (any unexpected exception in `LoadData`) | No | A single table row, "Sync status couldn't be loaded. Try Refresh; details are in the event log.", with a red "Error" tag. The exception is written to the event log, never shown. |
| **No items in the selected scope** | No | Xperience's native empty state (no rows returned). With a channel/workspace filter applied, Xperience words it as "We couldn't find any matches"; that's accepted for consistency with other admin listings. |

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
    (`ApplicationPermissionInfo`, `UserRoleInfo`). That name is a convention,
    like the `webpages-{id}` URL segment, pinned by a unit test.

  A channel or workspace the user can't access is simply absent: not in the
  filter, not the default, and requesting it shows the "no longer exists" row.
  Page-level permissions inside a channel (page ACLs) aren't checked: checking
  them per row was judged too costly for the listing, so anyone who can view a
  channel sees the status of all its pages.

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
  back out of `LoadDataSettings.FilterWhereCondition` (see Server workflow).
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

- selecting a channel with a mix of in-sync, missing, out-of-date, and
  extra-on-target items and verifying each renders with the correct status;
- selecting a workspace and verifying the same for content-hub items;
- publishing new content on the source and confirming it appears as missing
  until Refresh (or TTL expiry) reflects an updated target state, if also
  synced;
- stopping the target instance and verifying the unavailable-target banner
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

## Acceptance criteria

- An authorized editor can select a website channel or content-hub workspace
  and see every local item's sync status against the configured target.
- Status values match the foundation's classification exactly: in sync,
  missing on target, out of date on target, extra on target.
- Target-unavailable and source-not-configured states are visually distinct
  from "no items in this scope" and from each other.
- Refresh reflects current target state without a full browser reload (it
  re-navigates within the admin to the tab's own path — see Server workflow).
- The application is inaccessible, both in the menu and via direct navigation,
  to a user without the required permission.

## Out of scope

- Items that have never been published. The table lists published and
  unpublished content, matching what Content Sync can act on — a newly created
  or cloned item appears only once published. See
  [Publication-state scope](content-inventory-foundation.md#publication-state-scope).
- Following the admin's own language switcher. `ListingPageBase`'s
  `GetCurrentContentLanguage()` is `private`, not `protected`, so it isn't
  reachable from a derived page; the Language filter is used instead.
- Triggering an actual Content Sync operation (push) from this page. The page
  is read-only status visibility; initiating a sync remains Xperience's own
  **Sync this page**/**Sync with all subpages**/Content hub **Sync** actions,
  which a row's link leads to.
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
