# Feature specification: Connection problems in the Event log

Status: **implemented** (November 2026), for 1.0.0-beta.6. Reviewed before building; where the build
differs, this spec says how.

## Context

When the source can't get a list from the target, the status pages say "The target instance couldn't be
reached or rejected the request, so sync status can't be determined." That's all an editor needs, but an
administrator fixing it needs the reason: a wrong secret, a target that's down, a timeout, a target
without the toolkit. Today nothing records it. The toolkit already writes to Kentico's Event log for syncs
(started, completed, failed) and for unexpected errors, so administrators already look there.

## What we have

- Every request from the source to the target goes through one method,
  `ContentInventoryClient.FetchAsync`, which sorts failures into four results:
  - **Unreachable**: no answer (connection refused, DNS, TLS) or no answer in time
    (`ContentSyncToolkit:RequestTimeout`, 30 s by default);
  - **Rejected**: any HTTP error status;
  - **Error**: a 200 whose body isn't the expected JSON;
  - **Success**.
- The target answers a failed secret check with a plain **404**, the same as an unknown address, so a
  caller can't learn the endpoint exists ([content-inventory-foundation.md](content-inventory-foundation.md)).
  The source therefore can't tell a wrong secret from a target without the toolkit, or with the target role
  off: only the target knows which it was. `IContentSyncTargetSecretValidator` deliberately collapses
  those cases into one `false`.
- Every response carries the target's wire `SchemaVersion` (5 today). The source doesn't check it.
- Endpoints added after the first release (required objects, the item ID lookups,
  `content-hub-items-by-guid`) answer 404 on an older target, and the source already treats that as "not
  checked" or "not answered" and carries on.
- The Event log is written with Kentico's `IEventLogService`, source `ContentSyncToolkit` for syncs.
- Kentico can limit how often an entry is written (`LoggingIntervalPolicy`, once or once per period), but
  only from 31.x, and it drops repeats silently. These entries need 30.8.0 too, a count of what was
  dropped, and a "recovered" entry, so the toolkit keeps that state itself.

## Design

Three entries on the source, one on the target. All use the event source `ContentSyncToolkit`. None is
written on a normal page load: an entry is written when something changes, not every time it's seen.

### On the source

**1. The target stops answering: a warning, once.** Written the first time a request to the target fails,
then not again while it keeps failing in the same way.

| Event code | When | Text (example) |
| --- | --- | --- |
| `TARGETUNREACHABLE` | No answer, or no answer within the timeout | "The Content Sync target `https://www.dancinggoat.com` didn't answer: connection refused. Check that it's running and that this instance can reach it. Content sync status can't be shown until it answers." The cause is the exception's message, or "no answer within 30 seconds (ContentSyncToolkit:RequestTimeout)". |
| `TARGETREJECTED` | An HTTP error status on a list request | For 404: "The Content Sync target `https://www.dancinggoat.com` answered 404 Not Found. Check that both instances use the same Content Sync secret, that Content Sync's target role is enabled there, and that the target has the Content Sync Toolkit installed. The target's Event log says which, if it's the secret or the target role." Other statuses name the status and suggest the target's own Event log. |
| `TARGETERROR` | A 200 that isn't the expected answer | "The Content Sync target `https://www.dancinggoat.com` answered, but not with the Content Sync Toolkit's data. Check that the target URL points to the Xperience application, not a proxy or another site." |

**2. The target answers again: information, once.** `TARGETRECOVERED`: "The Content Sync target
`https://www.dancinggoat.com` answers again." Written only after one of the warnings above, so a reader
sees when the problem started and ended.

**3. The target runs an older toolkit: information, once per finding.** `TARGETOLDER`, in two cases:

- A list answer carries a lower `SchemaVersion` than this instance's: "The Content Sync target
  `https://www.dancinggoat.com` runs an older version of the Content Sync Toolkit (data version 4; this
  instance uses 5). The lists still work, but some details need the same version on both instances.
  Update the toolkit on the target to the same version as this instance."
- An endpoint added after the first release answers 404 while the lists answer. The data version alone
  can't catch this: beta.4 and beta.5 both send version 5, but only beta.5 has the moved-items endpoint.
  Once per feature: "The Content Sync target `https://www.dancinggoat.com` doesn't support items moved to
  another workspace (it answered 404 Not Found), so it most likely runs an older version of the Content
  Sync Toolkit. The lists still work without it. Update the toolkit on the target to the same version as
  this instance." The features: items moved to another workspace, the check of content types, languages
  and channels before a sync, and links to items only on the target.

Rules:

- **State is kept in memory per target URL**, in a singleton. A restart starts fresh, so the first failure
  after a restart is logged again. That's intended: it confirms the problem survived the restart.
- **A different kind of failure, or a different error status, is a new entry.** Unreachable, then rejected, writes
  two warnings, and so does 404 then 500. Connection errors and timeouts count as one kind, since their text
  varies from one attempt to the next.
- **Only the two list requests count** (`web-pages`, `content-hub-items`). The other endpoints were added
  later, and an older target answers them with 404, which the source already handles ("not checked", no
  links, no hints). A 404 from one of them while the lists succeed is the older-target case, not a
  rejection, and is covered by `TARGETOLDER`.
- **Nothing secret is written:** no secret, no request headers, no query strings beyond the endpoint
  name. The target URL is the configured one, which administrators already know.

### On the target

**4. A request with the wrong secret, or while the target role is off: a warning, at most every 10
minutes.** `REQUESTREJECTED`, written by `ContentSyncTargetSecretFilter` when a request carries the
toolkit's secret header but fails the check:

| Reason | Text |
| --- | --- |
| The secret doesn't match | "A Content Sync Toolkit request from 203.0.113.7 was refused: its Content Sync secret doesn't match this instance's. Use the same secret on both instances." |
| Target role off | "A Content Sync Toolkit request from 203.0.113.7 was refused: Content Sync's target role isn't enabled on this instance (ContentSynchronization:Target:Enabled)." |
| No secret configured | "... refused: this instance has no Content Sync target secret configured." |

- **Requests without the header aren't logged.** Anything can request an unknown address (crawlers,
  scanners), and logging those would let anyone fill the Event log. Only callers that present
  themselves as a toolkit source are written down.
- **At most one entry per reason every 10 minutes**, so a misconfigured source refreshing its list, or
  someone sending wrong secrets, can't flood the log. The entry says how many requests were refused since
  the last one ("12 requests refused in the last 10 minutes").
- **The answer doesn't change:** still a bodiless 404, so the caller learns nothing new. The reason is
  only in the target's own Event log.
- The caller's address is the request's remote IP, as ASP.NET Core sees it (behind a proxy, the proxy's
  unless forwarded headers are configured). Kentico's Event log records the IP of every entry anyway.
- `IContentSyncTargetSecretValidator` stays as it is (public, one `bool`). The filter gets the reason
  from an internal method next to it.

### What isn't logged

- Page views, list loads, Refresh, successful requests.
- Anything on 30.8.0 that differs from 31.x: these entries work the same on both.
- Sync outcomes: already logged (`SYNCSTARTED`, `SYNCCOMPLETED`, `SYNCFAILED`).

## Also in this step: message rows can't be selected

The rows that stand in for the list when it can't be shown ("Target unavailable", "Sync status couldn't
be loaded", a channel, workspace or language that doesn't exist) can be ticked today, which enables
**Sync** above the list and opens Kentico's dialog with nothing in it. They become disabled rows
(`Row.Disabled`), and hovering one shows its whole message (`Row.InactiveMessage`), which the Name or
Path column cuts short. Built and checked on 31.9.1 during the beta.5 review; waiting in a stash.

## Compatibility

- Same on 30.8.0 and 31.x.
- No new setting, no new endpoint, no schema change.
- A beta.5 target keeps working with a beta.6 source and the other way round; only the entries differ.

## Test strategy

- Unit tests:
  - each failure kind writes its warning once, a repeat writes nothing, a different kind writes again,
    success after failure writes `TARGETRECOVERED`, success without a failure writes nothing;
  - a 404 from an optional endpoint while the main requests succeed writes no warning;
  - a lower `SchemaVersion` writes `TARGETOLDER` once;
  - the target filter: each reason, the 10-minute limit and the count, no entry without the header, the
    response still a bodiless 404;
  - no entry contains the secret.
- On the rig (31.9.1 and 30.8.0): stop the target, then start it again; change the secret on one side;
  turn the target role off; check both Event logs, and that each problem is logged once, not on every
  list load or Refresh.
- Message rows: on both versions, open a channel, workspace and language that don't exist and the
  target stopped; the row can't be ticked, and hovering shows the whole message.

### Checked

November 2026, driven with the Playwright CLI, entries read from `CMS_EventLog`:

- `31.9.1`, the repository's rig:
  - both instances healthy, the list loaded twice: no entries;
  - target stopped, three Refreshes: one `TARGETUNREACHABLE` ("No connection could be made because the
    target machine actively refused it. (localhost:27311)"); target started again: one `TARGETRECOVERED`;
  - source restarted with a different secret, three Refreshes: one `TARGETREJECTED` on the source, one
    `REQUESTREJECTED` on the target ("its Content Sync secret doesn't match this instance's");
  - target restarted with the target role off: one `REQUESTREJECTED` ("Content Sync's target role isn't
    enabled on this instance");
  - neither Event log contains either secret;
  - message rows for a workspace and a language that don't exist: the checkbox is disabled, and hovering
    shows the whole message.
- `30.8.0`, with the library packed as `1.0.0-eventlog1`: the message rows as above; target stopped, two
  Refreshes: one `TARGETUNREACHABLE`.
- Not run live: an older target (`TARGETOLDER`) and the 10-minute limit with its count; both are covered by
  unit tests.

## Acceptance criteria

- An administrator can tell from the source's Event log why the list says the target is unavailable, and
  from the target's which secret or role problem refused it.
- Repeated failures never produce more than one entry per kind, on either instance.
- No entry contains the secret or other request data.
- Message rows can't be selected for a sync, and show their whole text on hover.
