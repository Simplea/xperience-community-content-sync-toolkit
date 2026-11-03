# Feature specification: Recycle bin hints for items only on the target

Status: **proposal**, not scheduled. Nothing here is built yet.

## Context

An item deleted on the source after a sync stays on the target and shows as
**Only on target**. Content Sync can't delete, so today the toolkit offers one
way out: the ↗ link opens the item in the target's administration, where the
editor deletes or unpublishes it (see [Open items on the
target](target-item-links.md)). That needs an account on the target, and many
teams don't give editors access to production.

Since Xperience `30.12.0`, a deleted item first goes to the **recycle bin**,
where it stays for a retention period (30 days by default). While it's there,
the editor can take it off production with Content Sync alone, without signing
in to production: restore it on the source, publish it, unpublish it (with a
redirect), and sync. This proposal makes the toolkit point that out.

## Research findings

Checked live on the `31.9.1` rig (October–November 2026) and in Kentico's
documentation ([Recycle bin](https://docs.kentico.com/documentation/business-users/recycle-bin),
[Recycle bin management](https://docs.kentico.com/documentation/developers-and-admins/configuration/recycle-bin-management),
[Work with content sync](https://docs.kentico.com/guides/digital-marketing/work-with-content-governance/work-with-content-sync)).

### The recycle bin

- **Version.** Added in `30.12.0` (November 2025 Refresh). The toolkit's
  minimum is `30.8.0`, so the feature must detect it.
- **Storage.** `CMS_RecycleBinContentItem`, object type
  `CMS.ContentEngine.RecycleBinContentItemInfo` (public namespace). One row per
  deleted language variant: `RecycleBinContentItemContentItemGUID`, display
  name, language, channel, content type, the user who deleted it and when, the
  original location (the tree path for pages), and the item's serialized data.
- **Pages.** The row is keyed by the *content item* GUID; the page's
  `WebPageItemGUID`, which the toolkit matches pages by, is only inside the
  serialized data (Kentico's XML serialization, `<WebPageItemGUID>`). Kentico
  has a public `IRecycleBinContentItemDataDeserializer` for that data.
- **Indexes.** On the channel, language, content type and deleting user; none
  on the GUID. The table only holds the retention period's deletions.
- **Deleted rows.** The page's or item's own rows are removed from
  `CMS_WebPageItem` and `CMS_ContentItem`; only the recycle bin row remains.
- **Permissions.** Users see only items they deleted, unless their role has
  **Manage recycle bin** (administrators see all). Restoring also needs Create
  permission where the item goes back.
- **Retention.** Configurable in **Settings → Content → Recycle bin**; after it,
  the item is permanently deleted.

### Restoring

- A restored page keeps both GUIDs (`WebPageItemGUID` and `ContentItemGUID`)
  and gets new IDs (page 69 → 73 on the rig). Content Sync matches by GUID, so
  it's still the target's page.
- It comes back as **Draft (Initial)** with no publish date: to Content Sync,
  a never-published item, which it can't sync. It has to be published first.
- References to it in other content are restored too: the sync dialog's
  Detailed selection listed about 20 linked pages and items (unchecked by
  default).
- With a workflow on the section, the restored draft goes through **Change
  workflow step → Schedule / Publish**; on a site with approval steps, that
  can need someone's approval.

### Unpublish redirects sync

Unpublishing a page with **Redirect to** a page, then syncing it, gives the
target the same redirect: the same former-URL row (same GUID, redirect flag,
redirect target) and the same `301` on the live site. Without it, an
unpublished page answers `404`.

### End to end

On the rig, with *(Clone) On Roasts* (published on both instances):

| Step on the source | Toolkit status | Target |
| --- | --- | --- |
| Move to recycle bin | Only on target ↗ | published, `200` |
| Restore | Not published (same GUID) | published |
| Publish | Changed | published |
| Unpublish, redirect to Articles | (source `301 → /articles`) | published |
| Sync this page | | unpublished, `301 → /articles` |
| Delete again (optional) | Only on target ↗, "Unpublished on the target, and not on this instance" | unpublished, `301` |

## Goal

For an **Only on target** item that's in the source's recycle bin, tell the
editor so, and how to take it off the target with Content Sync, without
production access. The toolkit doesn't restore, publish, unpublish or sync
anything itself.

## Proposed design

### What the editor sees

- **Tooltip.** On an Only on target row found in the recycle bin:
  "Deleted here on 29 Oct by Ana Pérez; it's in the recycle bin until 28 Nov.
  To remove it from the target with Content Sync: restore it, publish it,
  unpublish it (choose a redirect), and sync it. Or open it on the target (↗)."
- **Link to the recycle bin.** The Recycle bin application, searched by the
  item's name if its URL allows it. The ↗ link to the target stays.
- **No new status.** The row stays **Only on target**; the hint is extra
  information for the rows on the page being shown. A status would have to be
  known for every row before filtering, sorting and paging (see Performance).

### Lookup

- Runs while building a listing page, only for its Only on target rows, and
  only when there are any: one query against the recycle bin, filtered by the
  scope's channel (pages) or workspace, the language, and the rows' content
  item GUIDs.
- **Content item GUIDs for pages.** The target's item ID routes
  (`web-page-ids`, see [Open items on the target](target-item-links.md)), which
  the page already calls for these rows, also return each page's
  `ContentItemGUID` (a schema bump). The source then queries by that GUID, the
  table's key. Reading the page GUID out of the serialized data instead would
  mean loading 9–16 KB of XML per bin row.
- **Access.** Through `ObjectQuery` by object type name and column names, as
  for display names, since the library builds against `30.8.0` where the class
  doesn't exist. If the object type isn't registered (before `30.12.0`) or the
  query fails, no hint is shown and the error is logged once.
- **Permissions.** The hint is shown only when the signed-in user could see
  the item in the recycle bin: they deleted it, or their role has **Manage
  recycle bin**, or they're an administrator.
- **Expiry date.** From the deletion date plus the retention setting, if the
  setting can be read through a public API; otherwise the tooltip omits it.

### Performance

- Most listing pages have no Only on target rows, so most loads don't query.
- When they do: one query for at most one listing page of GUIDs (50) against a
  table that holds only the retention period's deletions, filtered by indexed
  columns. Expected cost: milliseconds.
- The target's response grows by one GUID per Only on target row of the page,
  not per inventory item.

## Open questions

- **Deleted parent.** Restoring a page whose parent was also deleted: does
  Kentico restore the parent, refuse, or put the page elsewhere? Not tested.
- **Several deletions of one item.** The same item deleted, restored and
  deleted again leaves one row per deletion? Show the latest.
- **Language variants.** Deleting one language variant leaves the page in the
  others; the hint must match the selected language only.
- **Content hub items.** The flow is the same, but an unpublish only reaches
  the target when the target has the item published (Kentico's rule); confirm
  live.
- **Done items.** An item deleted here and unpublished on the target stays
  Only on target for good. Should **Hide items in sync** also hide "only on
  target and unpublished there"?
- **One-click restore and unpublish.** A later step could run the restore,
  publish and unpublish on the source through Kentico's APIs. It changes
  content on the source and has to respect workflows; out of scope here.

## Out of scope

- Restoring, publishing, unpublishing or syncing from the toolkit.
- Changing anything on the target.
- A status of its own for items in the recycle bin.
- Emails and headless items, which the toolkit doesn't compare.

## Test strategy

- **Unit:** the hint text (with and without an expiry date), the permission
  rule, the lookup's handling of a missing object type.
- **Live, on `31.9.1` or later:** a deleted page and content item show the
  hint; another user without Manage recycle bin doesn't see it; the hint
  disappears after the item is restored or permanently deleted; the end-to-end
  flow above still works.
- **Live, on `30.8.0`:** no hint and no errors.
