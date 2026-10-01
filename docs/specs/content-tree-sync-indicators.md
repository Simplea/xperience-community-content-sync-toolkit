# Feature specification: Content tree sync indicators

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

The [sync-status-admin-page](sync-status-admin-page.md) gives editors a
dedicated place to review sync status, but an editor's normal workflow starts
in the page content tree or the Content hub listing — the same places
Xperience's own **Sync this page**/**Sync with all subpages**/**Sync** actions
already live. This feature surfaces the same status information from the
[content inventory foundation](content-inventory-foundation.md) directly in
those existing views, so an editor sees what needs attention without switching
to a separate application.

## Research findings

Xperience's own content tree and Content hub listing show no sync-related
state per item today; the grayed-out-row behavior editors already see is
scoped to the sync action dialog itself (items that cannot be synchronized at
all, for example unpublished pages), not a standing status indicator. This
feature adds new visual state to existing product UI rather than replacing or
extending an existing status column.

The sibling Forms Toolkit project's `FormListPageExtender`/`PageExtender<T>`
pattern (extending an existing listing page's configuration, adding elements
via `Page.PageConfiguration`) is the established precedent in this codebase for
augmenting a built-in Xperience listing without forking it. This feature
follows the same pattern, applied to the page tree and Content hub listing
instead of the Forms listing.

Two things could not be confirmed without implementation-time verification
against the admin client APIs (flagged here rather than assumed, resolve
during the Compatibility and API gate step):

- whether the page content tree's client component exposes a supported
  extension point for decorating individual tree rows (as opposed to only
  listing-page table rows, which is the pattern the Forms Toolkit precedent
  uses); and
- whether the Content hub listing can display items from more than one
  workspace at once, which would change how this feature requests status from
  the foundation for that view (see Server workflow).

## Goal

Let an editor viewing the page tree for a website channel, or the Content hub
listing for a workspace, see at a glance which items are missing on the
configured target or out of date there — without leaving the tree/listing or
running a separate check.

## Entry point and interaction

Add a small status badge next to each item's display name, in both the page
tree and the Content hub listing:

```text
Pages                                                    DancingGoatCore ▾

▾ Home
  ▾ Store
      Coffee beans          ● Missing on target
    ▾ Brewers
        Chemex               ▲ Out of date on target
        French press
  ▾ Articles
      Coffee processing
      Coffee brewing methods
```

- **In-sync** and **extra-on-target** items show no badge — the absence of a
  badge is the common case and should not add visual noise to every row. Only
  `MissingOnTarget` and `OutOfDateOnTarget` are decorated, using distinct icons
  and colors (open question for implementation: reuse Xperience's existing
  status-badge visual language if the admin component library exposes one,
  rather than introducing a new visual style).
- Hovering or focusing a badge shows a tooltip with the status name and the
  source item's last-published timestamp (matching the metadata already
  available from the foundation's `ContentInventoryItem`) — no additional
  server call for the tooltip; the data is already loaded (see Server
  workflow).
- The badge is not interactive in this version — it does not trigger a sync
  and does not navigate anywhere. It makes existing, already-discoverable
  native actions (**Sync this page**, Content hub **Sync**) easier to *decide
  when* to use, without duplicating or wrapping them. See Out of scope.
- Applies only within a website channel's own page tree and within one
  workspace's Content hub listing at a time — matching how Xperience itself
  scopes these views to one channel or workspace already.

### Unavailable and unconfigured states

- If the current instance is not configured as a sync source
  (Content Sync's source role not enabled with a target URL), show no badges at all — this feature is
  invisible rather than presenting confusing partial state on an instance that
  was never meant to check sync status.
- If the target is unreachable or rejects requests
  (`TargetAvailable = false`), show no per-row badges (do not guess, and do
  not decorate every row with an error state) and instead show one
  unobtrusive, dismissible notice at the top of the tree/listing indicating
  that sync status could not be loaded. This must not block or slow down the
  tree/listing itself from being otherwise fully usable.

## Administration integration

Implemented as `PageExtender<T>` subclasses against the built-in page-tree
listing and Content hub listing pages (exact target page types to be confirmed
during implementation — see Compatibility and API gate), following this
repository's established extension pattern rather than replacing either
listing outright.

Gated by the same
`XperienceCommunity.ContentSyncToolkit.ViewSyncStatus` permission used by
[sync-status-admin-page](sync-status-admin-page.md#administration-integration),
so a user without visibility into sync status in the dedicated admin page does
not see it leaking into the tree/listing either. A user without the permission
sees the tree/listing exactly as it behaves today, with no toolkit-added
elements.

## Server workflow

1. When a page tree or Content hub listing loads for a given channel or
   workspace, the extender issues one page command requesting the full sync
   status for that scope — the same
   `IContentSyncStatusService.GetWebPageSyncStatusAsync`/
   `GetContentHubSyncStatusAsync` calls the admin page uses, benefiting from
   the same cache (default 90-second TTL) so switching between the tree and
   the dedicated admin page for the same scope does not each trigger a fresh
   remote call.
2. The client decorates already-rendered rows by matching each row's item GUID
   against the fetched result; no per-node network call as the tree expands.
3. If the tree/listing is left open past the cache TTL, status is not
   automatically refreshed mid-session in this version — badges reflect status
   as of when the view was loaded (see Out of scope). Reloading the
   page/navigating back into it re-fetches.

## Security and privacy

- Same permission requirement as
  [sync-status-admin-page](sync-status-admin-page.md#security-and-privacy):
  `XperienceCommunity.ContentSyncToolkit.ViewSyncStatus`, enforced on the
  server command, not only by hiding the client-side badge.
- No additional content metadata is exposed beyond what the badge/tooltip
  shows (status and source last-published timestamp) — never field values,
  never the configured secret or target URL.

## Compatibility and API gate

Before implementation, confirm against `30.8.0` and `31.7.2`:

- the exact extensible page/component identity for the page-tree listing and
  the Content hub listing (whether `PageExtender<T>` against a documented
  listing page type is sufficient, or whether tree-row-level decoration needs
  a different, more specific extension point than the Forms Toolkit precedent
  used for flat table rows);
- whether the Content hub listing can show multiple workspaces at once, which
  would require this feature to request and merge status for more than one
  scope per view rather than the single-scope call the admin page uses.

If tree-row-level decoration is not achievable through a supported public
extension point on either listing, reduce this feature's scope to whichever of
the two (page tree, Content hub listing) is supported, and document the
other as deferred rather than shipping an internal-API-dependent
implementation.

## Error behavior

- Status fetch failure for the current scope: no badges, one dismissible
  notice (see Unavailable and unconfigured states); the tree/listing itself
  remains fully functional.
- An item present in the tree/listing but absent from the fetched status
  result (for example, created after the status was fetched, within the same
  session): no badge, not an error state — treated as unknown rather than
  guessed.
- Permission denied: no toolkit-added elements appear; the tree/listing
  behaves exactly as it does without the toolkit installed.

## Suggested component boundaries

- `PageTreeSyncIndicatorExtender` / `ContentHubSyncIndicatorExtender`
  (`PageExtender<T>` subclasses, exact base types per Compatibility and API
  gate): fetch status for the current scope and expose it to the client.
- Shared badge/tooltip React component, reused by both extenders, so visual
  treatment stays consistent between the two listings and with
  [sync-status-admin-page](sync-status-admin-page.md)'s status table.
- No new server-side classification or caching logic — this feature is purely
  a presentation layer over `IContentSyncStatusService`.

## Test strategy

### Unit and component tests

- permission gating of the page command on both extenders;
- row-to-status matching by GUID, including items with no matching status
  entry (unknown, not misclassified);
- unavailable-target and not-configured states each suppress badges without
  breaking the underlying tree/listing;
- badge/tooltip content matches the foundation's status and timestamp exactly.

### Integration and runtime tests

Run against both the minimum supported and latest verified Xperience versions,
using two Dancing Goat instances (source and target) configured with the
toolkit:

- a channel with a mix of statuses renders the correct badge only on
  missing/out-of-date rows, none on in-sync/extra-on-target rows;
- a workspace's Content hub listing behaves the same;
- stopping the target instance shows the single dismissible notice and no
  per-row badges, with the tree/listing otherwise fully usable;
- an unconfigured source instance shows no toolkit UI at all;
- an unauthorized user sees the tree/listing exactly as without the toolkit
  installed;
- opening the dedicated admin page and the tree for the same scope within the
  cache TTL does not trigger a second remote fetch (verified via a request
  counter on the test target).

## Acceptance criteria

- Missing-on-target and out-of-date-on-target items are visually flagged in
  both the page tree and the Content hub listing, without decorating in-sync
  or extra-on-target items.
- Badges never render on an instance not configured as a sync source, and
  degrade to a single non-blocking notice (not per-row noise) when the target
  is unavailable.
- The tree/listing's native performance and functionality are unaffected when
  the toolkit is not applicable (no permission, not configured, or target
  unavailable).
- Status shown matches what the same scope's data would show on
  [sync-status-admin-page](sync-status-admin-page.md) at the same point in
  time.

## Out of scope

- Triggering a sync from the badge or tooltip; same reasoning as
  [sync-status-admin-page](sync-status-admin-page.md#out-of-scope) — this
  feature makes Xperience's existing native sync actions easier to decide
  when to use, not a replacement trigger for them.
- Live/automatic refresh of badges while the tree/listing stays open past the
  cache TTL; an editor reloads the view to see updated status.
- Multi-workspace Content hub views, if Compatibility and API gate confirms
  they exist — deferred rather than guessed at.
- Any change to which rows are grayed out or selectable in Xperience's own
  sync action dialog; this feature only adds a badge, it does not alter native
  sync eligibility behavior.

## References

- [Content sync](https://docs.kentico.com/documentation/business-users/content-sync)
- [UI page extenders](https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/ui-pages/ui-page-extenders.html)
- [content-inventory-foundation spec](content-inventory-foundation.md)
- [sync-status-admin-page spec](sync-status-admin-page.md)
- [Architecture overview](../Architecture.md)
