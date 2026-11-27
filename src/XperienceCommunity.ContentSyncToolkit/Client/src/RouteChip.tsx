import { useEffect, useRef, useState } from "react";

import { usePageCommand } from "@kentico/xperience-admin-base";
import { Icon, Tooltip, TooltipPlacement } from "@kentico/xperience-admin-components";

/** One side of the chip (ContentSyncRouteSide). */
export interface RouteSide {
  /** The label, or the host (shortened when long). */
  readonly name: string;
  /** The full host, shown on hover; empty when the name already is it. */
  readonly hover: string;
  readonly isLabel: boolean;
}

/** What the chip shows (ContentSyncRouteProperties). */
export interface RouteProps {
  readonly this: RouteSide;
  readonly target: RouteSide;
  /** The Feedback link; empty when ContentSyncToolkit:FeedbackLinks is off. */
  readonly feedbackUrl: string;
  /** Where the one-time line after a sync goes; empty when the links are off. */
  readonly afterSyncUrl: string;
  /** Where the browser remembers that the line was dismissed. */
  readonly feedbackStorageKey: string;
}

/** What GetRouteState answers (ContentSyncRouteState). */
interface RouteState {
  readonly target: "unknown" | "answered" | "unreachable" | "rejected" | "error";
  readonly syncedRecently: boolean;
  /** This user has a sync running: asked again every few seconds until it finishes. */
  readonly syncRunning: boolean;
}

const colors = {
  ink: "#151515",
  muted: "#6b6470",
  line: "#e7e1e4",
  link: "#8a14b8",
  green: "#0b7a6c",
  greenSoft: "#e3f4f1",
  red: "#c4001a",
  redSoft: "#fde8eb",
  grey: "#a39ba5",
  greySoft: "#eeeaec",
};

// Kentico's subcaption wrapper is as wide as its content and padded 16px on each side (an inline style,
// hence !important): it grows here and loses the padding, so the chip lines up with Refresh and Feedback
// with Filter. On 30.x, the invisible caption before it (see ConfigurePage) keeps its height but takes no
// width. Without :has() support, the link stays next to the chip.
const styles = `
div:has(> .cstk-root) { flex: 1 1 auto; min-width: 0; padding-left: 0 !important; padding-right: 0 !important; }
div:has(+ div > .cstk-root) { width: 0; overflow: hidden; }
.cstk-route { display: flex; align-items: center; gap: 12px; margin: 2px 0 4px; font-size: 14px; color: ${colors.ink}; }
.cstk-route svg { width: 16px; height: 16px; }
.cstk-chip { display: inline-flex; align-items: center; gap: 10px; background: #fff; border-radius: 20px; padding: 5px 14px;
  box-shadow: 0 2px 6px rgba(0, 0, 0, .06); max-width: 100%; min-width: 0; }
.cstk-side { display: inline-flex; align-items: center; gap: 8px; white-space: nowrap; min-width: 0; }
.cstk-side b { font-weight: 600; }
.cstk-arrow { display: inline-flex; color: ${colors.muted}; }
.cstk-dot { width: 9px; height: 9px; border-radius: 50%; flex: none; }
.cstk-feedback { margin-left: auto; display: inline-flex; align-items: center; gap: 6px; color: ${colors.link}; font-size: 13.5px;
  text-decoration: none; white-space: nowrap; }
.cstk-feedback:hover, .cstk-after a:hover { text-decoration: underline; }
.cstk-after { display: flex; align-items: center; gap: 10px; background: ${colors.greenSoft}; border-left: 4px solid ${colors.green};
  border-radius: 6px; padding: 7px 12px; margin: 6px 0 4px; font-size: 14px; color: ${colors.ink}; }
.cstk-after a { color: ${colors.link}; font-weight: 600; text-decoration: none; }
.cstk-after .cstk-check { display: inline-flex; color: ${colors.green}; }
.cstk-close { margin-left: auto; display: inline-flex; background: none; border: 0; padding: 2px; cursor: pointer; color: ${colors.muted}; }
`;

const dotColors: Record<RouteState["target"], [string, string]> = {
  unknown: [colors.grey, colors.greySoft],
  answered: [colors.green, colors.greenSoft],
  unreachable: [colors.red, colors.redSoft],
  rejected: [colors.red, colors.redSoft],
  error: [colors.red, colors.redSoft],
};

const dotText = (state: RouteState["target"], target: string) => {
  const details = " Details are in the Event log.";
  switch (state) {
    case "answered":
      return `${target} answered`;
    case "unreachable":
      return `${target} didn't answer.${details}`;
    case "rejected":
      return `${target} refused the request.${details}`;
    case "error":
      return `${target} answered with something other than the Content Sync Toolkit's data.${details}`;
    default:
      return `Checking ${target}…`;
  }
};

// localStorage can be unavailable (private windows, blocked storage); the line then shows until dismissed on the page.
const isDismissed = (key: string) => {
  try {
    return window.localStorage.getItem(key) === "1";
  } catch {
    return false;
  }
};

const dismiss = (key: string) => {
  try {
    window.localStorage.setItem(key, "1");
  } catch {
    // Nothing to remember it in.
  }
};

// Whether a DOM change touched the list: rows added or removed (it loaded, refreshed, was filtered or
// paged), or a cell changed in place.
const changesList = (mutations: MutationRecord[], own: Node | null) =>
  mutations.some((mutation) => {
    if (own && own.contains(mutation.target)) {
      return false;
    }

    const element = mutation.target instanceof Element ? mutation.target : mutation.target.parentElement;
    if (element?.closest("table, [role='table'], [role='grid']")) {
      return true;
    }

    const isRow = (node: Node) =>
      node instanceof Element && (node.matches("tr, [role='row']") || node.querySelector("tr, [role='row']") !== null);
    return Array.from(mutation.addedNodes).some(isRow) || Array.from(mutation.removedNodes).some(isRow);
  });

const Side = ({ side }: { side: RouteSide }) => {
  const text = side.isLabel ? <b>{side.name}</b> : <span>{side.name}</span>;
  return side.hover
    ? (
      <Tooltip tooltipText={side.hover} placement={TooltipPlacement.Bottom}>
        <span className="cstk-side">{text}</span>
      </Tooltip>
    )
    : <span className="cstk-side">{text}</span>;
};

/**
 * The line above a status list: this instance → the target, with a dot for what the list's last request
 * to the target got, a Feedback link, and once per version a line after the editor's first completed
 * sync. The dot asks the page (GetRouteState), which answers from memory: nothing is sent to the target.
 * It asks again whenever the list changes, and every few seconds while the editor's sync runs. See docs/specs/route-chip-and-feedback.md.
 */
export const RouteChip = ({ route }: { route: RouteProps }) => {
  const [state, setState] = useState<RouteState>({ target: "unknown", syncedRecently: false, syncRunning: false });
  const [dismissed, setDismissed] = useState(() => isDismissed(route.feedbackStorageKey));
  const root = useRef<HTMLDivElement>(null);

  const { execute: getRouteState } = usePageCommand<RouteState>("GetRouteState", {
    after: (result) => result && setState(result),
  });
  const getRef = useRef(getRouteState);
  getRef.current = getRouteState;

  useEffect(() => {
    getRef.current();

    let timer: number | undefined;
    const observer = new MutationObserver((mutations) => {
      if (changesList(mutations, root.current)) {
        window.clearTimeout(timer);
        timer = window.setTimeout(() => getRef.current(), 300);
      }
    });
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });

    return () => {
      observer.disconnect();
      window.clearTimeout(timer);
    };
  }, []);

  // While this user's sync runs, ask again until it finishes, so the line shows when it does, with
  // Kentico's Sync dialog still open or closed.
  useEffect(() => {
    if (!state.syncRunning) {
      return undefined;
    }

    const poll = window.setTimeout(() => getRef.current(), 5000);
    return () => window.clearTimeout(poll);
  }, [state]);

  const [dot, halo] = dotColors[state.target] ?? dotColors.unknown;
  const close = () => {
    dismiss(route.feedbackStorageKey);
    setDismissed(true);
  };
  const showAfterSync = state.syncedRecently && route.afterSyncUrl !== "" && !dismissed;

  return (
    <div ref={root} className="cstk-root">
      <style>{styles}</style>
      <div className="cstk-route">
        <span className="cstk-chip" aria-label={`Comparing ${route.this.name} with ${route.target.name}`}>
          <Side side={route.this} />
          <span className="cstk-arrow"><Icon name="xp-arrow-right" /></span>
          <span className="cstk-side">
            <Tooltip tooltipText={dotText(state.target, route.target.name)} placement={TooltipPlacement.Bottom}>
              <span className="cstk-dot" style={{ background: dot, boxShadow: `0 0 0 3px ${halo}` }} />
            </Tooltip>
            <Side side={route.target} />
          </span>
        </span>
        {route.feedbackUrl && (
          <a className="cstk-feedback" href={route.feedbackUrl} target="_blank" rel="noopener noreferrer">
            <Icon name="xp-bubble" />
            Feedback
          </a>
        )}
      </div>
      {showAfterSync && (
        <div className="cstk-after">
          <span className="cstk-check"><Icon name="xp-check" /></span>
          <span>
            Synced. How is the Content Sync Toolkit working for you?{" "}
            <a href={route.afterSyncUrl} target="_blank" rel="noopener noreferrer" onClick={close}>Tell us on GitHub</a>
          </span>
          <button type="button" className="cstk-close" aria-label="Don't show this again" onClick={close}>
            <Icon name="xp-times" />
          </button>
        </div>
      )}
    </div>
  );
};
