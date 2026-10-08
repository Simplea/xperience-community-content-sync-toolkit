# Feature specification: Sync status beyond pages and content hub items

Status: **proposal**, not scheduled. Nothing here is built yet.

## Context

The foundation compares two kinds of content: website channel pages and
reusable content hub items, one language at a time. Content Sync transfers
more than that, and editors run into differences the Sync status page can't
show today. This spec lists those other kinds of data, what Content Sync does
with each, and which the toolkit could compare.

What Content Sync transfers (Kentico's
[Content sync](https://docs.kentico.com/documentation/business-users/content-sync)
documentation):

| Data | How it's synced |
| --- | --- |
| Reusable content items, pages | Selected by the editor. Compared today. |
| Forms | Selected in the Forms application, or with pages that show them (**Include forms**). Since **30.9.0**, one version after this toolkit's minimum. Without submissions, autoresponders, automation processes or notifications. |
| Content item assets, content folders, taxonomies and tags | Always included with the items that use them; can't be excluded. |
| Page URLs (system and vanity), former URLs, page folders | Included with pages. |
| Linked items, pages and forms | Included by default; can be excluded in Detailed selection. |
| Security settings (secured flag, member role restrictions) | Included; the member roles themselves aren't. |

Not transferred, and already checked as required objects (see the
foundation's Required objects): content types, languages, website channels,
workspaces. Member roles and image variant definitions aren't checked yet.

**Localization.** Xperience by Kentico has no editor-managed resource strings:
application texts live in `.resx` files deployed with the code (Kentico's
upgrade guide), or in a community module that Content Sync doesn't know about.
For this toolkit, localization therefore means **language variants** of
content: which items exist, and are current, in which languages.

## Candidates

Ordered by value to editors, then by cost.

### 1. Language coverage

**Problem.** The page compares one language at a time, chosen in a filter. An
editor preparing a release in several languages has to switch languages and
compare each, and can't see that an item is current in English but missing in
Spanish on the target. Content Sync also syncs one language per sync, so a
forgotten language is a common gap.

**Proposal.** A **Languages** view: one row per item, one column per language,
each cell showing that language variant's status (New, Changed, Reordered,
In sync, and so on, or "not translated here"). A filter shows only items
with a gap in some language.

**Data.** The existing inventory, once per language. The endpoint already
takes a language, so no wire change; the cost is one target request per
language (cached as today). Columns are dynamic, so the listing is built per
request from the instance's languages.

**Open questions.** How many languages a site can have before the grid is too
wide (a cap, with a language filter beyond it); whether "not translated here,
exists on target" (an extra variant) matters.

### 2. Forms

**Problem.** Forms are synced separately or with pages, and their field
configuration can change without any page changing. Nothing shows that a form
on the target is older or missing. A form whose fields changed also deletes
submission data for removed fields on the target when synced (Kentico's
warning), so editors should see it before syncing.

**Proposal.** A **Forms** tab, like Content hub: each form's status, with
a "Fields differ" status (a kind of Changed), and a tooltip that repeats Kentico's
warning about removed fields.

**Data.** A forms inventory: form GUID, code and display name, last modified
time, and a hash of the form's field definition (like content types' hash in
the required-objects check). Requires a new endpoint and schema version.

**Compatibility.** Content Sync syncs forms only from **30.9.0**. On 30.8.0
the tab is hidden, or the toolkit's minimum version moves to 30.9.0; either is
a release decision.

### 3. Taxonomies and tags

**Problem.** Tags travel with the items that use them, so renaming or moving a
tag on the source isn't visible on the target until an item using it is
synced, and nothing shows that the tag differs.

**Proposal.** Compare taxonomies and tags by GUID: missing, renamed, or moved
to another parent on the target. Show them on the items that use them, like
Incompatible ("The target has an older version of tag X; sync any item that uses
it"), rather than as a tab, since editors can't sync tags directly.

### 4. Page URLs

**Problem.** Changing a page's URL slug or vanity URL changes what visitors see
on the target, and editors may expect it to show as a difference.

**Open question first.** Whether a URL change creates a new page version (then
the publish date already covers it) or not. Verify on the rig before
designing anything; if it doesn't, compare each page's canonical URL path per
language and add a "URL changed" status.

### 5. Content folders

**Problem.** Moving a content hub item to another folder may not change its
publish date, so it would look In sync while the target shows it elsewhere.

**Open question first.** As for URLs: verify whether a folder move creates a
version. If not, compare each item's folder (by GUID) and show a moved item
as Moved, like pages.

### 6. More required objects

Add **member roles** (by GUID and code name, as Kentico matches them) and
**image variant definitions** to the required-objects check. Both are
documented as required and not transferred.

## Not candidates

- **Assets:** a changed file creates a new item version, so publish dates
  already cover it.
- **Emails, headless items, settings, users, contacts:** Content Sync doesn't
  transfer them.
- **Resource strings:** not editor-managed in Xperience by Kentico (see
  Localization).
- **Form submissions:** never synced, by design.

## Approach and order

1. Verify the two open questions (URLs, folders) on the rig; they decide
   whether candidates 4 and 5 are needed at all.
2. Language coverage: no wire change, the most common gap.
3. More required objects: small, extends an existing check.
4. Forms: a new tab and endpoint, plus the 30.9.0 decision.
5. Taxonomies and tags.

Each step follows the foundation's conventions: pure comparison logic with
unit tests, secret-gated `no-store` endpoints, a schema version bump for any
wire change, older targets treated as "not checked", and a live two-instance
check.
