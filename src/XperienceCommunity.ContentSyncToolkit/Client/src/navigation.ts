/** A tab's channel or workspace and language, as the editor last left it. */
export interface Choice {
  readonly scopeId: number;
  readonly languageName: string;
}

/** Saves the editor's choice on a tab. Storage that isn't available (private windows, blocked site data) just isn't used. */
export const saveChoice = (storageKey: string, choice: Choice): void => {
  if (!storageKey) {
    return;
  }

  try {
    window.localStorage.setItem(storageKey, JSON.stringify(choice));
  } catch {
    // Nothing to remember it in: the tab opens on its default.
  }
};

/** The editor's last choice on a tab, or null when there's none or it can't be read. */
export const loadChoice = (storageKey: string): Choice | null => {
  if (!storageKey) {
    return null;
  }

  try {
    const value = JSON.parse(window.localStorage.getItem(storageKey) ?? "null");
    if (value && typeof value.scopeId === "number" && typeof value.languageName === "string") {
      return { scopeId: value.scopeId, languageName: value.languageName };
    }
  } catch {
    // Unreadable: the tab opens on its default.
  }

  return null;
};

/** A status list's address below the admin path; the same as ContentSyncStatusNavigation.ListPath on the server. */
export const listPath = (entryPath: string, listSlug: string, choice: Choice): string =>
  `${entryPath.replace(/\/$/, "")}/${choice.scopeId}/${encodeURIComponent(choice.languageName)}/${listSlug}`;

/**
 * The same router path with the application segment (the first) written in the other letter case:
 * "content-sync" becomes "Content-sync", and any other spelling becomes "content-sync". The server
 * matches addresses whatever their case; the admin compares them exactly, so it treats the other
 * spelling as a new application level and draws it again, left menu included.
 */
export const otherSpelling = (routerPath: string, applicationSlug: string): string => {
  const segments = routerPath.split("/");
  const capitalized = applicationSlug.charAt(0).toUpperCase() + applicationSlug.slice(1);
  segments[1] = segments[1] === applicationSlug ? capitalized : applicationSlug;
  return segments.join("/");
};

/** Whether a path is at or below another, whatever the letter case. */
export const isWithin = (path: string, prefix: string): boolean => {
  const normalized = prefix.replace(/\/$/, "").toLowerCase();
  const lower = path.replace(/\/$/, "").toLowerCase();
  return lower === normalized || lower.startsWith(normalized + "/");
};
