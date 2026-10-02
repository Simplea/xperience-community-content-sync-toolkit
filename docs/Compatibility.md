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
version, as Content Sync requires), driven with the Playwright CLI. The NuGet
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

## Updating versions

- Retest and update the latest verified version after every Xperience refresh.
- Do not raise the minimum merely because a newer Xperience release is available.
- Raise the minimum only when required by an API, security fix, runtime lifecycle,
  or an intentional change to the support window.
- After a stable Content Sync Toolkit release, raising the minimum Xperience
  version is a breaking compatibility change and requires a new major toolkit
  version.
