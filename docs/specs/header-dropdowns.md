# Feature specification: Channel, workspace and language dropdowns

Status: **implemented** (November 2026; verified on `31.9.1` and `30.8.0`), researched in a spike (branch
`spike/header-selectors`, not merged).

## Context

The status lists choose what they compare with fields in their **Filter** panel: **Channel** (Pages) or
**Workspace** (Content hub), and **Language**. Kentico's own applications put these choices in the top
bar instead: Content hub has a workspace dropdown and a language dropdown, and a website channel's page
tree has a language dropdown. Editors know those. In the Filter panel the same choices are a click
further away and look like ordinary filters.

Because they're filter fields, they also share the filter's limits. With nothing applied, Kentico hides
the whole toolbar, Filter included, under an empty list (issue #33). That is why the lists stopped
opening on pending items only before 1.0.0: an editor whose default channel or workspace had nothing
pending was left on an empty list with no way to choose another (see
[sync-status-admin-page.md](sync-status-admin-page.md), **Hide items in sync**).

This spec moves the channel or workspace and the language to Kentico's own top-bar dropdowns, makes the
lists open on pending items again, and removes the fields the dropdowns replace from the Filter panel.
Like Kentico's own applications, the lists then show one language at a time, so the Language column goes
too.

## Research findings

From the spike, on the repository's rig, against Kentico's documentation
([Admin UI customization model overview](https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/admin-ui-customization-model-overview),
[Prepare your environment for admin development](https://docs.kentico.com/documentation/developers-and-admins/customization/extend-the-administration-interface/prepare-your-environment-for-admin-development)).
Kentico's software was only inspected through its public signatures, its public npm typings, its
documentation and the admin's own requests in the browser; nothing was decompiled.

### Kentico's dropdowns are page templates

- The top-bar dropdowns aren't components a page can place. They belong to two **page templates**:
  `TemplateNames.WORKSPACE` and `TemplateNames.CONTENT_LANGUAGE`. A page registered with one of them
  draws its dropdown in the top bar and the levels below it underneath. Their properties are public:
  `ContentHubWorkspaceClientProperties` (`WorkspaceId`, `Workspaces`) and
  `ContentHubContentLanguageClientProperties` (`LanguageName`, `Languages`, a list of
  `ContentLanguageModel`).
- The React components behind them aren't exported by `@kentico/xperience-admin-base` or
  `@kentico/xperience-admin-components`. Reusing them means using the templates.
- The **workspace** template takes any list of id and name pairs, so it can list website channels too.
  It hides itself when the list has one entry. Its heading, **Workspaces**, is the global localization key
  `admin.base.workspaceSelector.headline`: changing it would change Content hub too.
- The **language** template takes any list of `ContentLanguageModel`. Its heading is **Language
  variants**. An extra first entry, **All languages** with Kentico's `xp-earth` icon, also works (drawn
  and chosen like a language), but isn't used: see Decisions.
- A dropdown switch replaces its own segment of the address and keeps the segments below it. The list
  therefore sits at a parameter level under the language level, with a fixed default value (`all`), as
  Content hub's own list does (`content-hub/{workspace}/{language}/all/list`).

### The left menu and Kentico's navigation

- The admin fetches each level of an address separately (`POST /admin/api/page` with its `path`). On a
  navigation it fetches only the levels from the first one that changed. A full load, the browser's
  reload and a direct link fetch every level.
- Content synchronization's left menu is drawn with the application level. It **disappears whenever the
  levels drawn by these templates are replaced or removed without the application level being fetched
  again**: a dropdown switch, Back and Forward, and a left-menu click from a status list to the List of
  content synchronizations. Kentico's own Content hub has no left menu, so it isn't affected there.
- Attempts to keep the menu, each run through the same round of switches, Back and Forward, reloads and
  menu clicks:

  | Attempt | Result |
  | --- | --- |
  | A full page load on each switch | Works, but each switch reloads the page (the top bar and menu are drawn again) |
  | Sending the browser's Back/Forward signal (`popstate`) after the switch | Fails: menu lost, later menu clicks dead |
  | Through the admin's own router, via the start page and straight back | Fails: menu lost |
  | **Through the admin's own router, with the application segment in the other letter case** (`Content-sync` / `content-sync`) | **Works**: no reload, menu kept and highlighted, Back and Forward too |
  | As above, writing the hyphen as `%2D` instead | Fails: the admin decodes the address before comparing |
  | As above, then putting the lowercase address back in the address bar | Fails: Back loses the menu |

- Why the letter-case attempt works: the admin compares levels case-sensitively, so a different spelling
  of the application segment makes it fetch from the application level, menu included. The server matches
  slugs case-insensitively, so both spellings open the same page, and links with either spelling work.
- The admin's router is `react-router-dom` (7.18.4 in 31.9.1). Kentico's webpack configuration
  (`@kentico/xperience-webpack-config`) shares it with custom admin modules at runtime, so a custom
  component's `useNavigate()` drives the admin's own router. Kentico's documentation lists `react` and
  `react-router` as shared runtime dependencies that a module must never bundle, and says their behaviour
  can change with a Kentico update even if the module isn't rebuilt.
- A menu entry can't forward to its default address from the server: a `RedirectUrl` from a menu
  entry's page was ignored in about one of four clicks from the List of content synchronizations. A small
  custom page template that navigates in the browser works every time.
- A `SubcaptionComponent` (a component next to the listing's caption) renders on the toolkit's lists,
  which is where the navigation component goes. It isn't rendered on Kentico's own List of content
  synchronizations, which doesn't need it.
- On the local rig a switch shows the new list in about 0.1 s.

### Pending-only default

- `ListingTemplateClientProperties.FilterInitialValues` (Kentico 31.2.0 and later) starts a listing with
  a filter applied. Because it's an applied filter, Kentico's empty state offers *Clear your filters
  here*, so an editor can always get back to the full list. When the editor clears the filter, Kentico
  saves that empty state per browser, and the initial values no longer apply in that browser.
- With the channel or workspace and language in the top bar, an empty list no longer hides them: issue
  #33's original problem is gone whatever the filter shows.

## Goal

- Choose the channel (Pages) or workspace (Content hub) and the language with Kentico's own top-bar
  dropdowns, as in Content hub, inside Content synchronization, with its left menu kept.
- Open the lists on pending items, without ever leaving an editor on an empty list they can't leave.
- Remove the Filter fields the dropdowns replace.
- Show one language at a time, with a default selected, as Kentico does.

## Design

### Addresses

Each tab keeps its menu entry and gains two levels under it:

| Tab | Address |
| --- | --- |
| Pages | `/admin/content-sync/pages/{websiteChannelId}/{language}/all` |
| Content hub | `/admin/content-sync/content-hub/{workspaceId}/{language}/all` |

- `{language}` is a content language code name.
- The levels sit under the menu entries, so the left menu highlights the tab and the breadcrumb reads
  *Content synchronization › Pages* or *› Content hub*. The two levels have no breadcrumb or menu entry
  of their own.
- The menu entry itself (`/admin/content-sync/pages`, `/admin/content-sync/content-hub`) forwards to the
  editor's last choice on that tab (see Remembering the last choice), or, without one, to the default
  channel or workspace and its default language:
  - **Channel or workspace:** today's rule, the first one in the editor's list that has content.
  - **Language:** on Pages, the channel's primary language (`WebsiteChannelPrimaryContentLanguageID`), as
    Kentico's page tree opens on it; on Content hub, the instance's default content language (the one
    Kentico's language dropdown marks `IsDefault`), as Kentico's Content hub does.
- Switching the channel or workspace keeps the selected language, as Kentico's dropdowns do.
- A channel or workspace id that doesn't exist, or that the editor can't open, shows today's "no longer
  exists" view. So does an unknown language.
- The menu entries keep their addresses, so bookmarks to `/admin/content-sync/pages` and
  `/admin/content-sync/content-hub` still open the lists. Nothing else in the toolkit links to them.

### The dropdowns

- **Pages:** Kentico's workspace dropdown, listing the website channels the editor can open (today's
  channel list). It's headed **Workspaces**, a Kentico text the toolkit can't change for one page only.
  With one website channel it's hidden, as in Kentico.
- **Content hub:** Kentico's workspace dropdown, listing the workspaces the editor can open.
- **Language, both tabs:** Kentico's language dropdown, with the instance's languages in Kentico's
  order. The list compares the selected language only.

### Keeping the left menu

A small browser module of the toolkit's (see Implementation notes) has two parts:

- **Navigation component**, rendered invisibly next to each list's caption. While a list is open, a
  navigation from it to another address in Content synchronization (a dropdown switch or a left-menu
  click) goes through the admin's own router, with the application segment written in the other letter
  case from the current address. The admin fetches from the application level and draws the menu again,
  without a page reload. Every other navigation is left alone, and leaving the list restores the
  browser's own behaviour.
- **Menu entry template**: the menu entries' page template. Opened on its own, it navigates (through the
  admin's router, keeping the address's letter case) to the editor's last choice when it's still valid.
  Otherwise it asks the server for the default address (a page command, `GetDefaultTarget`), which is
  only worked out then: finding the first channel or workspace with content took up to 2.3 s for an
  editor who sees no pages on the large site checked for 1.0.0-beta.4 (see Compatibility.md).

**Fallback.** The letter-case navigation relies on behaviour Kentico doesn't document. A setting,
`ContentSyncToolkitOptions.FullReloadOnSwitch` (default `false`), makes both parts use full page loads
instead: slower (the page is drawn again on each switch) but independent of how the admin compares
addresses. It's the documented way out if a Kentico update changes that behaviour, until the toolkit
adapts. The Compatibility check for each new Kentico version includes the round of switches from the
Test strategy.

The address bar alternates between `content-sync` and `Content-sync`. That's visible only there; links
copied with either spelling work.

### Remembering the last choice

- **Saved:** when a list opens, the navigation component saves its channel or workspace and language in
  the browser's local storage, one entry per tab (Pages, Content hub), keyed by the signed-in user's id
  so editors sharing a browser keep their own choices. Nothing is saved on the server.
- **Used:** the menu entry's properties carry the ids and code names the editor can open (the dropdowns'
  own lists). The menu entry template opens the saved channel or workspace and language when both are
  still in those lists, and asks for the default otherwise: a deleted channel, workspace or
  language, or lost access, falls back without an error. The server still checks every address (see
  Security); the saved choice is only a suggestion.
- The saved choice is per browser: it doesn't follow the editor to another machine, as with Kentico's
  own remembered selections. Storage that's unavailable (private windows, blocked site data) just means
  the default opens.
- Applies in the fallback mode too (`FullReloadOnSwitch`): the menu entry loads the saved address in
  full instead.

### Filter

Removed: **Channel** (Pages), **Workspace** (Content hub) and **Language**. The dropdowns replace them.

Kept: **Status**, **Hide items in sync**, **Only my changes**, **Content type**, **Published from** and
**Published to**, with today's behaviour.

A filter saved in the browser with the removed fields loses them; the remaining fields still apply.

### Language column

Removed. Every row is in the language selected in the top bar, so the column would repeat it. Each row
is still one language version; the row's language comes from the address.

### Pending-only default

- On Kentico **31.2.0 and later**, each list starts with **Hide items in sync** applied
  (`FilterInitialValues`). The list opens on what differs. If nothing does, Kentico's empty state shows
  *Clear your filters here*, which shows every item. The channel or workspace and language stay
  available in the top bar either way.
- An editor who clears the filter keeps that choice in their browser (Kentico's behaviour); the list then
  opens on every item for them.
- **Earlier versions** have no `FilterInitialValues`; the lists open on every item there, as today, with
  pending items first (the Status sort).

### Implementation notes

- **Pages:** each tab's menu entry (custom template), its scope level (`TemplateNames.WORKSPACE`), its
  language level (`TemplateNames.CONTENT_LANGUAGE`) and the list (`TemplateNames.LISTING`, parameter slug
  defaulting to `all`). The list reads the scope and language from its parent levels' parameters instead
  of the filter.
- **Browser module:** `src/XperienceCommunity.ContentSyncToolkit/Client`, built with Kentico's webpack
  configuration (`@kentico/xperience-webpack-config`) and registered with
  `AdminModule.RegisterClientModule`, as Kentico documents for custom admin code. The built files are
  embedded in the package (`AdminClientPath`). The `@kentico/*` npm packages match the lowest supported
  Kentico version.
- **Build:** the built files aren't committed. CI and the release workflow install Node and build the
  module before `dotnet build`; a local `dotnet build` builds it when its output is missing or older than
  its sources. CONTRIBUTING documents Node as a prerequisite.
- **Removed:** the Filter's scope and language fields and their option providers, where nothing else
  uses them; the every-language comparison (each language compared in turn and listed together) and the
  Language column.
- **Docs:** [sync-status-admin-page.md](sync-status-admin-page.md) (Language and scope fields, the
  every-language default, Hide items in sync), the Usage Guide, the README and their screenshots.

## Decisions

- **Kentico's own dropdowns, not the toolkit's.** Reuses what editors know and what Kentico maintains,
  rather than a copy that drifts from it. Our own Select-based dropdowns were built in a spike and
  rejected: they looked different and sat in the wrong place.
- **Inside Content synchronization.** The tabs stay in its left menu; no separate application.
- **Router navigation with the letter-case difference, with a full-reload fallback.** The only way found
  to keep the left menu without reloading the page (see Research findings). Full reloads work everywhere
  but redraw the whole page on each switch.
- **One language at a time, with a default selected.** Since 1.0.0 the lists compare every language by
  default, because the earlier default (the default language only) was invisible: nothing on the page
  showed which language was compared, so an empty list could hide another language's changes. With
  Kentico's dropdown in the top bar the selected language is always shown, as in Kentico's own
  applications, so that reason is gone. One language is also one comparison per switch instead of one per
  language, and matches the sync, which runs one language at a time. The cost: another language's
  pending changes show only after switching to it, as in Kentico. An **All languages** entry in Kentico's
  dropdown was tried and works, but would keep the slower comparison and the Language column for little
  gain.
- **Pending-only through `FilterInitialValues`, not a "Show items in sync" default.** An applied filter
  keeps Kentico's *Clear your filters here*, so an editor is never stuck on an empty list.
- **The last choice is remembered per browser, not per user account.** Editors come back to the channel
  or workspace and language they were working on, as in Kentico's own applications. Browser storage
  needs no table or endpoint; following the editor across machines would need a user-settings table,
  which isn't worth it for this. The address still carries the choice for Back, Forward, bookmarks and
  shared links.

## Compatibility

- **`31.9.1`:** researched in the spike. The implementation passed the browser round on the rig, with
  the navigation and with full reloads, and also:
  - a restricted editor (Article reviewer, with View on Content synchronization) offered only their
    two workspaces, opening on the first with content, with page permissions applied on Pages; a link
    to a workspace they can't open shows "Not available", and a saved choice for it falls back to the
    default;
  - two users in the same browser keeping their own saved choices;
  - two website channels (a temporary second channel, primary language Spanish, without pages):
    the default skipped the empty channel; switching the channel kept the language and the reverse;
    Back; the saved channel reopened from the menu, and fell back to the default once the channel was
    deleted;
  - a filter saved by 1.0.0-beta.4, with Channel, Workspace and Language values: Kentico ignores the
    removed fields and applies the rest;
  - Hide items in sync applied when each list first opens, in eight clean rounds of switches.
- **`30.8.0`:** Kentico's public API documentation (in the NuGet package) has both templates
  (`TemplateNames.WORKSPACE`, `TemplateNames.CONTENT_LANGUAGE`), their client properties,
  `ContentLanguageModel` and `SubcaptionComponentBase`. The admin uses `react-router-dom` 6.30.1 there (7.18.4
  in 31.9.1); the module uses only `useNavigate` and `useLocation`, which are the same in both, and
  compiles against the lowest version. Browser round passed on the 30.8.0 rig (spike, module built with
  the 30.8.0 `@kentico/*` packages): dropdown switches on both tabs, Back and Forward, left-menu clicks,
  direct links in any letter case, the browser's reload and the Refresh button; the menu shown and
  highlighted and the breadcrumb right at each step; no browser errors. The same with full reloads (the
  fallback).
- **`FilterInitialValues`:** 31.2.0 and later only (see Pending-only default); the toolkit sets it only
  when the property exists.

## Security

- The channel or workspace list is the editor's existing scope list (only what they can open); an id in
  the address is checked against it, as a filter value is today.
- No new endpoint. The browser module only navigates within the admin.
- The remembered choice holds ids and code names only, in the editor's own browser. It's checked against
  the editor's current lists before use, and the server checks the address again.

## Out of scope

- Remembering the last choice across machines (a server-side user setting).
- Changing the **Workspaces** heading for the Pages tab.
- Dropdowns on the List of content synchronizations (Kentico's own page; it lists every synchronization).

## Test strategy

- **Unit tests:** the address parameters become the request's scope and language; unknown ids and
  languages; the menu entry's default address (the channel's primary language on Pages, the default
  content language on Content hub) and the lists of ids and code names it carries for the remembered
  choice; the filter models no longer have scope or language fields; no Language
  column; `FilterInitialValues` is set when the property exists; the `FullReloadOnSwitch` option reaches
  the browser module.
- **Browser round** on the rig, on each supported Kentico version, with the navigation and the fallback:
  - dropdown switches on both tabs (channel or workspace, language);
  - Back and Forward, the browser's reload, the Refresh button and direct links (either spelling);
  - left-menu clicks between Pages, Content hub and the List of content synchronizations, in any order;
  - the remembered choice: a tab reopened from the menu (and after signing in again) opens the last
    channel or workspace and language; another user in the same browser gets their own; a removed
    workspace or language falls back to the default;
  - each step: the right list, the menu shown and highlighted, the breadcrumb, no browser errors.
- **Format and build:** CI builds the browser module and fails if it doesn't build.

## Acceptance criteria

- Both tabs show Kentico's channel or workspace dropdown and language dropdown in the top bar. Opening a
  tab from the menu selects the editor's last channel or workspace and language on that tab, or the
  defaults when there's none or it's no longer available.
- Switching either dropdown changes the list without a page reload; the left menu stays, with the tab
  highlighted, and the breadcrumb names the tab.
- Back, Forward, reload, Refresh and direct links show the right list with the menu.
- The Filter panel has no Channel, Workspace or Language field, and the lists have no Language column.
- On 31.2.0 and later the lists open with **Hide items in sync** applied, and *Clear your filters here*
  shows every item when nothing is pending.
- With `FullReloadOnSwitch` set, every switch is a full page load and everything above still holds.
- The browser round passes on every supported Kentico version.
