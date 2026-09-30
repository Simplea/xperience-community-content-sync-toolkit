# Architecture: content inventory and sync status

## Context

Xperience by Kentico's built-in Content Sync feature (shipped in the May 2025
Refresh) lets editors push pages and content-hub items from a source instance to
a target instance, but it gives no visibility into what's missing on the target
before or after a sync. Confirmed via documentation research:

- There is no per-item sync status shown in the content tree or content hub.
- The only monitoring UI — the **Content synchronization** application — exists
  only on the *target* instance and shows sync history (completed/scheduled
  tasks), not sync gaps.
- No documented REST or Management API lets a third party query whether a given
  content item exists on a target instance.

This is the gap Content Sync Toolkit exists to fill. Both of the toolkit's
planned editor-facing features need the same missing capability — knowing what
exists on the target and diffing it against the source:

- an admin page for exploring the page/content-hub tree and seeing what's
  missing on the target (see [sync-status-admin-page](specs/sync-status-admin-page.md));
- inline "missing on target" indicators in the existing content tree and
  content hub UIs (see [content-tree-sync-indicators](specs/content-tree-sync-indicators.md)).

This document describes the shared foundation both features are built on. The
foundation itself is specified in detail in
[content-inventory-foundation](specs/content-inventory-foundation.md); this
document is the architectural overview — how the pieces fit together and why.

## Research: why the toolkit runs on both instances

Kentico's own Content Sync connection is a one-way push: the source is
configured with the target's URL and a shared secret, and pushes sync tasks to
it over HTTPS. There is no read channel back. We considered and rejected:

- **Direct database read against the target.** Couples the toolkit to Kentico's
  internal schema across versions and requires DB network access/credentials
  that most organizations won't grant across environment boundaries (source and
  target are frequently on separate networks, e.g. staging vs. production).
- **Reusing Kentico's `ContentSynchronizationOptions`.** That type belongs to
  Kentico's own feature and isn't documented as public, stable API for
  third-party binding.
- **`Kentico.Xperience.ManagementApi`.** A real, currently-preview NuGet
  package — but it only covers content-*type*/schema management
  (`ContentTypeController`, `ReusableSchemaController`, etc.), entirely under an
  `Internal` namespace. Not applicable to content *item* instances.

The chosen design: the toolkit NuGet package is installed on **both** instances.
On the target, it exposes a small authenticated, read-only HTTP endpoint
returning a content inventory (item GUIDs, types, publish timestamps). On the
source, it calls that endpoint and diffs the result against local content. This
mirrors Kentico's own connection model (HTTPS + shared secret) without taking a
dependency on Kentico's internal option types.

## Components

```text
Source instance                                    Target instance
────────────────────                                ────────────────────
ILocalContentInventoryService  ── (local query) ──►  ILocalContentInventoryService
        │                                                    │
        ▼                                                    ▼
IContentSyncStatusService                            ContentInventoryController
  ├─ IContentInventoryClient ──── HTTPS + secret ────►  (guarded by
  │    (calls target's endpoint)                         ContentSyncTargetSecretValidator)
  ├─ IContentInventoryCache
  └─ ContentSyncStatusComparer (pure diff logic)
        │
        ▼
  ContentSyncStatusResult
   (consumed by both editor-facing features)
```

- **`ILocalContentInventoryService`** — runs on *every* installation (source,
  target, or both). Enumerates local web pages (scoped by website channel) and
  content-hub items (scoped by workspace) via `IContentQueryExecutor`.
- **`ContentInventoryController`** — the target-side endpoint. Present in every
  installation but only answers requests when `Target.Enabled` is `true` and the
  caller presents the correct shared secret; otherwise every rejection reason
  (disabled, wrong secret, missing secret) looks identical (404) from the
  outside.
- **`IContentInventoryClient`** — the source-side HTTP client that calls a
  configured target's endpoint.
- **`ContentSyncStatusComparer`** — pure, static diff logic: matches local vs.
  remote inventory by GUID and classifies each item as in sync, missing on
  target, out of date on target, or extra on target (present on target but not
  locally).
- **`IContentSyncStatusService`** — the orchestrator both editor-facing features
  call. Ties together the local query, the remote client (with a short cache on
  just the remote fetch, not the whole diff), and the comparer.

Full data contract, security model, and test plan for these pieces:
[content-inventory-foundation](specs/content-inventory-foundation.md).

## Design decisions

**Publish-timestamp comparison, not content hashing.** Content Sync itself only
exposes publish timestamps (`ContentItemCommonDataLastPublishedWhen`), not a
content hash. The comparer's "out of date" classification is therefore
timestamp-based and knowingly sensitive to clock skew between the two servers —
documented as an accepted limitation, not solved in the initial version.

**Cache the remote fetch only, not the whole diff.** Local queries are cheap
database reads and should stay maximally fresh — an editor who just published
locally should see that reflected immediately. The expensive, rate-limited part
is the cross-instance HTTP call, so only that gets a TTL (default 90 seconds).
This lets a tree UI that re-checks status per node, per render, cost at most one
remote call per scope rather than one per node.

**Register everything unconditionally; gate behavior at request time.** The
target controller, secret validator, and HTTP client are all registered by
`AddContentSyncToolkit()` regardless of whether the current instance is
configured as a source, a target, both, or neither. Conditional registration at
startup can't react to configuration that changes without an app restart and
would duplicate the runtime gate that's already mandatory for the "disabled
looks identical to wrong secret" security requirement.

**No wire-level pagination.** `ILocalContentInventoryService` pages internally
(`Offset` with `OrderBy`, looped until a page returns fewer rows than requested) and
returns one complete in-memory list. Large sites are an accepted v1 limitation;
revisit if response sizes become a real problem.

**Minimum supported version tracks Content Sync's own feature completeness,
not just API availability.** Content Sync shipped in two phases: content-hub
items in the May 2025 Refresh (`30.5.0`), website channel pages in the July
2025 Refresh (`30.8.0`). Since this toolkit covers both from the start, `30.8.0`
is the floor — an earlier version's Content Sync can't sync pages at all, so
supporting it would mean claiming compatibility with a feature this toolkit
has nothing to show for half its scope. All query APIs the foundation depends
on were verified present at `30.8.0` as well as the latest build-verified
version (`31.7.2`), so this floor doesn't trade away any needed capability.
See [content-inventory-foundation](specs/content-inventory-foundation.md#compatibility-and-api-gate)
for the specific verified API surface.

## Non-goals (initial version)

- No content-hash-based staleness detection.
- No multi-target support — one source diffs against exactly one configured
  target URL.
- No secret rotation tooling beyond a config change and restart.
- No "discover all channels/workspaces" convenience method — callers always
  pass an explicit scope name.

## References

- [Content sync](https://docs.kentico.com/documentation/business-users/content-sync) — editor-facing behavior.
- [Content sync configuration](https://docs.kentico.com/documentation/developers-and-admins/configuration/content-sync-configuration) — `ContentSynchronizationOptions`, the connection model this design mirrors.
- [content-inventory-foundation spec](specs/content-inventory-foundation.md)
- [sync-status-admin-page spec](specs/sync-status-admin-page.md)
- [content-tree-sync-indicators spec](specs/content-tree-sync-indicators.md)
