import { useEffect, useRef } from "react";
import { useLocation, useNavigate } from "react-router-dom";

import { isWithin, otherSpelling, saveChoice } from "./navigation";

interface StatusNavigationProps {
  /** The tab's menu entry below the admin path, e.g. "/content-sync/pages". */
  readonly entryPath: string;
  /** The application below the admin path: "/content-sync". */
  readonly applicationPath: string;
  /** ContentSyncToolkitOptions.FullReloadOnSwitch: full page loads instead of the router. */
  readonly fullReload: boolean;
  /** Where the editor's last choice on the tab is kept. */
  readonly storageKey: string;
  readonly scopeId: number;
  readonly languageName: string;
}

/**
 * Renders nothing, next to a status list's caption (see ContentSyncStatusNavigationComponent).
 *
 * Kentico's dropdowns in the top bar, and the left menu, navigate within the admin. Leaving a list for
 * another address of Content synchronization, the admin redraws only the levels that changed, and
 * the application's left menu doesn't survive that. So while a list is open, such a navigation goes
 * through the admin's router with the application segment in the other letter case (see
 * otherSpelling): the admin redraws from the application level, menu included, without a page load.
 * With fullReload, it's a full page load instead. Other navigations are left alone.
 *
 * It also saves the list's channel or workspace and language as the editor's last choice on the tab.
 */
export const StatusNavigationComponent = ({
  entryPath,
  applicationPath,
  fullReload,
  storageKey,
  scopeId,
  languageName,
}: StatusNavigationProps) => {
  const navigate = useNavigate();
  const location = useLocation();
  const navigateRef = useRef(navigate);
  navigateRef.current = navigate;

  // The part of the browser's address the router doesn't see, e.g. "/admin".
  const routerBase = window.location.pathname.slice(0, window.location.pathname.length - location.pathname.length);
  const routerBaseRef = useRef(routerBase);
  routerBaseRef.current = routerBase;

  useEffect(() => {
    saveChoice(storageKey, { scopeId, languageName });
  }, [storageKey, scopeId, languageName]);

  useEffect(() => {
    const applicationSlug = applicationPath.replace(/^\/|\/$/g, "").toLowerCase();
    const isAdminAddress = (pathname: string) =>
      pathname.toLowerCase().startsWith(routerBaseRef.current.toLowerCase());
    const toRouterPath = (pathname: string) => pathname.slice(routerBaseRef.current.length) || "/";

    // The browser's own methods: the guards below are set on window.history, not its prototype.
    const originalPush = History.prototype.pushState;
    const originalReplace = History.prototype.replaceState;
    let navigating = false;

    const guard = (original: History["pushState"]) =>
      function (this: History, data: unknown, unused: string, url?: string | URL | null) {
        if (!url || navigating) {
          return original.call(this, data, unused, url);
        }

        const current = window.location.pathname;
        const target = new URL(url.toString(), window.location.href);
        const leavesList = target.pathname.replace(/\/$/, "").toLowerCase() !== current.replace(/\/$/, "").toLowerCase();
        if (!leavesList
          || !isAdminAddress(target.pathname)
          || !isWithin(toRouterPath(current), entryPath)
          || !isWithin(toRouterPath(target.pathname), applicationPath)) {
          return original.call(this, data, unused, url);
        }

        if (fullReload) {
          window.location.assign(target.href);
          return undefined;
        }

        // The admin's own navigation first, so its history entry is in place, then the other spelling
        // over it, once the admin has handled the first.
        const result = original.call(this, data, unused, url);
        const to = otherSpelling(toRouterPath(target.pathname.replace(/\/$/, "")), applicationSlug) + target.search;
        window.setTimeout(() => {
          navigating = true;
          try {
            navigateRef.current(to, { replace: true });
          } finally {
            navigating = false;
          }
        }, 0);

        return result;
      };

    window.history.pushState = guard(originalPush);
    window.history.replaceState = guard(originalReplace);

    return () => {
      window.history.pushState = originalPush;
      window.history.replaceState = originalReplace;
    };
  }, [entryPath, applicationPath, fullReload]);

  return null;
};
