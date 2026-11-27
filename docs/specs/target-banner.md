# Feature specification: Show which instances the list compares

Status: **implemented** (November 2026). Reviewed before building; where the build differs, this spec
says how. In 1.0.0-beta.6 the caption became the route chip
([route-chip-and-feedback.md](route-chip-and-feedback.md)): same hosts and labels, drawn by the toolkit.

## Context

The status pages compare this instance with its Content Sync target, but don't say which instances those
are. On a multi-site setup, or when staging, UAT and production instances are close copies, an editor
can't tell from the page what "Only on target" or a Sync button refers to.

It matters more once the list can sync ([sync-actions.md](sync-actions.md)): before sending content, the
editor should see where it comes from and where it goes.

## What we have

- The target's address is Content Sync's `ContentSynchronization:Source:TargetUrl`, already read by the
  toolkit (`IContentSyncToolkitSettings.Source.TargetUrl`).
- The target's administration address is derived from it (target URL plus `/admin`), or set with the
  optional setting described in [Open items on the target](target-item-links.md).
- This instance's address is the one the editor is using: the host of the current request.
- Xperience has no instance name of its own.
- The toolkit already shows callouts when the target is unreachable or not configured.

## Design

A line at the top of both tabs, above the toolbar:

> **staging.example.com → production.example.com**

- **This instance:** the host of the current request, which is what the editor sees in the browser's
  address bar.
- **The target:** the host of Content Sync's target URL.
- Host names only, without the scheme or path, since that's what editors recognise.
- It's the listing's caption (`ListingConfiguration.Caption`), so it takes one line, not a callout box.
  The caption is plain text, so the line has no link to the target's administration: the only built-in
  subcaption component that could hold one (`LabelWithTooltipSubcaption`) is internal. Items only on the
  target still link there.
- Without a request to read the host from, it reads "Compared with *target*".
- Shown whenever the source is configured. When the target is unreachable, the existing callout still
  appears below it.
- The sync confirmation ([sync-actions.md](sync-actions.md)) names the same instances.

### Optional labels

Two settings give the instances names, for teams whose host names don't say which environment they are:

- `ContentSyncToolkit:InstanceLabel`, for example "Staging";
- `ContentSyncToolkit:TargetLabel`, for example "Production".

With labels: **Staging** (staging.example.com) → **Production** (production.example.com). Either label can
be set alone.

### Behind a proxy

Behind a reverse proxy or load balancer, the request's host can be an internal name. The host comes from
ASP.NET Core's request, so projects that configure forwarded headers (`UseForwardedHeaders`) get the
public name; otherwise `InstanceLabel` covers it.

## Decisions

Agreed in review (November 2026): show **both** instances, this one and the target, by host name, with
optional labels.

## Test strategy

- Unit tests: host and label formatting, with and without each label.
- On the rig (`30.8.0` and the latest version):
  - the line on both tabs, with the rig's two hosts (checked on `31.9.1`: "localhost:27300 →
    localhost:27311");
  - the line together with the unreachable-target callout;
  - both labels set (checked: "Staging (localhost:27300) → Production (localhost:27311)").

## Acceptance criteria

- Both tabs show this instance and the target, by host name or label.
- Nothing changes when the source isn't configured (the existing callout applies).
