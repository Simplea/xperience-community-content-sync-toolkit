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
  shows the result in the **Content sync status** admin application.

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

// Source instances only: adds the Content sync status admin application.
builder.Services.AddContentSyncToolkitAdmin();
```

The target's inventory endpoint is an attribute-routed controller under
`/xperience-community/content-sync-toolkit/inventory/`. It's served by the
same controller routing Xperience sites already use (`MapControllerRoute` or
`MapControllers`).

## Configure source and target

Both instances share a secret. Use a long random value, keep it out of source
control (user secrets, environment variables, or a key vault), and serve the
target over HTTPS so the secret isn't sent in clear text.

**Target** (`appsettings.json` or equivalent):

```json
{
  "ContentSyncToolkit": {
    "Target": {
      "Enabled": true,
      "Secret": "<shared secret>"
    }
  }
}
```

**Source**:

```json
{
  "ContentSyncToolkit": {
    "Source": {
      "TargetUrl": "https://target.example.com",
      "Secret": "<shared secret>",
      "RequestTimeout": "00:00:30",
      "InventoryCacheDuration": "00:01:30"
    }
  }
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `Target:Enabled` | `false` | Whether this instance answers inventory requests. When `false`, every request is rejected. |
| `Target:Secret` | — | Secret a source must send. Must match the source's `Source:Secret`. |
| `Source:TargetUrl` | — | Base URL of the target. This instance acts as a source only when it's set. |
| `Source:Secret` | — | Secret sent to the target. |
| `Source:RequestTimeout` | 30 seconds | Timeout for requests to the target. |
| `Source:InventoryCacheDuration` | 90 seconds | How long the target's inventory is cached on the source. The source's own content is never cached. |

## Give editors access

The **Content sync status** application (under **Configuration**) uses
Xperience's standard **View** permission. Administrators have it
automatically. For other users, open **Role management**, edit a role,
**Add permission set**, and choose **For** *Content sync status* **allow users
to** *View*. Users without it don't see the application, and direct
navigation to it is refused.

## Use the Content sync status application

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
- **Search** by path (Pages) or name (Content hub); press Enter to apply.
- **Sort** by clicking a column header. By default, items needing action come
  first: Missing on target, then Out of date on target, then Extra on target,
  then In sync.
- **Click a row** to open the item in its editor, where you can sync it with
  Xperience's own Content Sync actions. Extra on target rows can't be opened,
  because the item doesn't exist on this instance.
- **Refresh** fetches the target's inventory again instead of using the cached
  copy. The applied filters and search are kept.
- Hover the ⓘ next to **Status** or **Last published** for a short explanation.

| Status | Meaning |
| --- | --- |
| **Missing on target** | Published on this instance but absent on the target. |
| **Out of date on target** | Published more recently on this instance than on the target. |
| **Extra on target** | On the target but not published on this instance. |
| **In sync** | The target has the same published version. |

Other states:

- **A warning banner saying the application isn't configured**: `Source:TargetUrl`
  isn't set on this instance.
- **A "Target unavailable" row**: the target couldn't be reached, rejected the
  secret, or has `Target:Enabled` set to `false`.
- **No rows**: the channel or workspace has no published content in the
  compared language, or nothing matches the applied filters.
- **A "Not available" row**: the selected channel, workspace, or language was
  deleted after the filter was applied. Clear that filter or choose another.

## What is compared

- **Only published content.** An item appears once it's published, because
  Content Sync can't transfer never-published items. A newly created or cloned
  item shows up only after publishing it. An item with a pending draft is
  compared by its last published version, which is what Content Sync would
  push.
- **Unpublished items aren't listed yet.** An item unpublished on the source
  but still published on the target currently shows as **Extra on target**.
  See the [known gap](specs/content-inventory-foundation.md#known-gap-unpublished-items).
- **Timestamps, not content.** Status comes from publish timestamps, so large
  clock differences between the two servers can affect **Out of date on
  target**.
- **One language at a time.** The default content language unless you choose
  another with the Language filter.
