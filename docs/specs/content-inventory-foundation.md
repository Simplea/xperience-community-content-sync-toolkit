# Feature specification: Content inventory and sync status foundation

Status: Implemented and runtime smoke-tested on Xperience by Kentico `31.7.2`
Repository: `Simplea/xperience-community-content-sync-toolkit`
Supported baseline: Xperience by Kentico `30.8.0` or newer (the first version
where Content Sync covers both content-hub items and website channel pages);
build-verified through `31.7.2`; .NET 8
Research date: 2026-08-11

## Context

Xperience Community Content Sync Toolkit is an independently owned integration
maintained by Simplea for Xperience by Kentico. It is a reusable library, not an
Xperience website and not a Kentico-owned product. The Dancing Goat project
under `examples/` is the integration host used to run and verify the library.

This specification covers the toolkit's shared foundation, not an end-user
feature by itself. Two planned editor-facing features —
[sync-status-admin-page](sync-status-admin-page.md) and
[content-tree-sync-indicators](content-tree-sync-indicators.md) — both need the
same missing capability: knowing what content exists on a target Xperience
instance and diffing it against the source. This foundation provides that
capability so both features consume one implementation instead of two. See
[Architecture](../Architecture.md) for how these pieces fit into the toolkit as
a whole.

## Research findings

As of the research date, the current Xperience by Kentico documentation and
shipped SDK confirm:

- Content Sync shipped in two phases, confirmed via Kentico's own refresh
  announcements: content-hub items in the **May 2025 Refresh (`30.5.0`)**, and
  website channel pages in the **July 2025 Refresh (`30.8.0`)**. Since this
  toolkit covers both content kinds from the start, `30.8.0` is the earliest
  version where it has anything to be useful for — an older version's Content
  Sync can't sync pages at all, regardless of what this toolkit's own query
  APIs could technically run against.
- Content Sync (the built-in feature, distinct from this toolkit) connects a
  source instance to a target instance over HTTPS using a shared secret
  (`ContentSynchronizationOptions.Source.TargetUrl`/`.Secret` on the source,
  `.Target.Enabled`/`.Secret` on the target). Synchronization tasks are stored
  in `CMS_Synchronization` and asset files under `~/assets/synchronizations`,
  applied by a "Content sync restoration" scheduled task on the target that
  runs every 30 seconds.
- The only sync-visibility UI — the **Content synchronization** application —
  exists only on the target instance and shows completed/scheduled sync tasks,
  not a diff of what's missing.
- No documented public REST or Management API exists for a third party to query
  whether a given content item GUID exists on a target instance.
  `Kentico.Xperience.ManagementApi` (currently preview) was checked and only
  covers content-type/schema management, entirely under an `Internal`
  namespace — not applicable to content item instances.
- The relevant content-query APIs ship in `CMS.ContentEngine` (reusable content
  items: `IContentQueryExecutor`, `ContentItemQueryBuilder`,
  `ContentItemQueryBuilder.InWorkspaces(string[])`) and `CMS.Websites` (web
  pages: `ContentTypesQueryParametersExtensions.ForWebsite(ContentTypesQueryParameters,
  string websiteChannelName, PathMatch, bool)`), both transitively available via
  the `kentico.xperience.admin`/`kentico.xperience.webapp` packages already
  referenced by this repository. Confirmed directly against the shipped XML
  documentation for `kentico.xperience.core` `31.7.2` rather than assumed.
- Identity and staleness signals available on queried content:
  `SystemFields.ContentItemGUID` / `WebPageItemGUID`,
  `SystemFields.ContentItemCommonDataLastPublishedWhen`,
  `ContentItemCommonDataVersionStatus`. No content hash is exposed — only
  publish timestamps.

This specification therefore treats the inventory/diff foundation as a toolkit
extension with no equivalent in Xperience itself: nothing here wraps or extends
an existing product feature.

## Goal

Let a source instance determine, for a given website channel or content-hub
workspace, which of its local content items are missing, out of date, or
in sync on a configured target instance — without requiring direct database
access to the target and without depending on Kentico's internal Content Sync
implementation types.

## Data contract

### Configuration: Xperience's Content Sync settings

The toolkit complements Xperience's Content Sync and has no source or target
configuration of its own. Content Sync already configures everything the
toolkit needs — the target's URL and a shared secret, in the public
`CMS.ContentSynchronization.ContentSynchronizationOptions` — so the toolkit
reads those directly, through `IContentSyncToolkitSettings`:

| Toolkit behavior | Content Sync setting |
| --- | --- |
| This instance is a source, comparing against that target | `Source.Enabled` and an absolute `Source.TargetUrl` |
| Secret sent with inventory requests | `Source.Secret` |
| This instance answers inventory requests | `Target.Enabled` |
| Secret required on those requests | `Target.Secret` |

An instance's role therefore follows its Content Sync role exactly; one may be
both (for example, a staging environment that's a sync target of production
and a sync source toward a further environment). Every consumer (the HTTP
client, the secret validator, the admin page's "not configured" check) reads
the resolved settings.

Why not separate toolkit settings: an earlier version had its own
`TargetUrl`/`Secret`/`Enabled` settings, later with a fallback to Content
Sync's. Duplicating them made installers configure a second URL and secret,
and allowed the comparison to point at a different instance than the one
Content Sync pushes to. Without Content Sync the toolkit has nothing to
complement, so its settings were removed. Accepting Content Sync's target
secret on the inventory endpoint adds no exposure: whoever holds it can
already push content to the target. Content Sync requires the target on HTTPS
with a trusted certificate, so the toolkit's requests are HTTPS too.

The toolkit's own options are only what Content Sync has no equivalent for:

```csharp
public sealed class ContentSyncToolkitOptions
{
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan InventoryCacheDuration { get; set; } = TimeSpan.FromSeconds(90);
}
```

Verified live on 31.7.2 with only `ContentSynchronization__*` environment
variables on both rig instances: the target answered the Content Sync secret
with `200` and any other secret with `404`, and the source's status page
loaded without the "not configured" banner, fetching over
`https://localhost:27311`.

SaaS: Kentico configures Content Sync automatically for SaaS deployment
environments, from the connections defined in Xperience Portal. It does so
through the same options. `AddKenticoCloud(configuration)`, which Kentico's
SaaS `Program.cs` requires in every cloud environment, calls an internal
`AddXperienceCloudContentSynchronization` that binds
`ContentSynchronizationOptions` to the `CMSContentSynchronization`
configuration section the platform provides (checked by decompiling
`Kentico.Xperience.Cloud` 31.7.2). A LOCAL development source is configured by
hand with the Target URL and Connection secret Xperience Portal shows. Still
not verified on a real SaaS environment: what the platform puts in that
section, and that the SaaS edge lets the inventory GET requests through. Check
both before the first release.

Inventory responses carry `Cache-Control: no-store` (`ResponseCache` with
`NoStore` on the controller). Cloudflare, which fronts SaaS, wouldn't cache an
extensionless JSON route by default, but secret-gated responses must never
come from a shared cache.

### Inventory item

```csharp
public enum ContentInventoryItemKind { WebPage, ContentHubItem }

public sealed record ContentInventoryItem(
    Guid Guid,
    ContentInventoryItemKind Kind,
    string ContentTypeName,
    string ScopeName,             // website channel name or workspace name
    string LanguageName,
    string? TreePath,             // web pages only; null for content-hub items
    DateTime? LastPublishedWhen,  // UTC since schema version 2; see Time zones
    string? VersionStatus)
{
    public string Name { get; init; } = string.Empty;  // WebPageItemName / ContentItemName
    public int? Order { get; init; }                   // WebPageItemOrder; pages only, schema 2
}
```

`Name` and `Order` were added after the first version as init-only properties
rather than positional parameters, so the positional constructor stays source-
and binary-compatible. `Name` carries the item's code name for display, which
content-hub items need because they have no tree path. `Order` is the page's
position among its siblings, so a reorder can be detected (see Comparison
rules).

### Time zones

Xperience stores date and time values in the time zone of the **server** the
application runs on, and the content query returns them with an unspecified
kind (confirmed live: the database held `20:04` server time, UTC−5, for a
publish at `01:04` UTC). Schema version 1 of this spec assumed UTC and sent the
values unchanged, so two instances in different time zones compared wrong
moments and "out of date" could flip either way; it only worked because both
rig instances shared a machine.

Since schema version 2, `ContentInventoryTime.ToUtc` converts every
`LastPublishedWhen` to UTC when the inventory is built, and the endpoint sends
it with a `Z` suffix. The source normalizes what it receives the same way: a
value without a time zone, which a target on schema version 1 sends, is taken
as the source's own local time — exactly how it was compared before, so a mixed
pair is no worse than it was.

`VersionStatus` is a plain string, not Kentico's `VersionStatus` enum, so the
wire contract does not couple to Kentico's internal type layout across
potentially different versions running on the source and target.

### Target endpoint

- Route: `xperience-community/content-sync-toolkit/inventory`.
- `GET .../web-pages?channelName={name}&languageName={name}` and
  `GET .../content-hub-items?workspaceName={name}&languageName={name}`.
- Required header: `X-ContentSyncToolkit-Secret`.
- Response body: `{ SchemaVersion: 2, GeneratedAtUtc, Items: ContentInventoryItem[] }`.
  `SchemaVersion` is a forward-compatibility hook for a source and target
  running different toolkit versions. `1`: publish dates in server-local time
  without a time zone. `2`: publish dates in UTC, plus `Order` for pages.
  Readers accept both; an older reader ignores `Order`.
- Rejection (missing/wrong secret, or Content Sync's `Target.Enabled == false`): a bodiless
  `404`, which the host renders exactly as it renders any unknown URL — see
  Security and privacy.

## Local inventory behavior

`ILocalContentInventoryService` exposes two methods, not one method with a
kind discriminator, because the underlying query shapes genuinely differ:

```csharp
public interface ILocalContentInventoryService
{
    Task<IReadOnlyList<ContentInventoryItem>> GetWebPagesAsync(
        string websiteChannelName, string languageName, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContentInventoryItem>> GetContentHubItemsAsync(
        string workspaceName, string languageName, CancellationToken cancellationToken);
}
```

Both take the channel/workspace name as an explicit parameter rather than
relying on ambient `IWebsiteChannelContext`, because the implementation must
answer for whatever scope a remote caller asks about, not "the current
request's channel." Both page internally (`Offset` with `OrderBy`, looped until
a page returns fewer rows than requested) and return one complete in-memory list — see
Out of scope for why wire-level pagination is not part of this version.

Two things the original specification got wrong, corrected after verification
against a live two-instance test rig (not just doc/reflection inspection — see
[Contributing-Setup](../Contributing-Setup.md) for how to stand up the same
rig):

- `ContentItemQueryBuilder.ForContentTypes(...)` does **not** default to "all
  content types" when given an empty configuration — a live instance throws
  `"Cannot generate query without limiting content types"`. The doc phrasing
  ("Limits the collection... to...") implied an unfiltered baseline; it does
  not have one. `GetContentHubItemsAsync` therefore first enumerates every
  reusable content type's class name and passes that explicit list to
  `.OfContentType(...)`, using `CMS.DataEngine.DataClassInfoProvider`
  instantiated directly (`new DataClassInfoProvider()`), not injected — that
  class implements `CMS.DataEngine.Internal.INotManagedByContainer`, so
  Kentico deliberately excludes it from DI and expects direct construction,
  unlike `IContentQueryExecutor` and most other Info providers.
- An unknown website channel or language makes the content query **throw**
  (`ArgumentException` "Unable to find website channel…",
  `InvalidOperationException` "Language '…' does not exist"); only an unknown
  workspace quietly matches nothing. Found by a live check after the first
  release of this spec: the target answered `500` with the exception text, which
  both broke the "empty inventory" rule below and made the source report
  "Target unavailable" whenever the target lacked a channel or language the
  source had. `LocalContentInventoryService` now checks that the channel (of
  website type) and the language exist, through a small `IContentScopeLookup`,
  and returns an empty inventory if not. The check isn't a timing-safe
  operation, but an unknown and an empty scope both return the same body.
- The controller in [Target endpoint](#target-endpoint) must be declared `public`,
  not `internal`. ASP.NET Core's default `ControllerFeatureProvider` silently
  excludes non-public classes from controller discovery — the route never
  registers at all, and requests fall through to the host application's own
  404 handling with no error indicating why.

## Comparison rules

`ContentSyncStatusComparer` is pure, static logic matching local and remote
inventory by GUID:

| Local present | Remote present | Local timestamp | Remote timestamp | Status |
| --- | --- | --- | --- | --- |
| no | yes | — | — | `ExtraOnTarget` |
| yes | no | — | — | `MissingOnTarget` |
| yes | yes | null | any | `InSync` |
| yes | yes | not null | null | `OutOfDateOnTarget` |
| yes | yes | not null | not null | `OutOfDateOnTarget` if local > remote, else `InSync` (equal timestamps are `InSync`) |

Timestamps are compared in UTC (see Time zones). Publication state is checked
before this table (see Publication-state scope), and two page rules after it,
because moving or reordering a page changes neither its version nor its publish
date (position lives on the page record, `WebPageItemTreePath` and
`WebPageItemOrder`, not on its published version):

- **Moved:** both sides have the page, the table says `InSync`, but
  the tree paths differ (case-insensitively) → `OutOfDateOnTarget`.
- **Reordered:** for each parent level, the pages present on both sides are
  compared by their **relative** order. If it differs, every `InSync` page on
  that level becomes `OutOfDateOnTarget`. Order values can't be compared
  directly: a page that exists on only one side shifts every later sibling's
  value. The whole level is marked because Kentico's
  [Content sync](https://docs.kentico.com/documentation/business-users/content-sync#sync-moved-or-reordered-pages)
  documentation says reordered or moved pages need **all** pages on the level
  synced. Skipped when either side has no `Order` (a schema 1 target).

Verified live on 31.7.2: dragging `(Clone) On Roasts` above `On Roasts` in the
source's page tree swapped their `WebPageItemOrder` (7/6 → 6/7) and left both
publish dates unchanged. The status page then showed every page under
`/Articles` that also exists on the target as `OutOfDateOnTarget`, with "Page
order on this level changed here. To reorder the target, sync all pages on this
level."; `/Articles/Clone_Coffee_processing_techniques`, which exists only on
the source, stayed `MissingOnTarget`. Before the reorder the same level was
`InSync`, which also confirmed that a page present on only one side doesn't
cause a false reorder. The move rule isn't verified live (no page was moved to
another parent); it's covered by unit tests.

Each `ContentSyncStatusItem` carries a `Reason` for `OutOfDateOnTarget`:
`PublishedMoreRecently`, `PublishStateDiffers`, `Moved`, or `Reordered`
(`None` otherwise), so consumers can explain the status.

This remains a timestamp comparison across two independently running servers,
so it's sensitive to clock skew between instances. No content hash is available
to compare instead: Content Sync only exposes publish timestamps, and Kentico's
public `HashCalculationHelper` only hashes its own compatibility reports. It
also can't see edits made directly on the target after a sync (the target's
copy then has the newer date, so it reads `InSync`); Kentico's
[documentation](https://docs.kentico.com/documentation/business-users/content-sync#editing-process-when-using-content-sync)
says to edit only on the source, so this is documented rather than solved.
A content fingerprint (a hash of field values per item, computed on each side)
would close it, and is deferred: see Out of scope.

A failed remote fetch (target unreachable, rejected, or erroring) must never be
treated as "target has zero items" — that would make every local item falsely
report as `MissingOnTarget`. `IContentSyncStatusService` surfaces fetch failure
as `TargetAvailable = false` and does not run the comparer in that case.

### Status ordering for consumers

The comparer returns items unordered. Features that list them should use one
shared order, so the same content reads the same way everywhere:

1. By urgency — what Content Sync would add, then update, then what only the
   target has: `MissingOnTarget`, `OutOfDateOnTarget`, `ExtraOnTarget`,
   `InSync`.
2. Within a status, most recently published first (the local publish date, or
   the target's for `ExtraOnTarget`); never-published items last.
3. Then by path or name.

The [sync status admin page](sync-status-admin-page.md#sorting) uses this as
its default sort (`ContentSyncStatusListingSupport.StatusSortRank` and
`ApplySort`). It lives with the admin page rather than in this foundation
because ordering is presentation; if
[content-tree-sync-indicators](content-tree-sync-indicators.md) needs it, move
it next to the comparer instead of duplicating it.

## Publication-state scope

Both inventories (local, and the target's via its endpoint) list what Content
Sync can act on, per Kentico's
[Content sync](https://docs.kentico.com/documentation/business-users/content-sync)
documentation:

| Item state | In the inventory? | Why |
| --- | --- | --- |
| Draft (Initial), or a custom workflow step before the first publish | No | Content Sync cannot synchronize never-published items. Listing them as `MissingOnTarget` would ask an editor to do something they can't. |
| Published | Yes, `VersionStatus = "Published"` | — |
| Published, with a pending Draft (New version) or workflow step | Yes, compared by its last **published** version | Content Sync synchronizes the latest published version, not the draft — so a pending draft does not make the item out of date. |
| Unpublished (published once, then unpublished) | Yes, `VersionStatus = "Unpublished"` | Content Sync can push an unpublish: unpublished pages without restriction, and unpublished content-hub items when the target has them published. |
| Unpublished, then re-drafted (Draft (Initial) with a `LastPublishedWhen`) | No | See Verification results. Whether Content Sync acts on it is undocumented. |
| Secured (requires authentication) | Yes, in any of the states above | Content Sync syncs secured items like any other. |

In practice: a newly created or cloned item only appears once it's published.
That's expected, not a bug.

### Inventory queries

Each inventory runs two content queries, both with
`IncludeSecuredItems = true`, and merges them (a GUID is never listed twice;
the published row wins):

1. **Published versions** — the default `ForPreview = false`, which returns
   each item's published version even when a newer draft exists.
2. **Unpublished items** — `ForPreview = true`, restricted to
   `ContentItemCommonDataVersionStatus = Unpublished`. Two queries rather than
   one `ForPreview = true` query, because the latter returns a pending draft's
   row in place of the published version for published-with-draft items,
   changing what those are compared by.

Unpublished rows get `VersionStatus = "Unpublished"` from a constant
(`ContentInventoryVersionStatus.Unpublished`), not from
`VersionStatus.ToString()` — see the naming difference below.

`IncludeSecuredItems` defaults to `false`. Before it was set, a secured page
(Dancing Goat's `/Articles/Coffee_Beverages_Explained`) was missing from both
inventories, so the admin page never listed it at all.

### Comparison rules for publication state

No new `ContentSyncStatus` member and no wire-contract change: the rules reuse
`OutOfDateOnTarget` and the existing `VersionStatus` field. They're evaluated
before the timestamp rule, because unpublishing keeps `LastPublishedWhen`:

| Local | Remote | Status |
| --- | --- | --- |
| Unpublished page | absent | `MissingOnTarget` (Content Sync can create it) |
| Unpublished content-hub item | absent | left out — Content Sync makes no change for a new unpublished content item |
| Unpublished | Published | `OutOfDateOnTarget` |
| Published | Unpublished | `OutOfDateOnTarget` |
| Unpublished | Unpublished | the timestamp rule |
| absent | Unpublished | `ExtraOnTarget` (the existing rule) |

`"Archived"` counts as unpublished (see below), and a missing (`null`)
`VersionStatus` counts as published.

The admin page's status tag explains a publish-state difference in its
tooltip (for example, "Unpublished here, still published on the target"), so
`OutOfDateOnTarget` isn't ambiguous between newer edits and a different
publish state.

**Mixed versions.** A target running an older toolkit version doesn't send
unpublished items, so an item unpublished on that target is reported as it was
before: `MissingOnTarget`. That's no worse than before the change.

This changed `ContentSyncStatusComparer`'s classification but not any public
signature — a minor-version change under this repository's release policy.

### Verification results (live two-instance rig, 31.7.2)

- **Unpublishing keeps the publish timestamp.** Unpublishing a page changes
  its `ContentItemCommonDataVersionStatus` to `Unpublished` (3) on the same
  row and leaves `ContentItemCommonDataLastPublishedWhen` unchanged. Timestamps
  alone can never detect an unpublish, so the state rules above are required.
  The both-unpublished case can still use the timestamp rule.
- **The query works.** A `Where` on `ContentItemCommonDataVersionStatus =
  Unpublished` inside `ContentItemQueryBuilder` with `ForPreview = true`
  returns exactly the unpublished items. With the default
  `ForPreview = false`, it returns nothing.
- **Before the change, the gap reproduced:** an unpublished source page that
  the target still had published showed as `ExtraOnTarget`.
- **After the change:**
  - unpublishing `/Articles/Donate_with_us` on the source (published on the
    target) shows it as `OutOfDateOnTarget`, with the tooltip "Unpublished
    here, still published on the target", and its row still links to the page;
  - unpublishing a content-hub item that's missing on the target removes it
    from the list;
  - unpublishing `/Articles/Origins_of_Arabica_Bourbon` on the **target**
    (published on the source) shows it on the source as `OutOfDateOnTarget`,
    with the tooltip "Published here, unpublished on the target", after
    Refresh — previously it would have been `MissingOnTarget`;
  - the secured page `/Articles/Coffee_Beverages_Explained` appears in both
    inventories and shows as `InSync`.

  All three items were published again afterwards.
- **`VersionStatus` naming differs by version.** Values are `InitialDraft=0`,
  `Draft=1`, `Published=2`, `Unpublished=3` on both 30.8.0 and 31.x, but
  30.8.0 also declares `Archived = 3`. For a duplicated value, .NET doesn't
  guarantee which name `ToString()` returns, so an older instance may report
  an unpublished item as `"Archived"`. The comparer treats `"Archived"` and
  `"Unpublished"` as the same state.
- **Draft (Initial) doesn't always mean never published.** Creating a new
  version of an *unpublished* page moves that row to `InitialDraft` (0) while
  keeping its original `LastPublishedWhen`. Such an item matches neither query
  above; `LastPublishedWhen` is what distinguishes "never published" from
  "unpublished, then re-drafted". Whether Content Sync can act on such an item
  is undocumented, so it's left out.
- **Content Sync of an unpublished page absent on the target creates it
  there, unpublished.** Verified with Xperience's own Content Sync between the
  two rig instances (target on HTTPS with a trusted development certificate;
  see [Contributing-Setup](../Contributing-Setup.md#connecting-xperiences-own-content-sync)).
  `/Articles/Clone_On_Roasts`, unpublished on the source and absent on the
  target, showed as `MissingOnTarget` ("Unpublished here, and not on the
  target yet"); **Sync this page** created it on the target as `Unpublished`
  within about 10 seconds, after which it showed as `InSync` ("Unpublished on
  both instances"). Publishing it on the source then showed `OutOfDateOnTarget`
  ("Published here, unpublished on the target"), and syncing again made it
  published on the target and `InSync`.

## Administration integration

This foundation has no administration UI of its own. It is consumed by the two
editor-facing features, each registered through the same public
`AddContentSyncToolkit()` startup extension this specification defines.
Register via `services.AddContentSyncToolkit()`. Source and target come from
Content Sync's configuration (see Configuration); the only options tune the
source's requests:

```csharp
services.AddContentSyncToolkit(options =>
{
    options.RequestTimeout = TimeSpan.FromSeconds(30);
    options.InventoryCacheDuration = TimeSpan.FromSeconds(90);
});
```

Everything the extension registers — the controller's application part, the
secret validator, and the typed HTTP client — is registered unconditionally,
regardless of whether the current instance is configured as a source, a
target, both, or neither. Runtime behavior is gated entirely by
`ContentSyncTargetSecretValidator` on the target side and by
`ContentInventoryClient` failing fast with a clear error when no target URL is
resolved on the source side.

## Server workflow and consistency

For each source-side status request:

1. Query local inventory for the requested scope (channel or workspace),
   always fresh — no caching of the local half.
2. Check the remote-fetch cache (`{kind}|{scopeName}|{languageName}`, default
   90-second TTL from `Source.InventoryCacheDuration`); on a miss, call the
   target's endpoint. Each status method also has an overload taking
   `forceRefresh`: when `true` it skips the cache lookup, fetches fresh, and
   stores the fresh result for later calls. The admin page's **Refresh** uses
   it; it doesn't change the TTL.
3. On any fetch failure, return `ContentSyncStatusResult(TargetAvailable: false, Items: [])`
   without running the comparer.
4. On success, run `ContentSyncStatusComparer` over the local and (cached or
   fresh) remote inventory and return the classified result.

For each target-side inventory request: validate the shared secret
(constant-time comparison), then query local inventory for the requested scope
and return it. No caching on the target side — a target only ever answers about
its own local state, which is a single cheap database read per request.

## Security and privacy

- The target endpoint is service-to-service, not an authenticated Xperience
  administration user — it does not use the
  `[Authorize(AuthenticationSchemes = ...)]` admin-identity pattern used
  elsewhere in the Xperience ecosystem. Authentication is a shared secret
  transmitted over HTTPS, matching Content Sync's own connection model.
- Secret comparison must use `CryptographicOperations.FixedTimeEquals`, not a
  standard string comparison, to avoid a timing side channel.
- Content Sync's `Target.Enabled == false`, a missing configured secret, and a wrong or
  missing provided secret must all produce the identical response (`404`, no
  distinguishing body) — a caller must not be able to distinguish "this
  instance isn't a configured target" from "the secret is wrong" from "the
  route doesn't exist." The rejection is therefore a `404` with **no body**, not
  `NotFoundResult`: `[ApiController]` gives a client error result a
  ProblemDetails body even when an authorization filter short-circuits, and an
  unknown URL never has one. Without a body, the host's own 404 handling (for
  example `UseStatusCodePagesWithReExecute`) renders it like any unknown URL.
  Verified live on 31.7.2: a rejection and an unknown URL return the same
  Dancing Goat 404 page, differing only in per-request form IDs, which also
  differ between two requests to the same unknown URL.
- The response body must contain only the fields defined in the data contract:
  GUID, kind, content type name, scope name, language, tree path, publish
  timestamp, version status. No field values, no user information, no other
  content metadata.
- Treat the requested `channelName`/`workspaceName` and `languageName` as
  untrusted input; an unknown scope or language name returns an empty
  inventory, not an error that could confirm or deny the existence of a
  channel/workspace/language by its message content. Verified live on 31.7.2
  for an unknown channel, workspace, and language on both actions.

## Compatibility and API gate

Verified directly against the shipped XML documentation for
`kentico.xperience.core` at both the minimum supported version (`30.8.0`) and
the latest build-verified version (`31.7.2`) — present and identically shaped
at both:

- `CMS.Websites.ContentTypesQueryParametersExtensions.ForWebsite(ContentTypesQueryParameters, string websiteChannelName, PathMatch, bool)`
  — explicit-channel-name scoping, not the ambient `IWebsiteChannelContext`
  that Dancing Goat's own examples rely on;
- `CMS.ContentEngine.ContentItemQueryBuilder.InWorkspaces(string[])` — explicit
  workspace-name scoping;
- `CMS.Websites.ContentQueryExecutorExtensions.GetWebPageResult<T>(IContentQueryExecutor, ContentItemQueryBuilder, Func<IWebPageContentQueryDataContainer, T>, ContentQueryExecutionOptions, CancellationToken)`
  — raw per-row mapping with no dependency on `RegisterContentTypeMappingAttribute`,
  so the foundation works across whatever content types a consuming
  application defines, not just ones it registers a model for;
- `CMS.Websites.IWebPageContentQueryDataContainer : CMS.ContentEngine.IContentQueryDataContainer`
  (confirmed via assembly reflection, not just doc inference) — a web page row
  carries both `WebPageItem*` fields and the inherited `ContentItem*` fields
  (`ContentItemGUID`, `ContentTypeName`, `ContentItemCommonDataLastPublishedWhen`,
  `ContentItemCommonDataVersionStatus`) in one object;
- `CMS.Websites.PathMatch.Children(string path, int nestingLevel = -1)` — the
  default `-1` means "all levels," confirmed via reflection;
- `CMS.ContentEngine.ContentQueryParametersBase<T>.Offset(int offset, int fetch)`
  paired with `.OrderBy(...)` — the documented paging mechanism; `TopN` cannot
  be combined with `Offset` in the same query scope, so the internal paging
  loop uses `Offset` exclusively.

Production code must not reference namespaces or types marked `.Internal` or
use reflection to access non-public members at runtime (the reflection above
was a one-time API-surface verification during specification, not a runtime
dependency). If any required behavior cannot be implemented using supported
APIs, revise this specification's scope explicitly before implementation
continues.

## Error behavior

- Unknown channel, workspace, or language name (local or remote): empty
  inventory result, not an error. On the source this means a channel or
  language the target doesn't have yet shows every item as `MissingOnTarget`,
  not "target unavailable."
- Remote target unreachable, TLS/connection failure, or timeout:
  `ContentInventoryFetchStatus.Unreachable`; surfaced by the orchestrating
  service as `TargetAvailable = false`.
- Remote target reachable but rejects the request (missing/wrong secret,
  disabled): `ContentInventoryFetchStatus.Rejected`; surfaced the same way as
  `Unreachable` — a caller of `IContentSyncStatusService` does not need to
  distinguish "misconfigured" from "unreachable" to render a safe "target
  status unavailable" state.
- Unexpected error deserializing or processing a response:
  `ContentInventoryFetchStatus.Error`; also surfaced as `TargetAvailable = false`.
- Local query failure (for example, a database error): let the exception
  propagate; this is an operational failure of the instance itself, not a
  sync-status condition to model.

## Suggested component boundaries

- `ContentSyncToolkitOptions`: the toolkit's own settings (request timeout,
  inventory cache duration).
- `IContentSyncToolkitSettings` / `ContentSyncToolkitSettings`: the source and
  target settings, read from Xperience's Content Sync configuration (see
  Configuration).
- `ILocalContentInventoryService` / `LocalContentInventoryService`: local
  content querying, present on every installation regardless of role.
- `IContentScopeLookup` / `ContentScopeLookup`: checks a requested website
  channel and language exist before querying, so the guard is testable
  without a database.
- `ContentInventoryController`: target-side wire endpoint.
- `ContentSyncTargetSecretValidator`: isolated, independently testable secret
  validation, shared by the controller's authorization filter.
- `IContentInventoryClient` / `ContentInventoryClient`: source-side HTTP call
  to a configured target.
- `IContentInventoryCache` / `ContentInventoryCache`: short-TTL cache scoped to
  the remote fetch only.
- `ContentSyncStatusComparer`: pure diff logic, no dependencies.
- `IContentSyncStatusService` / `ContentSyncStatusService`: orchestrates the
  above; the only component the two editor-facing features depend on directly.

Keep Xperience-specific query construction inside `LocalContentInventoryService`
so the comparer, cache, and orchestrator stay testable without a live database,
matching this repository's established testing convention (see
[Contributing-Setup](../Contributing-Setup.md)).

## Test strategy

### Unit tests

Full coverage, hand-rolled fakes, no mocking framework (matching this
repository's established convention):

- secret validator: disabled-regardless-of-secret, empty configured secret,
  missing/null/wrong/differing-length provided secret, exact match with
  `Enabled = true`, case sensitivity;
- controller authorization: missing secret, wrong secret, and disabled target
  all short-circuit before the local inventory service is touched, for both
  actions — covered by the filter's own tests (bodiless 404, identical for
  every rejection) plus a test that the filter is applied at controller level
  with no action opting out;
- local inventory guard: an unknown channel or language returns an empty
  inventory without running a query;
- comparer: both empty; local-only (`MissingOnTarget`); remote-only
  (`ExtraOnTarget`); equal timestamps; newer local; newer remote; both null;
  local-null/remote-present; local-present/remote-null; duplicate GUIDs within
  one side (defined behavior, not an unhandled exception); a mixed batch;
  pass-through of `ContentTypeName`/`ScopeName`/`TreePath`/`LanguageName`;
- cache: hit within TTL returns without invoking the fetch; expiry via a fake
  `TimeProvider`; distinct keys (kind, scope name) don't collide; zero/negative
  TTL always misses;
- orchestrating service: local is re-queried every call even on a remote
  cache-hit; remote fetch is skipped within TTL; cache keys don't
  cross-contaminate web-page and content-hub scopes; an unreachable/rejected
  target produces `TargetAvailable = false` and is never silently diffed as an
  empty inventory;
- client: correct route and query string, secret header attached, successful
  envelope deserialization, non-2xx status maps to `Rejected` rather than
  throwing, an unreadable 2xx body (HTML, truncated JSON, JSON `null`) maps to
  `Error`, simulated network failure maps to `Unreachable`;
- DI registration: resolves every registered interface without error; calling
  `AddContentSyncToolkit` twice remains idempotent.

### Integration and runtime tests

Run against both the minimum supported and latest verified Xperience versions,
using the Dancing Goat integration host:

- registering the toolkit as a target, calling the endpoint with a correct
  secret, and verifying the returned inventory matches Dancing Goat's actual
  published pages/content-hub items for a real channel and workspace;
- calling the endpoint with a missing or wrong secret, and with
  Content Sync's `Target.Enabled = false`, verifying an identical `404` response in all three
  cases;
- configuring one Dancing Goat instance as source and a second as target,
  publishing/unpublishing content on the source, and verifying the diffed
  result updates accordingly within the configured cache TTL.

## Acceptance criteria

- `AddContentSyncToolkit()` registers a working target endpoint and source
  client from a single call, following this repository's established DI
  registration pattern.
- A target instance with Content Sync's `Target.Enabled = true` and a configured secret
  answers inventory requests for a given channel or workspace with accurate
  local content data.
- A target instance with Content Sync's `Target.Enabled = false`, or any caller presenting a
  missing or incorrect secret, receives an identical rejection response.
- A source instance correctly classifies local content against a live target's
  inventory into in-sync, missing-on-target, out-of-date-on-target, and
  extra-on-target categories.
- A target that is unreachable or rejects the request never causes local
  content to be misreported as missing.
- The implementation passes compatibility validation on `30.8.0` and `31.7.2`
  without depending on internal Xperience APIs.

## Out of scope

- Any administration UI — covered by
  [sync-status-admin-page](sync-status-admin-page.md) and
  [content-tree-sync-indicators](content-tree-sync-indicators.md).
- Content fingerprints (content-hash-based staleness detection); publish
  timestamps, publication state, tree path, and sibling order only. Deferred,
  not rejected: Kentico provides no content hash, so the toolkit would compute
  one per item on each side (sending only the hash, never field values), with
  the open questions of query cost and of values that legitimately differ
  between instances (IDs, asset URLs). Lower priority because Kentico says to
  edit only on the source.
- Detecting deletions as such. Content Sync can't delete on the target
  (Kentico's documentation: "Content synchronization cannot be used to delete
  items on the target instance"), so an item deleted on the source is simply
  `ExtraOnTarget`; consumers explain that it has to be deleted on the target by
  hand.
- Wire-level pagination or continuation tokens; internal looping produces one
  complete in-memory response.
- Multi-target support; one source diffs against exactly one configured target
  URL.
- Secret rotation tooling beyond a configuration change and restart.
- A "discover all channels/workspaces" convenience method; callers always pass
  an explicit scope name.
- Triggering an actual Content Sync operation from the diff result — this
  foundation is read-only visibility. Initiating a sync remains Xperience's
  own **Sync this page**/**Sync with all subpages**/Content hub **Sync**
  actions. Checked against the `30.8.0` API: the services that create a
  synchronization (`ISynchronizationManager`, `ISynchronizationService`,
  `ISynchronizationCommandManager`) are all in `.Internal` namespaces, which
  Compatibility and API gate rules out, so this isn't possible with public APIs.

## References

- [Content sync](https://docs.kentico.com/documentation/business-users/content-sync)
- [Content sync configuration](https://docs.kentico.com/documentation/developers-and-admins/configuration/content-sync-configuration)
- [Architecture overview](../Architecture.md)
- [.NET API compatibility rules](https://learn.microsoft.com/en-us/dotnet/core/compatibility/library-change-rules)
