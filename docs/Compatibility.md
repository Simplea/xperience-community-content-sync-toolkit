# Xperience compatibility

Content Sync Toolkit separates its minimum supported Xperience version from the
latest version used by the integration site.

## Supported range

- Minimum supported Xperience version: `30.8.0`.
- Latest build-verified Xperience version: `31.9.1`.

`30.8.0` is not an arbitrary floor: it is the first version where Xperience's
own Content Sync feature covers both content-hub items (added in `30.5.0`, the
May 2025 Refresh) and website channel pages (added in `30.8.0`, the July 2025
Refresh). Content Sync Toolkit exists to add visibility into that feature, so a
version where Content Sync can't sync both kinds of content is not a
meaningful target regardless of what the toolkit's own query APIs could
technically compile against.

The NuGet package declares only the minimum version of `Kentico.Xperience.Admin`.
Consuming applications remain responsible for keeping their Xperience packages
aligned on a single version.

The absence of a NuGet upper bound does not mean that every future Xperience
release is automatically supported. The latest verified value records the newest
release covered by the project's compatibility checks.

## Development model

The library and unit tests compile against the minimum supported version. This
prevents contributors from accidentally using an API that is unavailable to
supported consumers.

The Dancing Goat integration site uses the latest verified version. It consumes
the library project built against the minimum, which checks that the resulting
library can be referenced by a current Xperience application.

Before release, validate the packed NuGet artifact in separate applications using:

1. the minimum supported Xperience version; and
2. the latest verified Xperience version.

## Verification record

Runtime checks of the two-instance setup (source and target on the same
version, as Content Sync requires) for `1.0.0-beta.1`, driven with the
Playwright CLI. The NuGet
package was packed from the library (built against the minimum) and installed
like a consumer would.

| Version | How | Result |
| --- | --- | --- |
| `31.9.1` (latest) | The repository's Dancing Goat rig, both databases updated from `31.7.2` with `--kxp-update` | Pass |
| `30.8.0` (minimum) | Two fresh Dancing Goat sites from `Kentico.Xperience.Templates` `30.8.0`, with the packed package | Pass |

Checked on both versions:

- the target endpoint: the Content Sync secret gets `200`, any other `404`;
  `Cache-Control: no-store`; schema 3 with display names and page order;
- the application in the **Content management** menu with its icon;
- both tabs: rows, content type and item display names, row links to the
  editor, dates in the editor's time zone, and the Refresh tooltip;
- every status tag's label and color: In sync (green), Missing on target
  (red), Out of date and Order differs on target (amber), Extra on target
  (grey on `30.8.0`; no extra item on the `31.9.1` rig);
- **Order differs on target** naming the page out of place, and its fix;
- the required-objects banner and "Can't sync yet" tooltips (a changed content
  type definition);
- the **Target unavailable** row (target stopped) and the not-configured banner
  (`31.7.2`, `30.8.0`).

Page permissions, with a restricted editor (role Article reviewer, with
Access channel and View on Sync status), compared with what Kentico's own page
tree shows the same editor:

| Role's channel permissions | `31.9.1` | `30.8.0` |
| --- | --- | --- |
| Access channel + Manage permissions | every page (65), as in the tree | every page (63), as in the tree |
| Access channel only, no page permissions | no pages, tree shows only the root | no pages, tree shows only the root |
| Display on the root, inheritance broken on Articles without the role | (checked on `31.7.2`: all but Articles) | 56 pages, all but the 7 under Articles, as in the tree |

On `30.8.0` the template has no non-administrator account, so the editor was
invited through the Users application; with email sending not configured, the
invitation link was taken from the email queue, as the Users application
suggests.

Differences between the versions that don't affect the toolkit: on `30.8.0`,
listing rows are clickable elements rather than `<a>` links, dropdown options
are buttons, and the admin home page doesn't list applications as tiles.

### 1.0.0-beta.2 statuses

`1.0.0-beta.2` replaced the four statuses with nine, Incompatible and Not
published among them, and made the Status filter a multi-select (see the
[Usage Guide](Usage-Guide.md#statuses)). Checked live on `31.9.1` on the
repository's rig, in a 1440px-wide window:

- every column fits without cutting a status label or a date;
- the Incompatible, Reordered, Not published and In sync tags in their colors, and
  their tooltips;
- the Hide items in sync checkbox on each tab, and that unchecked it filters
  nothing;
- the multi-select Status filter: options in order, two options combined on
  each tab, and an incompatible Changed item matching Incompatible, not
  Changed;
- the default sort (Incompatible first), and the status and header tooltips;
- that the required-objects banner no longer shows;
- with Kentico's own Content Sync, a content item unpublished (Unpublished),
  synced (In sync), re-drafted (Not published), republished (Changed) and
  synced back (In sync);
- the newer-draft note on a published article whose new version is in a
  workflow step (Ready for review), and on a published content item whose new
  version is scheduled to publish.

Checked live on `30.8.0` (October 2026), with the packed `1.0.0-beta.2`
package on two fresh Dancing Goat sites from `Kentico.Xperience.Templates`
`30.8.0`, in a 1440px-wide window:

- every staged status with its color and tooltip: Incompatible (red, the
  target's Home page type changed), Unpublished, New, Changed (unpublished on
  the target), Moved and Reordered (orange), Not published and Only on target
  (grey), and In sync (green) with the newer-draft note;
- the Hide items in sync checkbox, and the multi-select Status filter
  (Unpublished and Only on target combined);
- the Status header tooltip, and every column fitting the 920px grid.

`30.8.0` labels the content item editor's button **Edit content item** where
`31.9.1` says **Create new version**; that doesn't affect the toolkit.

### Large inventories, languages and target links

Checked after #19 (keyset paging and named columns), #21 (no language
fallbacks) and #22 (links to items on the target), with `main` packed as a
package and installed on two fresh Dancing Goat sites from
`Kentico.Xperience.Templates` `30.8.0` (October 2026):

- **Inventories:** every inventory of both instances (pages and three
  workspaces, English and Spanish) exported and compared with the released
  `1.0.0-beta.2` on the same databases. The English ones were field-for-field
  identical, across every publish state (published, unpublished, never
  published, unpublished then edited again). The Spanish ones listed only the
  items with a Spanish variant (6 pages and 2 content hub items per instance,
  where `1.0.0-beta.2` listed every English item), with Spanish display names.
  The sites have fewer than 5,000 items per inventory, so each was read in one
  batch; reading many batches was measured on `31.9.1` with 20,000 items per
  tab (see the foundation spec's Large inventories).
- **Target links:** an Only on target content hub item and page each linked to
  their editor on the target (`content-hub/1/en/all/list/182/content`,
  `webpages-1/en_9`) and opened it in a new tab, with the status list kept; a
  row that exists on the source still opened its local editor. The page was
  made target-only for the test by giving its target copy a new GUID, then
  restored.

The `30.8.0` template doesn't bind Content Sync's options from configuration,
so the sites added
`builder.Services.Configure<ContentSynchronizationOptions>(builder.Configuration.GetSection("ContentSynchronization"))`,
as the repository's Dancing Goat does; without it the target role stays off
and the toolkit's endpoints answer `404`.

Non-administrator editors on `31.9.1`, with 20,000 pages: the Pages tab took
about the same time as for an administrator (0.35–0.55 s per request) for a
role with Manage permissions on the channel, which sees every page, and about
0.75–0.8 s when page permissions had to be checked for every page.

### 1.0.0-beta.4

Checked in November 2026, before the release.

`31.9.1`, on the repository's Dancing Goat rig, driven with the Playwright CLI:

- the pages inside **Content synchronization** (#27); every language listed
  by default with the Language column (#29); **Moved here** for an item moved
  to another workspace, and a sync from its new workspace clearing it (#28);
  the **Modified by** column and **Only my changes** (#32), with one page
  marked as last edited by another user;
- issue #33, in fresh browser profiles: an empty workspace sorting first is
  skipped; an all-in-sync workspace lists its items; **Hide items in sync**
  is remembered after a reload; **Clear all** and Xperience's **Clear your
  filters here** never leave the editor without the toolbar; a channel whose
  only page was never published is skipped; and a restricted editor (Article
  reviewer, with View on Content synchronization and Access channel on both
  channels) who can't see a channel's only page opens on the next channel,
  while an administrator opens on it;
- the column widths at 1440 and 1920 px wide.

`30.8.0`, with the library packed as `1.0.0-beta.4-local33` and installed on a
fresh Dancing Goat site from `Kentico.Xperience.Templates` `30.8.0`, run as
source and target on the existing `30.8.0` databases: both tabs load inside
**Content synchronization**, with the Language and Modified by columns, the
Only my changes and Hide items in sync filters, every item listed with the
pending ones first, and each status tag in its color (Incompatible red;
Unpublished, New, Changed and Moved orange; Not published and Only on target
grey; In sync green). Not repeated on `30.8.0`: the restricted editor, the
recycle bin and workspace move hints, and a second language.

A large site on `31.9.1`, to check the new lookups (Modified by, Deleted here,
Moved here, every language, the default channel or workspace) at scale:
20,000 pages and 20,000 Content hub items created and published through the
Xperience API, the first 2,000 of each with a Spanish version, then the source
copied to the target. On the source after that: 300 of each republished (half
by another user), 100 with a newer draft, 50 unpublished, 100 Spanish versions
republished, 30 pages moved, 200 new pages and items, 2,200 of each moved to
the recycle bin and 100 deleted permanently (2,300 rows **Only on target** per
tab), and 50 items moved to another workspace. Requests timed in the browser,
with the target on the same machine:

| Listing request | Pages (~22,700 rows) | Content hub (~22,300 rows) |
| --- | --- | --- |
| Administrator, first visit after a restart (fetches the target) | 1.7 s | 0.9 s |
| Administrator, paging, sorting, search, filters | 0.4–0.5 s | 0.3 s |
| Editor with Manage permissions on the channel | 0.4–0.5 s | 0.3 s |
| Editor who sees every page through page permissions | 0.8–0.9 s | 0.3 s |
| Editor who sees no page (default-scope check reads every page) | 0.9 s, 2.3 s first visit | 0.3 s |

Every status was listed as expected: **Deleted here** with its **In recycle
bin** link on the recycled rows, **Only on target** on the permanently deleted
ones, **Moved here** on the moved items, and **Only my changes** listing only
the other user's 300 changes for them. The recycle bin and workspace move
lookups each received the 2,300 only-on-target GUIDs in one query without
errors.

Upgrading a role that had View on the old **Sync status** application isn't
enough: since #27 the pages need View on **Content synchronization**, and
without it the editor gets Access Denied (seen on `31.9.1`; the Usage Guide's
upgrade note covers it).

## Updating versions

- Retest and update the latest verified version after every Xperience refresh.
- Do not raise the minimum merely because a newer Xperience release is available.
- Raise the minimum only when required by an API, security fix, runtime lifecycle,
  or an intentional change to the support window.
- After a stable Content Sync Toolkit release, raising the minimum Xperience
  version is a breaking compatibility change and requires a new major toolkit
  version.
