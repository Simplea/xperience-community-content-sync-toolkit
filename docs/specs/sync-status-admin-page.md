# Feature specification: Sync status admin page

Status: Not started
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

Add a new top-level administration application, **Content sync status**, with
its own icon and a permission-gated menu entry (see Security and privacy). The
application has two tabs, matching the two scopes the foundation supports:

```text
Content sync status                                          [Refresh]

[ Pages ]  [ Content hub ]

Website channel:  [ DancingGoatCore            ▾ ]   Language: [ en-US ▾ ]

┌─────────────────────────────────────────────────────────────────────┐
│ Status filter: [ All ▾ ]     Search: [                            ] │
├───────────────────────────┬──────────────┬───────────┬──────────────┤
│ Path / Name                │ Content type │ Status    │ Last published│
├───────────────────────────┼──────────────┼───────────┼──────────────┤
│ /Home                      │ Home         │ ✓ In sync │ Aug 3, 2026  │
│ /Store/Coffee-beans        │ Product      │ ● Missing │ Aug 9, 2026  │
│ /Store/Brewers/Chemex      │ Product      │ ▲ Out of  │ Aug 10, 2026 │
│                             │              │   date    │              │
│ /Articles/Coffee-processing│ Article      │ ✓ In sync │ Jul 28, 2026 │
└───────────────────────────┴──────────────┴───────────┴──────────────┘

Showing 24 of 24 items
```

- The **Content hub** tab replaces the **Website channel** selector with a
  **Workspace** selector and drops the **Path / Name** column's path prefix in
  favor of a flat item name (content-hub items have no tree path).
- **Status filter** offers: All, Missing on target, Out of date on target, In
  sync, Extra on target.
- **Search** filters the visible rows by path/name substring, client-side,
  after the full scope result has loaded — no server-side search in this
  version (see Out of scope).
- Selecting a channel/workspace and language triggers a fetch; while it is in
  flight, show a loading state over the table area, not a full-page spinner,
  so the selectors remain usable.
- **Refresh** forces a fresh remote fetch, bypassing the foundation's inventory
  cache (see Server workflow). It does not affect the local half, which is
  already always fresh.
- Selecting a row does not perform any action in this version — it is a
  read-only status view (see Out of scope for why this deliberately excludes
  triggering a sync from here).

### Empty and unavailable states

- **No target configured** (`Source.TargetUrl` is unset): show a persistent
  banner explaining that content sync toolkit is not configured as a source on
  this instance, with a link to
  [Contributing-Setup](../Contributing-Setup.md) or equivalent configuration
  guidance. The channel/workspace selectors remain visible but the table area
  shows this message instead of attempting a fetch.
- **Target unreachable or rejecting requests** (`TargetAvailable = false` from
  the foundation): show a banner reading "Target status unavailable — showing
  local content only is not possible without a reachable target," and leave
  the table empty rather than guessing. Do not fall back to showing local
  content as if every item were unclassified.
- **No items in the selected scope**: show a plain "No content in this
  channel/workspace" message, distinct from the unavailable-target banner.
- **No channels or workspaces available** (a brand-new instance, or a user
  without access to any): show a message directing the editor to Xperience's
  own channel/workspace management, not an empty dropdown with no explanation.

## Administration integration

Register as a new `[UIApplication]` (not a page extender — there is no
existing Kentico application this naturally extends), gated by a custom
permission, e.g. `XperienceCommunity.ContentSyncToolkit.ViewSyncStatus`,
declared via `[UIPermission(...)]` on the application's root page class,
following this repository's existing pattern for custom permissions (see
[content-inventory-foundation](content-inventory-foundation.md#suggested-component-boundaries)).

The page's server-side commands call `IContentSyncStatusService` directly
(`GetWebPageSyncStatusAsync`/`GetContentHubSyncStatusAsync`); this feature adds
no new server-side business logic beyond marshaling the foundation's result
into a client-friendly shape (channel/workspace listing for the selectors,
paging/sorting for the table if the scope is large — see Out of scope).

Register the toolkit's administration module through the repository's
`AddContentSyncToolkit()` startup extension, matching how the sibling
`AddFormsToolkit()` registers its own admin module.

## Server workflow

1. On tab/selector change, the client issues a page command with the selected
   channel or workspace name and language.
2. The command calls `IContentSyncStatusService`, which serves the remote half
   from cache when available (default 90-second TTL) or fetches fresh.
3. **Refresh** issues the same command with a flag that bypasses the cache for
   this one request (does not change the TTL for subsequent requests from other
   users or tabs).
4. The command returns the full classified item list to the client; filtering
   and searching happen client-side over the already-fetched result, not as
   separate server round-trips.

## Security and privacy

- Require the `XperienceCommunity.ContentSyncToolkit.ViewSyncStatus` permission
  for both the application's menu visibility and every server command; do not
  rely on menu-hiding alone.
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

## Error behavior

- Selected channel or workspace no longer exists (deleted after the selector
  loaded): safe "no longer available" message; refresh the selector options.
- Server command failure unrelated to target availability (for example, a
  transient error in the local query): safe generic error message with a retry
  action; do not surface raw exception details.
- Permission denied: the application does not appear in the administration
  menu; a direct navigation attempt receives Xperience's standard forbidden
  administration response.

## Suggested component boundaries

- `ContentSyncStatusApplication` (or equivalent root `UIPage`): registers the
  application, permission, and menu entry.
- `ContentSyncStatusPage` commands: `GetChannels`, `GetWorkspaces`,
  `GetWebPageSyncStatus`, `GetContentHubSyncStatus` — thin wrappers over
  `IContentSyncStatusService` plus whatever channel/workspace enumeration is
  needed for the selectors.
- React client: tab component (Pages / Content hub), selector controls, status
  table with client-side filter/search, loading and empty/unavailable states.

Keep all business logic (status classification, caching) in the foundation;
this feature's C# surface should be limited to command marshaling and
channel/workspace enumeration for the selectors.

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
toolkit:

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

## Acceptance criteria

- An authorized editor can select a website channel or content-hub workspace
  and see every local item's sync status against the configured target.
- Status values match the foundation's classification exactly: in sync,
  missing on target, out of date on target, extra on target.
- Target-unavailable and source-not-configured states are visually distinct
  from "no items in this scope" and from each other.
- Refresh reflects current target state without requiring a full page reload.
- The application is inaccessible, both in the menu and via direct navigation,
  to a user without the required permission.

## Out of scope

- Triggering an actual Content Sync operation (push) from this page. The page
  is read-only status visibility; initiating a sync remains Xperience's own
  **Sync this page**/**Sync with all subpages**/Content hub **Sync** actions.
  A future iteration may add a deep link from a row to the item's location in
  the native page tree or Content hub listing so an editor can act from there,
  but that link is not part of this version.
- Server-side search or pagination for very large scopes; this version loads
  the full scope result and filters client-side, matching the foundation's
  "no wire-level pagination" scope.
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
