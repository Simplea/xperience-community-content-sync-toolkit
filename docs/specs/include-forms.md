# Feature specification: Include forms when syncing pages and content items

Status: **implemented** (November 2026), for 1.0.0-beta.6 (step 3). Reviewed before building; where the
build differs, this spec says how.

## Context

Kentico's Sync dialog for pages and content items has an **Include forms** checkbox: ticked, the forms
that the selected pages show in Page Builder (through the Form widget) go with them
([Content sync](https://docs.kentico.com/documentation/business-users/content-sync#sync-forms-with-pages)).
The dialog lists only the selected pages or items; the forms aren't listed there, only in Detailed
selection
([Work with content sync](https://docs.kentico.com/guides/digital-marketing/work-with-content-governance/work-with-content-sync)).

The toolkit hosts that same dialog on the Pages and Content hub tabs, but answers that the editor may
never sync forms, so the checkbox doesn't show, and every sync from the list goes with
`includeLinkedForms: false`. That was decision 3 of [sync-actions.md](sync-actions.md): forms would wait
for their own tab. This spec reverses it. The checkbox works as in Kentico, without listing forms; a
Forms tab is a separate, later piece ([forms-tab.md](forms-tab.md)).

## What we know

From the forms spike (November 2026, on `31.9.1`, with Kentico's public signatures from `30.8.0` to
`31.10.3`; nothing decompiled):

- **`ISynchronizationService.Create`** has an `includeLinkedForms` parameter from `30.9.0`, when Kentico
  added forms to Content Sync. `30.8.0`'s `Create` has none.
- **The dialog** shows **Include forms** when `LoadContentSynchronizationInitialData` answers
  `hasFormSyncPermission: true`. It starts unticked. Its Sync sends `includeLinkedForms` in
  `CreateContentSynchronization`.
- **Checked end to end:** with the toolkit's answer changed to `true`, the Pages tab's dialog showed the
  checkbox; ticked, the toolkit passed the flag to `Create`, and the page's form ("Coffee sample list")
  arrived on the target.
- **Kentico's permission rule:** the editor needs the **Synchronize** permission for the **Forms**
  application. Before `31.11.0` it's one application permission, stored as
  `Kentico.Xperience.Application.Forms` / `Synchronize` (seen in `CMS_ApplicationPermission`). From
  `31.11.0` forms belong to workspaces and the permission is per workspace; Kentico shows the checkbox
  when the editor has it in at least one workspace, and checks the workspace of each form it syncs.

## Design

1. **The checkbox.** `LoadContentSynchronizationInitialData` answers `hasFormSyncPermission: true` when:
   - this Xperience version's `Create` takes `includeLinkedForms` (`30.9.0` and later), and
   - the editor may sync forms: an administrator, or a role with Forms' Synchronize permission (before
     `31.11.0`); from `31.11.0`, Forms' Synchronize permission in **every workspace that has forms**.

   The last rule is stricter than Kentico's "at least one workspace". The toolkit calls Kentico's engine
   directly, which takes all the pages' forms, so it can't leave out a form in a workspace the editor
   can't sync. With the permission in every workspace that has forms, nothing goes that the editor
   couldn't sync in Kentico. The Forms tab can relax this later, when it knows each page's forms.
2. **The choice reaches Kentico.** The dialog's Sync sends `includeLinkedForms`; the toolkit carries it
   to `Create`, in every sync it starts for the selection (one per language and size limit). The server
   checks the permission again and ignores the flag without it.
3. **The record.** The Event log entry for a started sync adds a line: "Forms used on these pages:
   included" (or "items" on Content hub). Without the checkbox ticked, nothing changes.
4. **`30.8.0`**, and any version whose `Create` has no `includeLinkedForms`: no checkbox, as today.
5. **What doesn't change:** the review list, the statuses, Detailed selection's list (Kentico lists forms
   there; the toolkit's dependency list doesn't yet, see below), and the row's own text confirmation
   when the toolkit's dialog isn't used.

### Detailed selection

Kentico's Detailed selection lists a page's forms with its other dependencies, and syncs only the forms
ticked there: its Sync sends only the ticked items (`selectedIdentifiers`), never the **Include forms**
checkbox (checked on `31.9.1`). The toolkit's Detailed selection lists what `IDependencyRetriever`
returns, which on `31.9.1` doesn't include the page's form. So a sync from the toolkit's Detailed
selection sends no forms, whatever the checkbox says. Listing forms there belongs to the Forms tab
([forms-tab.md](forms-tab.md)).

## Compatibility

- No new setting; the checkbox follows `ContentSyncToolkit:SyncActions` with the rest of the dialog.
- No change on the target.
- `IContentSyncEngine` gains `SupportsLinkedForms`; `ContentSyncEngineRequest` gains `IncludeLinkedForms`
  (asking for it where `Create` can't take it fails clearly, as Detailed selection does).

## Test strategy

- Unit tests: `Create`'s arguments with and without the flag; asking for forms on `30.8.0`'s `Create`
  fails clearly; the dialog's argument read; the Event log line.
- On the rig, `31.9.1`:
  - the checkbox shows on both tabs for an administrator, unticked;
  - a page with a form, synced with the box ticked: the form arrives on the target; unticked: it
    doesn't change;
  - an editor without Forms' Synchronize permission: no checkbox;
  - the Event log entry says forms were included.
- `30.8.0`: no checkbox; syncs as today.
- `31.11.0` (workspaces) is checked when the rig moves to it ([forms-tab.md](forms-tab.md), open
  question 3).

## Checked

November 2026, driven with the Playwright CLI:

- `31.9.1`, the repository's rig, as administrator, on the Pages tab with "/Landing_pages/Coffee_samples"
  (which shows the "Coffee sample list" form):
  - the dialog offers **Include forms**, unticked (`hasFormSyncPermission: true`);
  - Sync unticked: `includeLinkedForms: false`, the page synced, the form on the target unchanged;
  - Sync ticked: `includeLinkedForms: true`, the form arrived on the target, and the Event log entry says
    "Forms used on these pages: included.";
  - Detailed selection lists the page without its form; its Sync sent only `{"ContentItem":[136]}`, and
    the form didn't change on the target.
- `30.8.0`, with the library packed as `1.0.0-forms1`: the toolkit's own confirmation, with no forms
  option; the sync started, and its Event log entry has no forms line.
- Not checked yet:
  - an editor without Forms' Synchronize permission (the rig's test editor couldn't sign in; their roles
    have no Forms permission, which is what the check reads);
  - the Content hub tab's dialog (the same command as the Pages tab's);
  - `31.11.0`, with forms in workspaces.

## Decisions

- November 2026: offer **Include forms** on the Pages and Content hub tabs as Kentico does, without
  listing forms, ahead of the Forms tab. Replaces decision 3 of [sync-actions.md](sync-actions.md).
