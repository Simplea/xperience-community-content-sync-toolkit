# Feature specification: Open items on the target

Status: **implemented**.

## Context

An item that exists only on the target shows as **Only on target**: it was
deleted on the source after a sync, or it was created on the target. Content
Sync can't remove it. Kentico's
[Content sync](https://docs.kentico.com/documentation/business-users/content-sync)
documentation: "You cannot select a deleted content item or page for
synchronization. Content synchronization cannot be used to delete items on the
target instance." Its
[Work with content sync](https://docs.kentico.com/guides/digital-marketing/work-with-content-governance/work-with-content-sync)
guide recommends unpublishing the item on the source and syncing, or deleting
it on the target by hand.

Today the toolkit only says so in the tooltip. The row can't be opened: the
item doesn't exist on the source, and the source doesn't know its ID on the
target. An editor has to sign in to the target, find the channel or workspace
and look for the item by name.

## Research findings

- **Content Sync can't delete**, and syncing a parent page doesn't remove its
  children that no longer exist on the source: a sync creates or updates the
  selected items and their dependencies (Kentico's docs; see Context).
- **Scheduling.** Content Sync's **Postpone restoration on the target**
  already schedules a sync of updates. Scheduled publish and unpublish dates on
  the source aren't synced. On the target, Kentico can schedule an unpublish
  itself, with a redirect.
- **No public API starts a sync**, so the toolkit can say what to sync but not
  run the sync.
- **Admin URLs need instance IDs.** A page opens at
  `/admin/webpages-{WebsiteChannelID}/{language}_{WebPageItemID}` and a
  content item at
  `/admin/content-hub/{WorkspaceID}/{language}/all/list/{ContentItemID}/content`.
  The inventory carries GUIDs and names only, so the source can't build these
  for the target. The source already builds them for its own items with
  `IPageLinkGenerator` (`ContentSyncStatusItemLinks`), from IDs it looks up for
  one listing page at a time (`ContentSyncItemIdResolver`).
- **Row links and new tabs.** A listing row's `Action` has `LinkParameters`
  (`UseWindowOpen`, `Target = "_blank"`) on 30.8.0 and later, and the admin
  client's row handler opens such links with `window.open`. On 31.9.1, though,
  the listing renders each row as a plain `<a href>` without a target, so a
  click follows the link in the same tab; `UseAnchor` (31.x only) doesn't
  change that (checked by rewriting the response in the browser). Ctrl+click
  or middle-click opens a new tab, as for any link.
- **Link cells open a new tab.** Kentico's link cell
  (`NamedComponentCellComponentNames.LINK_COMPONENT`, props
  `LinkNamedComponentCellProps { Text, Url }`, on 30.8.0 and later) renders
  `<a target="_blank" rel="noopener">`, and a click on it opens a new tab even
  inside a clickable row (checked on 31.9.1). It has no icon option.

## Goal

An **Only on target** row opens the item in the target's administration,
where the editor can delete it, unpublish it, or schedule the unpublish. The
toolkit doesn't change content on the target.

## Design

### Target: item ID endpoint

Two actions on the existing inventory controller, behind the same secret:

- `GET .../web-page-ids?channelName={name}&languageName={name}&guids={guid,guid,…}`
- `GET .../content-item-ids?workspaceName={name}&languageName={name}&guids={guid,guid,…}`

```csharp
public sealed record TargetItemIdsResponse(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    int? ScopeId,                      // WebsiteChannelID or WorkspaceID; null if unknown
    IReadOnlyList<TargetItemId> Items);

public sealed record TargetItemId(Guid Guid, int Id); // WebPageItemID or ContentItemID
```

- IDs are looked up with `ContentSyncItemIdResolver`, as the source does for
  its own rows: latest versions, secured items included, no language
  fallbacks. Content items are looked up across every reusable content type
  on the target, so the request doesn't need to name them.
- At most 100 GUIDs per request; the source sends one listing page (at most
  50). Values that aren't GUIDs are ignored.
- An unknown channel, workspace or language returns `ScopeId: null` and no
  items, as the inventory endpoints return an empty list, so the response
  doesn't confirm or deny that a scope exists.
- Schema version 4. A target on version 3 or earlier has no such endpoint and
  answers 404; the source then shows the row without a link, as before.

### Source: links

- **Which rows:** Only on target rows. Every other row exists on the source
  and keeps opening the item here.
- **When:** while building a listing page, only if it has Only on target rows:
  one request for that page's GUIDs. A failure (unreachable, rejected, older
  target) leaves those rows without a link; the rest of the page is
  unaffected.
- **URL:** the path `IPageLinkGenerator` builds from the target's IDs, under
  the target's admin URL. Both instances run Xperience versions with the same
  admin URL structure, which holds within the supported range.
- **Target admin URL:** `{TargetUrl}/admin` by default, from Content Sync's
  `Source:TargetUrl`. A new optional setting, `TargetAdminUrl`, overrides it
  when `TargetUrl` is an address editors' browsers can't reach (an internal
  host name, for example).
- **How it opens:** the row's name (Content hub) or path (Pages) becomes a
  link, marked with ↗, that opens the item on the target in a new tab, so
  the editor keeps the status list. The row itself isn't clickable, so there's
  one way to open it: a row link can't open a new tab on 31.9.1 (see Research
  findings). The editor signs in to the target if they aren't signed in
  already; the target's own permissions apply.
- **Tooltip:** "Only on the target. Open it there with the link on its name:
  Content Sync can't delete content, so delete or unpublish it on the
  target." Without a link, the current text stays.

### Configuration

```json
{
  "ContentSyncToolkit": {
    "TargetAdminUrl": "https://prod.example.com/admin"
  }
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `TargetAdminUrl` | `{TargetUrl}/admin` | Where editors open the target's administration. |

## Security and privacy

- The new actions are behind the same secret filter as the inventory, and
  answer only GUIDs the caller already got from the inventory with their IDs.
  An ID is no secret from a caller who can push content to the target.
- GUIDs, scope and language names are untrusted input: parsed, capped, and
  unknown names return an empty answer.
- The link only opens the target's admin; the editor still needs an account
  and permissions there. The toolkit doesn't sign anyone in.

## Out of scope

- **Deleting or unpublishing on the target from the toolkit.** It would need
  an endpoint that changes content, a rule for who on the source may delete on
  production, an audit trail, and Kentico's redirect choice on each page
  delete. It makes the toolkit a second sync engine, against Kentico's "edit
  only on the source" model. Opening the item on the target covers the need
  with Kentico's own UI and permissions.
- **Scheduling removals.** Kentico's scheduled unpublish on the target, reached
  through the link, covers it. Scheduled updates are Content Sync's **Postpone
  restoration**.
- **Starting a sync from the toolkit.** No public API.
- **A removal planning view** (items to remove from production in a release).
  Possible later as a list in the toolkit, still carried out on the target.
- **Target links on other rows** (Changed, Not published, …). A row has one
  action, which opens the source item. A second link would need another cell;
  revisit with the content tree work.

## Test strategy

- **Unit:** the target admin URL (default and override, trailing slashes);
  the client's request and response handling (success, rejected, unreachable,
  malformed); GUID parsing and the cap; which rows get a target link and the
  tooltip text.
- **Live, on the two-instance rig:** an Only on target page and content item
  each open the target's editor in a new tab; an older target or an
  unreachable one leaves the rows without a link and the page otherwise
  intact; the endpoint rejects a missing secret and answers empty for an
  unknown channel, workspace or language.

### Verification results (live two-instance rig, 31.9.1)

- **Endpoint:** returned the target's IDs for a page (`WebPageItemID` 73,
  `WebsiteChannelID` 1) and a content hub item (`ContentItemID` 185,
  `WorkspaceID` 1), skipped a value that wasn't a GUID, answered
  `ScopeId: null` with no items for an unknown channel and workspace, and
  answered 404 without the secret, like the inventory endpoints.
- **Admin page:** the Only on target page and content hub item opened
  `https://localhost:27311/admin/webpages-1/en_73/content` and
  `https://localhost:27311/admin/content-hub/1/en/all/list/185/content`, the
  items' editors on the target. The page was made target-only for the test by
  giving its target copy a new GUID; the rig's own target-only page was never
  published, and such pages aren't listed. A first version used the row click,
  which opened the target in the same tab; the link on the name opens a new
  one.
- **Older target:** not run live; the client maps the 404 to `Rejected`
  (unit tested), and the page then leaves the rows without links.

## Acceptance criteria

- The name or path of an Only on target row links to that item's editor on
  the target, in a new tab, for pages and for content hub items.
- No other row changes behavior.
- `TargetAdminUrl` overrides the default.
- Older targets and failed lookups leave the page working, without links on
  those rows.
- No content is changed on the target by the toolkit.
