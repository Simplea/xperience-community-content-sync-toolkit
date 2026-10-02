# Usage Guide

Content Sync Toolkit shows which content on one Xperience by Kentico instance
(the **source**) is missing from or out of date on another (the **target**),
so editors can see what Xperience's Content Sync still needs to push.

The toolkit is under initial development and not yet published to NuGet. See
the [README](../README.md) for status and the [compatibility policy](Compatibility.md)
for supported Xperience versions.

## How it works

Install the toolkit on **both** instances:

- The **target** answers inventory requests from the source: a list of its
  items' identities and publish timestamps. No field values leave the
  instance.
- The **source** compares its own content with the target's inventory and
  shows the result in the **Sync status** admin application.

An instance can be a source, a target, or both. Content Sync itself requires
source and target to run the same Xperience version; run the same toolkit
version on both as well.

## Register the toolkit

In `Program.cs` on each instance:

```csharp
using XperienceCommunity.ContentSyncToolkit;
using XperienceCommunity.ContentSyncToolkit.Admin;

builder.Services.AddContentSyncToolkit();
builder.Services.Configure<ContentSyncToolkitOptions>(
    builder.Configuration.GetSection("ContentSyncToolkit"));

// Optional: RequestTimeout and InventoryCacheDuration (see below).
// Source instances only: adds the Sync status admin application.
builder.Services.AddContentSyncToolkitAdmin();
```

The target's inventory endpoint is an attribute-routed controller under
`/xperience-community/content-sync-toolkit/inventory/`. It's served by the
same controller routing Xperience sites already use (`MapControllerRoute` or
`MapControllers`).

## Configure source and target

There's nothing toolkit-specific to configure: the toolkit uses Xperience's
own Content Sync configuration
([Content sync configuration](https://docs.kentico.com/documentation/developers-and-admins/configuration/content-sync-configuration)).

| Content Sync setting | What the toolkit does with it |
| --- | --- |
| `Source:Enabled` + `Source:TargetUrl` | This instance is a source, and the status page compares against that target. |
| `Source:Secret` | Sent with the toolkit's inventory requests. |
| `Target:Enabled` | This instance answers the toolkit's inventory requests. |
| `Target:Secret` | Required on those requests. |

So the page always compares against the instance Content Sync pushes to, with
the same secret. That adds no exposure: whoever holds the secret can already
push content to the target. Content Sync requires the target on HTTPS with a
trusted certificate, so the toolkit's requests use HTTPS too.

If Content Sync isn't configured as a source, the status page shows a banner
saying so; if it isn't configured as a target, the target rejects inventory
requests. The toolkit has no purpose without Content Sync.

Two optional toolkit settings tune the source's requests:

```json
{
  "ContentSyncToolkit": {
    "RequestTimeout": "00:00:30",
    "InventoryCacheDuration": "00:01:30"
  }
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `RequestTimeout` | 30 seconds | Timeout for requests to the target. |
| `InventoryCacheDuration` | 90 seconds | How long the target's inventory is cached on the source. The source's own content is never cached. |

## Give editors access

The **Sync status** application (under **Content management**) uses
Xperience's standard **View** permission. Administrators have it
automatically. For other users, open **Role management**, edit a role,
**Add permission set**, and choose **For** *Sync status* **allow users
to** *View*. Users without it don't see the application, and direct
navigation to it is refused.

Within the application, editors only see what they could open in Xperience
itself:

- **Pages** lists only website channels whose Pages application they can view.
- **Content hub** lists only workspaces whose content items they can view.

- **Pages** also lists only the pages they can see in that channel's page tree:
  page permissions (Display) apply here too, including sections where
  inheritance is broken. Administrators and roles with **Manage permissions**
  on the channel see every page, as in the page tree.

So a role with access to one workspace sees only that workspace here, even
with View on Sync status, and an editor who can't see a section of the page
tree doesn't see it here either.

## Use the Sync status application

The application has two tabs: **Pages** (one website channel at a time) and
**Content hub** (one workspace at a time).

- **Filter** opens the filter panel. All fields are optional and combine:
  - **Channel** (Pages) or **Workspace** (Content hub). Without a selection,
    the first one is shown.
  - **Language**. Without a selection, the instance's default content language
    is compared.
  - **Status**. **Needs action** shows what Content Sync still has to push:
    Missing on target plus Out of date on target.
  - **Content type**.
  - **Published from** / **Published to**: whole days, both included, matched
    against the Last published column.
- **Search** by path (Pages) or name (Content hub, the name the Content hub
  shows); press Enter to apply.
- **Sort** by clicking a column header. The list starts sorted by **Status**,
  so items needing action come first: Missing on target, then Out of date on
  target, then Extra on target, then In sync. Within each status, the most
  recently published items come first. Click **Status** to reverse the order.
  The page doesn't remember a different sort: it starts sorted by Status again
  each time you open it or use Refresh.
- **Click a row** to open the item in its editor, where you can sync it with
  Xperience's own Content Sync actions. Extra on target rows can't be opened,
  because the item doesn't exist on this instance.
- **Refresh** fetches the target's inventory again instead of using the cached
  copy. The applied filters and search are kept. Without it, the target's list
  is reused for up to 90 seconds (`InventoryCacheDuration`), so a page can
  still show an item as missing right after you sync it. The target also
  applies a sync within about 30 seconds of receiving it, so wait that long,
  then use Refresh. Hover the button for a reminder.
- Hover the ⓘ next to **Status** or **Last published** for a short explanation.

| Status | Meaning |
| --- | --- |
| **Missing on target** | Published on this instance but absent on the target. |
| **Out of date on target** | Published more recently on this instance than on the target, published on one and unpublished on the other, or moved (hover the status to see which and what to sync). |
| **Order differs on target** | The pages on this level are in a different order on the target. See below. |
| **Extra on target** | On the target but not on this instance. |
| **In sync** | The target has the same published version. |

Other states:

- **A warning banner saying the application isn't configured**: Content Sync
  isn't configured as a source on this instance (`Source:Enabled` with a
  `Source:TargetUrl`).
- **A warning banner saying some items can't be synced**: the target is
  missing a content type, language, channel or workspace this instance has, or
  has it differently (different fields, or recreated by hand instead of
  deployed). Content Sync doesn't transfer these and fails for items that use
  them, so a developer has to deploy them to the target first (CI/CD or a
  deployment package). The status tooltip of each affected item starts with
  "Can't sync yet" and says what it needs. Linked items, reusable field
  schemas, image variants, member roles and project code aren't checked.
- **A "Target unavailable" row**: the target couldn't be reached, rejected the
  secret, or doesn't have Content Sync's target role enabled.
- **No rows**: the channel or workspace has no published content in the
  compared language, or nothing matches the applied filters.
- **A "Not available" row**: the selected channel, workspace, or language was
  deleted after the filter was applied. Clear that filter or choose another.

### Why does a whole level show "Order differs on target"?

Content Sync sends each synced page's position with it, but pages you don't
include keep their old positions on the target. So syncing only some pages of a
level can leave the level in a different order there, for example after
reordering pages here, or after syncing a single new page. The status page then
marks every page on the level, because Content Sync only transfers an order
change when the whole level is synced.

- Hover the tag: it names the page or pages that are out of place.
- To fix it, open the page tree, and on the level's parent page use **Sync with
  all subpages**.
- If your site never shows pages in page tree order (for example, a listing
  sorted by date), the difference has no visible effect and you can ignore it.

## What is compared

- **Published and unpublished content, not drafts.** An item appears once
  it's been published, because Content Sync can't transfer never-published
  items. A newly created or cloned item shows up only after publishing it. An
  item with a pending draft is compared by its last published version, which
  is what Content Sync would push.
- **Unpublished items are compared by publish state.** An item unpublished on
  one instance and published on the other shows as **Out of date on target**,
  whichever side unpublished it, and the status tooltip says which. An
  unpublished page the target doesn't have shows as **Missing on target**. An
  unpublished content hub item the target doesn't have isn't listed, because
  Content Sync wouldn't create it. See
  [Publication-state scope](specs/content-inventory-foundation.md#publication-state-scope).
- **Secured pages are included,** like any other page.
- **Moved and reordered pages.** Moving or reordering a page doesn't republish it, so the page compares positions too: a page at a different path
  on the target shows as **Out of date on target**, and a level whose pages
  are in a different order shows as **Order differs on target**. To sync
  either, Content Sync needs all pages on the affected level (for a move, the
  old and the new one); the status tooltip says so.
- **Deleted items.** Content Sync can't delete anything on the target. An item
  deleted here stays on the target and shows as **Extra on target** until
  someone deletes it there.
- **Dates are in your time zone,** in the Last published column, like the rest
  of the administration. The Published from and Published to filters use the
  server's time zone, so near midnight a day can differ slightly.
- **Timestamps, not content.** Status comes from publish dates (compared in
  UTC, so servers in different time zones are fine) and from publish state and
  position. Large clock differences between the two servers can still affect
  **Out of date on target**.
- **What the page can't see:**
  - **Edits made directly on the target.** After a sync, the target's copy has
    the newer date, so later edits there still show as **In sync**. Kentico
    recommends editing only on the source; a sync overwrites target edits.
  - **Scheduled publish or unpublish dates.** Content Sync doesn't transfer
    them, and neither status reflects them.
- **One language at a time.** The default content language unless you choose
  another with the Language filter.
