import { useEffect, useRef } from "react";
import { useLocation, useNavigate } from "react-router-dom";

import { RoutingContentPlaceholder, usePageCommand } from "@kentico/xperience-admin-base";

import { listPath, loadChoice } from "./navigation";

interface StatusEntryProps {
  /** The menu entry's own address below the admin path, e.g. "/content-sync/pages". */
  readonly entryPath: string;
  /** The list's segment below the language: "all". */
  readonly listSlug: string;
  /** The channels or workspaces the editor can open. */
  readonly scopeIds: readonly number[];
  /** The languages that can be opened. */
  readonly languageNames: readonly string[];
  /** Where the editor's last choice on the tab is kept. */
  readonly storageKey: string;
  /** ContentSyncToolkitOptions.FullReloadOnSwitch: a full page load instead of the router. */
  readonly fullReload: boolean;
}

/** What the entry page's GetDefaultTarget command answers. */
interface DefaultTarget {
  readonly target: string;
}

/**
 * The page template of a status list's menu entry (see ContentSyncStatusEntryBase). It shows the levels
 * below it: the dropdowns and the list. Opened on its own, it goes on to the editor's last choice on
 * the tab, if both its channel or workspace and its language can still be opened. Otherwise it asks
 * the server for the default, which is only worked out then. It goes through the admin's router,
 * keeping the address's letter case, so the menu stays drawn.
 */
export const StatusEntryTemplate = ({
  entryPath,
  listSlug,
  scopeIds,
  languageNames,
  storageKey,
  fullReload,
}: StatusEntryProps) => {
  const navigate = useNavigate();
  const location = useLocation();

  // The address of the entry itself, below which the browser's address goes on; null elsewhere.
  const entryAddress = () => {
    const path = window.location.pathname.replace(/\/$/, "");
    const entry = entryPath.replace(/\/$/, "");
    return entry && path.toLowerCase().endsWith(entry.toLowerCase()) ? { path, entry } : null;
  };

  const goTo = (target: string) => {
    const address = entryAddress();
    // Only while the entry itself is still open: the editor may have moved on meanwhile.
    if (!target || !address) {
      return;
    }

    const adminRoot = address.path.slice(0, address.path.length - address.entry.length);
    if (fullReload) {
      window.location.replace(adminRoot + target);
      return;
    }

    // The application segment as the address spells it, then the rest of the target.
    const application = "/" + address.entry.split("/")[1];
    const shownApplication = address.path.slice(adminRoot.length, adminRoot.length + application.length);
    navigate(shownApplication + target.slice(application.length), { replace: true });
  };
  const goToRef = useRef(goTo);
  goToRef.current = goTo;

  const { execute: getDefaultTarget } = usePageCommand<DefaultTarget>("GetDefaultTarget", {
    after: (result) => goToRef.current(result?.target ?? ""),
  });

  useEffect(() => {
    const address = entryAddress();
    if (!address) {
      return;
    }

    const choice = loadChoice(storageKey);
    if (choice
      && scopeIds.includes(choice.scopeId)
      && languageNames.some((name) => name.toLowerCase() === choice.languageName.toLowerCase())) {
      goTo(listPath(address.entry, listSlug, choice));
    } else {
      getDefaultTarget();
    }
    // Runs when the address changes (the entry itself opened again); the rest is read from this render.
  }, [location.pathname]);

  return <RoutingContentPlaceholder />;
};
