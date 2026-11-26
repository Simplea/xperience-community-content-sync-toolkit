# Content hub items moved between workspaces

## Problem

Content Sync doesn't move content items between workspaces. When an editor moves an item to another
workspace on the source and syncs it, the target restores the item's content but keeps it in the
workspace it was first synced to.

Seen on `31.9.1` (November 2026) on the repository's rig, with *Hario Mini Mill Slim*, moved from
*Dancing Goat Commerce* to *Dancing Goat Events* on the source:

- synced from the status list three times, and once with Xperience's own **Sync** in Content hub, from
  *Dancing Goat Events*; each sync finished successfully;
- on the target, the item's language metadata showed each restore (its modified time changed), but
  `ContentItemWorkspaceID` stayed *Dancing Goat Commerce*.

Kentico's documentation doesn't say either way. An earlier note in
[content-inventory-foundation.md](content-inventory-foundation.md) said a sync from the new workspace
moves the item on the target; that was wrong, and so were the 1.0.0-beta.4 tooltip, Usage Guide and
release notes built on it ("Don't delete it: syncing it from its new workspace moves it there").

Workspaces are compared one at a time, so before this change a moved item showed:

- **Moved here** in its old workspace, in the color of what a sync changes, with advice that a sync
  would fix it;
- **New** in its new workspace, however often it was synced.

## Design

### The new workspace: compare with the target's copy

Before a workspace is compared, the source asks the target for the items it has here that the target's
inventory of the same workspace doesn't list: the target endpoint
`content-hub-items-by-guid?languageName=&guids=` returns those items, in whatever workspace each is, by
the same inventory rules as `content-hub-items`. Items the target has in another workspace are added to
the target's side of the comparison, so the row shows what a sync would really do: **In sync** or
**Changed**, not **New**.

- The lookup is cached with the target's inventory (`InventoryCacheDuration`), keyed by the GUIDs asked
  about, and repeated on **Refresh**. GUIDs go in batches of `MaxItemIdGuids` (100).
- Only GUIDs missing from the workspace's target inventory are asked about, so a workspace in sync asks
  nothing.
- A target on an older toolkit version doesn't answer. The items then show **New**, as before.
- The target lists each workspace the asked-for items are in, and keeps those items. Moves are rare, so
  this is usually none or one workspace.

The row's tooltip says where the item is on the target, and what to do:

> On the target it's still in *Dancing Goat Commerce*: Content Sync doesn't move items between
> workspaces, so move it to *Dancing Goat Events* on the target.

A workspace the editor can't open isn't named ("in another workspace", "to this workspace").

### The old workspace: Moved here

The row keeps its label, now grey like the rest of **Only on target**, since a sync can't change it.
The tooltip:

> Moved to *Dancing Goat Events* here, but still in this workspace on the target. Content Sync doesn't
> move items between workspaces: move it to *Dancing Goat Events* on the target, and don't delete it
> there. Its changes sync from *Dancing Goat Events*.

Once the item is moved on the target, both rows resolve: the old workspace no longer lists it, and the
new one compares it in place.

### Sync from the list

Selecting the item in its new workspace syncs it, which updates its content on the target, in the
workspace it's in there. It no longer reads as **New**.

## Compatibility

- **Target endpoint:** new, additive. `ILocalContentInventoryService.GetContentHubItemsByGuidAsync` and
  `IContentInventoryClient.GetContentHubItemsByGuidAsync` have default implementations, so existing
  implementations of these public interfaces still compile; they find nothing, which keeps today's
  behavior.
- **Schema version:** unchanged. The response is the existing inventory response.
- **Older target:** the source treats the missing endpoint as "not answered".

## Tests

- **Unit:** the comparison with a copy in another workspace (In sync), an older target (New), the
  lookup cached until a refresh, nothing asked when nothing is missing; the tooltips and the grey
  Moved here color.
- **Rig, `31.9.1`:** *Hario Mini Mill Slim* in *Dancing Goat Events* reads **In sync** with the note;
  in *Dancing Goat Commerce*, **Moved here** with the corrected tooltip.
