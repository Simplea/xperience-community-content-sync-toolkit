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

### Options

```csharp
public sealed class ContentSyncToolkitOptions
{
    public ContentSyncToolkitSourceOptions Source { get; set; } = new();
    public ContentSyncToolkitTargetOptions Target { get; set; } = new();
}

public sealed class ContentSyncToolkitSourceOptions
{
    public Uri? TargetUrl { get; set; }
    public string? Secret { get; set; }
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan InventoryCacheDuration { get; set; } = TimeSpan.FromSeconds(90);
}

public sealed class ContentSyncToolkitTargetOptions
{
    public bool Enabled { get; set; } = false;
    public string? Secret { get; set; }
}
```

An instance's role is inferred, not declared twice: `Target.Enabled` is the
explicit switch for accepting inbound inventory requests. Being a source is
inferred from `Source.TargetUrl` being non-null. An instance may be both (for
example, a staging environment that is a sync target of production and a sync
source toward a further downstream environment) without two conflicting flags.

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
    DateTime? LastPublishedWhen,  // UTC
    string? VersionStatus);
```

`VersionStatus` is a plain string, not Kentico's `VersionStatus` enum, so the
wire contract does not couple to Kentico's internal type layout across
potentially different versions running on the source and target.

### Target endpoint

- Route: `xperience-community/content-sync-toolkit/inventory`.
- `GET .../web-pages?channelName={name}&languageName={name}` and
  `GET .../content-hub-items?workspaceName={name}&languageName={name}`.
- Required header: `X-ContentSyncToolkit-Secret`.
- Response body: `{ SchemaVersion: 1, GeneratedAtUtc, Items: ContentInventoryItem[] }`.
  `SchemaVersion` is a forward-compatibility hook for a source and target
  running different toolkit versions.
- Rejection (missing/wrong secret, or `Target.Enabled == false`): `404`, with no
  distinguishing body — see Security and privacy.

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
request's channel." Both page internally (`TopN`/`Offset`, looped until a page
returns fewer rows than requested) and return one complete in-memory list — see
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

This is a timestamp comparison across two independently running servers and is
therefore sensitive to clock skew between instances. No content hash is
available to compare instead, since Content Sync itself only exposes publish
timestamps. This is an accepted, documented limitation, not solved by this
specification.

A failed remote fetch (target unreachable, rejected, or erroring) must never be
treated as "target has zero items" — that would make every local item falsely
report as `MissingOnTarget`. `IContentSyncStatusService` surfaces fetch failure
as `TargetAvailable = false` and does not run the comparer in that case.

## Administration integration

This foundation has no administration UI of its own. It is consumed by the two
editor-facing features, each registered through the same public
`AddContentSyncToolkit()` startup extension this specification defines.
Register via:

```csharp
services.AddContentSyncToolkit(options =>
{
    options.Target.Enabled = true;
    options.Target.Secret = "...";
    options.Source.TargetUrl = new Uri("https://target-instance.example.com");
    options.Source.Secret = "...";
});
```

Everything the extension registers — the controller's application part, the
secret validator, and the typed HTTP client — is registered unconditionally,
regardless of whether the current instance is configured as a source, a
target, both, or neither. Runtime behavior is gated entirely by
`ContentSyncTargetSecretValidator` on the target side and by
`ContentInventoryClient` failing fast with a clear error when
`Source.TargetUrl` is unset on the source side.

## Server workflow and consistency

For each source-side status request:

1. Query local inventory for the requested scope (channel or workspace),
   always fresh — no caching of the local half.
2. Check the remote-fetch cache (`{kind}|{scopeName}|{languageName}`, default
   90-second TTL from `Source.InventoryCacheDuration`); on a miss, call the
   target's endpoint.
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
- `Target.Enabled == false`, a missing configured secret, and a wrong or
  missing provided secret must all produce the identical response (`404`, no
  distinguishing body) — a caller must not be able to distinguish "this
  instance isn't a configured target" from "the secret is wrong" from "the
  route doesn't exist."
- The response body must contain only the fields defined in the data contract:
  GUID, kind, content type name, scope name, language, tree path, publish
  timestamp, version status. No field values, no user information, no other
  content metadata.
- Treat the requested `channelName`/`workspaceName` as untrusted input; an
  unknown scope name returns an empty inventory, not an error that could
  confirm or deny the existence of a channel/workspace by timing or message
  content.

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

- Unknown channel or workspace name (local or remote): empty inventory result,
  not an error.
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

- `ContentSyncToolkitOptions` / `ContentSyncToolkitSourceOptions` /
  `ContentSyncToolkitTargetOptions`: configuration surface.
- `ILocalContentInventoryService` / `LocalContentInventoryService`: local
  content querying, present on every installation regardless of role.
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
  all short-circuit before the local inventory service is touched (assert via
  a stub that throws if invoked), for both actions;
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
  envelope deserialization, non-2xx status maps to `Rejected`/`Error` rather
  than throwing, simulated network failure maps to `Unreachable`;
- DI registration: resolves every registered interface without error; calling
  `AddContentSyncToolkit` twice remains idempotent.

### Integration and runtime tests

Run against both the minimum supported and latest verified Xperience versions,
using the Dancing Goat integration host:

- registering the toolkit as a target, calling the endpoint with a correct
  secret, and verifying the returned inventory matches Dancing Goat's actual
  published pages/content-hub items for a real channel and workspace;
- calling the endpoint with a missing or wrong secret, and with
  `Target.Enabled = false`, verifying an identical `404` response in all three
  cases;
- configuring one Dancing Goat instance as source and a second as target,
  publishing/unpublishing content on the source, and verifying the diffed
  result updates accordingly within the configured cache TTL.

## Acceptance criteria

- `AddContentSyncToolkit()` registers a working target endpoint and source
  client from a single call, following this repository's established DI
  registration pattern.
- A target instance with `Target.Enabled = true` and a configured secret
  answers inventory requests for a given channel or workspace with accurate
  local content data.
- A target instance with `Target.Enabled = false`, or any caller presenting a
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
- Content-hash-based staleness detection; publish-timestamp comparison only.
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
  actions.

## References

- [Content sync](https://docs.kentico.com/documentation/business-users/content-sync)
- [Content sync configuration](https://docs.kentico.com/documentation/developers-and-admins/configuration/content-sync-configuration)
- [Architecture overview](../Architecture.md)
- [.NET API compatibility rules](https://learn.microsoft.com/en-us/dotnet/core/compatibility/library-change-rules)
