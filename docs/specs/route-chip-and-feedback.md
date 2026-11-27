# Feature specification: The route chip and feedback links

Status: **implemented** (November 2026), for 1.0.0-beta.6. Reviewed before building; where the build
differs, this spec says how.

## Context

Two changes to the line above the status lists.

1. **The instances line.** It shows which instances the list compares
   ([target-banner.md](target-banner.md)), but it's Kentico's listing caption: bold plain text that reads
   like a heading. It doesn't look like part of the admin's controls, repeats the host after each label,
   and doesn't say whether the target answered. That only shows in a callout under the toolbar.
2. **Feedback.** There's no easy way for users to tell us how the toolkit works for them. We want
   feedback in GitHub, without building or hosting anything. The links should show only when they're
   relevant and never get in the way.

The design was chosen in review from three options (see [Decisions](#decisions)): a route chip with
labels when set and hosts on hover, with a quiet Feedback link at the end of the same row.

## What we have

- Both tabs already render a toolkit component in the listing's subcaption slot
  (`PageConfiguration.SubcaptionComponent`, `ContentSyncStatusNavigationComponent`). It renders nothing
  today and handles navigation. It's the toolkit's browser module (added in beta.5), so the chip needs
  no change to Kentico.
- The line's text comes from `ContentSyncRules.InstancesCaption`: this instance's request host and
  `ContentSyncToolkit:InstanceLabel`, then Content Sync's target URL and `ContentSyncToolkit:TargetLabel`.
- `IContentSyncTargetHealth` (beta.6, step 1) knows, per target, whether the last list request failed and
  how: unreachable, rejected or error.
- `ContentSyncTracker` keeps the syncs started from the lists for 24 hours, with who started them and
  how they finished.
- The repository has Discussions turned off and Markdown issue templates.

## Design

### 1. The route chip

A white rounded chip in the style of the top bar's dropdowns, replacing the caption:

> ( **Staging** → ● **Production** ) &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; Feedback

**What each side shows**

| Settings | Chip |
| --- | --- |
| Both labels | **Staging** → ● **Production**; hovering a side shows its host |
| No labels | staging.dancinggoat.com → ● www.dancinggoat.com |
| Only `TargetLabel` | staging.dancinggoat.com → ● **Production** (its host on hover) |
| Only `InstanceLabel` | **Staging** (its host on hover) → ● www.dancinggoat.com |

- Hosts are as today: the request's host for this instance, the target URL's host for the target, and
  the port only when it isn't the standard one.
- A host longer than 40 characters is shortened in the middle ("content-staging.…dancinggoat.com"), with
  the full name on hover.
- Without a request to read the host from (rare), this instance's side reads "This instance".
- The hover text uses Kentico's own tooltip component (`Tooltip`, exported from 30.8.0 on).
- **On Xperience 30.x**, the list is placed below the header by the caption's height alone, so without a
  caption it covers the chip. There the page keeps an invisible caption (a no-break space) to hold the
  line. 31.x lays the header out itself and gets no caption.

**The dot on the target side**

| Dot | When | Hover text |
| --- | --- | --- |
| Grey | Before the list has loaded | "Checking Production…" |
| Green | The last list request got an answer | "Production answered" |
| Red | The last list request failed | "Production didn't answer", or "Production refused the request", or "Production answered with something other than the Content Sync Toolkit's data", plus "Details are in the Event log." |

- **No extra request.** The dot shows the result of the list's own request. After the list loads, the
  chip asks the page (a page command, `GetRouteState`) what `IContentSyncTargetHealth` recorded for the
  target. That's in memory; nothing goes to the target.
- It asks again when the list changes: Kentico has no listing event for a subcaption component, so the
  chip watches the page for rows added or removed, or cells changed, inside the list's table (a
  `MutationObserver`), and asks 300 ms after the last change. That covers the first load, Refresh,
  filters, paging and a row's status after a sync.
- The red-dot callout under the toolbar stays. The dot says *that* the target failed; the callout says
  what it means for the list.

**Where it applies:** both tabs, on 30.8.0 and 31.x, whenever the source is configured (as the caption
today). The sync confirmation keeps naming the instances in text, as today.

### 2. The Feedback link

A quiet link at the right end of the chip's row: a speech-bubble icon and "Feedback", in the admin's
link colour, no box or button. It opens a new tab on GitHub:

`https://github.com/Simplea/xperience-community-content-sync-toolkit/discussions/new?category=feedback`

with the versions prefilled (see below). Always there while the setting is on; it never moves, blinks or
counts anything.

### 3. The one-time line after a sync

The first time an editor's sync from the lists completes, on each version of the toolkit, one line under
the chip:

> ✓ Synced. How is the Content Sync Toolkit working for you? **Tell us on GitHub** &nbsp;&nbsp; ✕

- **When:** the chip's `GetRouteState` answer also says whether this user has a sync from the lists that
  completed in the last 24 hours (`ContentSyncTracker`). A failed sync doesn't count.
- **Right when it finishes:** closing Kentico's Sync dialog doesn't change the list, so the answer also
  says whether this user has a sync running, and while one does, the chip asks again every 5 seconds.
  The line shows as soon as the sync completes, with the dialog still open or closed.
- **Once:** it's remembered in the browser's `localStorage`, per toolkit version and user
  (`cstk-feedback-<version>-<userId>`). The line stays until the editor clicks the link or ✕; after that,
  it doesn't show again until the next version. Clearing the browser's storage can show it once more,
  which is acceptable.
- **Where it goes:** the release's own feedback discussion (below). If the package has no release
  discussion URL, it goes to the Feedback form, as the link does.
- It never shows on a page load without a recent sync, so editors who only read the lists never see it.

### 4. The setting

`ContentSyncToolkit:FeedbackLinks` (`bool`, default `true`). `false` hides the Feedback link and the
one-time line. The chip stays. For agencies who hand the admin to clients, or teams who don't want
outbound links.

### 5. On GitHub

Two things, both on the repository's Discussions. Discussions were turned on in November 2026, with a
**Feedback** category (slug `feedback`, open-ended discussion format); GitHub's API can't create
categories, so it was created on the website.

**B. A "Feedback" category with a form.** `.github/DISCUSSION_TEMPLATE/feedback.yml`, the discussion
equivalent of an issue form, with these fields:

| Field | Type | Required |
| --- | --- | --- |
| How is the toolkit working for you? | Dropdown: Works well / Works, with some problems / Doesn't work for us | No (see below) |
| What works, or what's missing? | Text area | Yes |
| Content Sync Toolkit version | Text, prefilled | No |
| Xperience by Kentico version | Text, prefilled | No |
| How you use it | Checkboxes: Pages / Content hub / Sync from the lists / Two or more targets | No |

- The link prefills the two versions through query parameters named after the fields' IDs
  (`toolkit-version`, `kentico-version`), as issue forms allow. GitHub documents this for issue forms,
  not for discussion forms, but it works there too (checked after the merge). If GitHub stops honouring
  them, the form opens with the two fields empty, which is harmless.
- The dropdown isn't required: GitHub preselects the first option of a required dropdown, so every
  post would say "Works well" unless changed. Optional, it starts on "None".
- The category's format is "Open-ended discussion", so replies and threads work.

**C. A pinned feedback discussion per release.** Created when a release is published, in the Feedback
category, pinned, titled "Feedback on 1.0.0-beta.6". It lists what changed and asks what works and what
doesn't. The previous one is unpinned.

- The package carries the discussion's URL as a constant (`ContentSyncRoute.ReleaseFeedbackUrl`),
  so the discussion is created before the package is built. That adds a step to the release checklist.
- If the constant is empty (a build from source, or a release without one), the one-time line goes to B.

**Versions in the links.** The toolkit's version is its assembly's informational version, without the
build metadata. Kentico's is the `CMS` assembly's version (for example 31.9.1). Nothing else goes in the
link: no host, user or content.

### What isn't added

- No telemetry, no request from the toolkit to GitHub or anywhere else. The links are plain links that
  the editor chooses to open.
- No prompts, ratings, counters or reminders.
- No feedback in the README, the package or the docs beyond a sentence in the Usage guide about the link
  and the setting.

## Compatibility

- Same on 30.8.0 and 31.x; checked on both.
- One new setting (`FeedbackLinks`), no new endpoint, no schema change.
- `InstanceLabel` and `TargetLabel` keep their meaning; only how they're drawn changes.
- `ContentSyncRules.InstancesCaption` is removed; the sync confirmation keeps naming the instances with
  `ContentSyncRules.InstanceName`.

## Test strategy

- Unit tests:
  - what each side of the chip shows for each combination of labels, and with no request host;
  - host shortening;
  - `GetRouteState`: no list request yet, answered, each failure kind, a recent completed sync by this
    user, a failed sync, someone else's sync, and `FeedbackLinks=false`;
  - the version strings in the link.
- Client: `npm run typecheck` and the build, as today.
- On the rig, on 31.9.1 and 30.8.0:
  - the chip with both labels, none, and each alone; hovering shows the hosts;
  - the dot green, then red with the target stopped (and its hover text), then green after it's
    started and the list refreshed;
  - the Feedback link opens the form with the versions filled in;
  - a sync from the list, then the one-time line; ✕ hides it; a reload doesn't bring it back; a second
    sync doesn't either;
  - `FeedbackLinks=false`: no link, no line;
  - nothing else on the page moves: the dropdowns, toolbar and callouts are where they are today.

### Checked

November 2026, driven with the Playwright CLI:

- `31.9.1`, the repository's rig:
  - no labels: "localhost:27300 → ● localhost:27311", Feedback at the row's right end; the link carries
    `toolkit-version` and `kentico-version=31.9.1`;
  - both labels: "Staging → ● Production" on the Content hub tab; hovering Production shows
    "localhost:27311" in Kentico's tooltip;
  - the target stopped: the dot red with the "Target unavailable" row; target started, Refresh: green,
    hover "localhost:27311 answered";
  - a sync from a row, from a fresh start: `GetRouteState` answered `syncRunning` while it ran, then
    `syncedRecently`, and the line showed before the dialog closed; ✕ hid it, and neither a reload nor
    the other tab brought it back (one `localStorage` key for the version and user);
  - `FeedbackLinks=false`: the chip without the Feedback link.
- `30.8.0`, with the library packed as `1.0.0-route2`: the chip, dot and hover text as above, and the
  toolbar where it was with the old caption (it covered the chip before the invisible caption).
- GitHub's real form, after the merge: the link opens the Feedback form with both versions filled in
  (`toolkit-version` and `kentico-version` work as query parameters on discussion forms too). It also
  showed the required dropdown preselecting "Works well", so the dropdown was made optional.

## Decisions

Agreed in review (November 2026):

- The route chip, option 2 on the design page: labels when set, hosts on hover, with a reachability dot.
- Feedback in GitHub Discussions: a Feedback category with a form (B) and a pinned discussion per release
  (C). Issues stay for bugs.
- A quiet link at the end of the line, and a one-time line after a user's first completed sync per
  version, both hidden by one setting.
- Discussions turned on, and the Feedback category created, in November 2026.

## Acceptance criteria

- Both tabs show the route chip instead of the caption, on 30.8.0 and 31.x.
- The chip shows labels when set and hosts otherwise, with each host on hover.
- The dot shows the target's state from the list's own request, without another request.
- The Feedback link opens GitHub's feedback form; the one-time line shows once per version after a
  completed sync, and neither shows with `FeedbackLinks=false`.
- The toolkit sends nothing anywhere.
