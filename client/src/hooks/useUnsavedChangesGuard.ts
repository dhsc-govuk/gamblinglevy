import { useEffect, useRef } from 'react';
import { stripBasePath } from '../helpers/authFailure';

interface Options {
  // Once the flow holds work worth keeping. Should be sticky: if it flipped back to false the sentinel
  // entry pushed below would still be in history with no listener left, so Back would need two presses.
  enabled: boolean;
  // The destination the user asked for, or null when the exit names none — the Back button, or a blocked
  // tab close. The caller falls back to the flow's own exit target in that case.
  onBlockedExit: (destination: string | null) => void;
}

// Everything about a click the decision below needs, lifted off the DOM by the listener. Kept as plain data
// so the rule can be exercised without a browser — the same reason helpers/authFailure.ts is a pure function
// rather than an interceptor.
export interface GuardedClick {
  href: string | null;
  linkTarget: string | null;
  hasDownload: boolean;
  button: number;
  // meta || ctrl || shift || alt — any of which means "open this somewhere else", not "leave this page".
  modified: boolean;
}

interface CurrentLocation {
  href: string;
  // `import.meta.env.BASE_URL`, with its trailing slash.
  basePath: string;
}

// The router-relative destination this click would leave for, or null when it is not an exit this guard has
// any business interrupting. Every rejection happens here, before the listener touches the event, so a click
// is never swallowed and then abandoned — a silently dead navigation link is worse than the lost work.
export const guardedDestination = (click: GuardedClick, current: CurrentLocation): string | null => {
  if (!click.href || click.hasDownload) {
    return null;
  }
  // A new tab leaves this one — and the wizard in it — exactly where it was. "Open Reports in a new tab so I
  // don't lose my place" is a strategy the user is entitled to, so modified and middle clicks pass too.
  // Deliberately NOT filtered on event.detail: activating a link with Enter fires a click with detail 0, and
  // excluding it would put the whole guard out of reach of keyboard users (WCAG 2.1.1).
  if (click.linkTarget && click.linkTarget !== '_self') {
    return null;
  }
  if (click.button !== 0 || click.modified) {
    return null;
  }

  let url: URL;
  let here: URL;
  try {
    url = new URL(click.href, current.href);
    here = new URL(current.href);
  } catch {
    return null;
  }

  // mailto:, tel: and friends never unload the document. A genuine cross-origin exit does, and beforeunload
  // already answers for it with the browser's own dialog — interrupting it here as well would ask twice.
  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    return null;
  }
  if (url.origin !== here.origin) {
    return null;
  }
  // Same pathname means the same <Route> element, so React reconciles rather than remounts and the wizard
  // keeps its useReducer state — nothing is lost, and interrupting would be wrong rather than merely noisy.
  // This one rule is what excludes the skip link, every error-summary link, and the wizard's own current-page
  // breadcrumb: <Link to="#"> does not render href="#", because React Router resolves a `to` with no pathname
  // against the current one (dropping the search), so it arrives here as "/…/activities/create#".
  if (url.pathname === here.pathname) {
    return null;
  }

  // navigate() is basename-aware and re-adds the prefix itself, but an anchor's resolved pathname always
  // carries it — handing one straight over doubles it into a 404. stripBasePath is the inverse the app
  // already uses for the other place a native pathname is fed back to the router.
  const normalisedBase = current.basePath.replace(/\/$/, '');
  const routerPath = stripBasePath(current.basePath, url.pathname);
  // stripBasePath deliberately returns the pathname untouched when it is not under the base — it protects the
  // "/foo" vs "/foobar/x" boundary that way. That answer is right for stripBasePath and wrong to navigate() to:
  // the prefix would simply be re-added, turning a same-origin link that lives OUTSIDE the SPA into an in-app
  // 404. On the DAC build the app is mounted under a path, so such links are possible. They are real document
  // navigations, which beforeunload already guards, so the guard has no business intercepting them. An in-app
  // path can never come back unchanged (the prefix is always removed), so an unchanged one means exactly this.
  if (normalisedBase !== '' && routerPath === url.pathname) {
    return null;
  }
  return `${routerPath}${url.search}${url.hash}`;
};

// A listener must be removed with the same capture flag it was added with, or it outlives the wizard and
// swallows anchor clicks on every page in the app. Neither tsc nor ESLint can see that mistake.
const CAPTURE = true;

// Guards a flow holding unsaved work against the three exits it would otherwise lose it to: the browser Back
// button, closing or reloading the tab, and any in-app link. The app mounts <BrowserRouter>, not a data
// router, so useBlocker / unstable_usePrompt throw at runtime and are not available — hence the history
// sentinel for Back, and the capture-phase click listener for links (BLUM-7639, then BLUM-7831).
const useUnsavedChangesGuard = ({ enabled, onBlockedExit }: Options): void => {
  // Held in a ref so the popstate effect depends on `enabled` alone. A callback rebuilt each render would
  // re-run that effect and push a fresh sentinel every time, and Back would then need one press per render.
  const onBlockedExitRef = useRef(onBlockedExit);
  onBlockedExitRef.current = onBlockedExit;

  useEffect(() => {
    if (!enabled) {
      return;
    }
    // The browser owns the wording; preventDefault plus a set returnValue is the whole of the API.
    const handleBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [enabled]);

  // Nothing can pop a sentinel back off the stack, so the effect below must never push a second one. It would
  // otherwise do exactly that under StrictMode, whose mount/unmount/mount leaves Back needing two presses in
  // development and nowhere else — the kind of difference that gets "fixed" in the wrong place.
  const sentinelPushedRef = useRef(false);
  // The URL the sentinel was pushed for, so a pop that lands somewhere else can be told apart from ours.
  const guardedHrefRef = useRef('');

  useEffect(() => {
    if (!enabled) {
      return;
    }
    // One sentinel entry, so the first Back press lands back on this same URL instead of leaving the flow.
    // The listener re-arms it and hands the exit to the caller, which is what keeps the page on screen.
    // The current state is carried onto the sentinel rather than replaced with null: React Router keeps its
    // history index in there, and an entry with no index leaves the router counting from null afterwards.
    if (!sentinelPushedRef.current) {
      guardedHrefRef.current = window.location.href;
      window.history.pushState(window.history.state, '', window.location.href);
      sentinelPushedRef.current = true;
    }
    const handlePopState = () => {
      // A multi-entry jump — history.go(-2), or picking an older entry from the Back button's menu — steps
      // straight past the sentinel, and the router has already navigated away by the time this runs. Re-arming
      // then would push a sentinel onto someone else's page, whose own Back would appear to do nothing. The
      // guard is simply lost in that case, which is as far as the History API allows it to reach.
      if (window.location.href !== guardedHrefRef.current) {
        return;
      }
      window.history.pushState(window.history.state, '', window.location.href);
      onBlockedExitRef.current(null);
    };
    window.addEventListener('popstate', handlePopState);
    return () => window.removeEventListener('popstate', handlePopState);
  }, [enabled]);

  // The third exit, and the one neither listener above can see: a React Router <Link> is a client-side push,
  // so it fires neither popstate nor beforeunload and the flow is left silently (BLUM-7831). Caught in the
  // capture phase on document because React 18 delegates its own events at the root container, a descendant
  // of document — so this runs first, and stopPropagation means the synthetic click is never dispatched and
  // Link's navigate() never runs. Unlike the sentinel this effect needs no once-only guard: add/remove is
  // idempotent, so StrictMode's double-mount leaves no residue.
  useEffect(() => {
    if (!enabled) {
      return;
    }
    const handleClick = (event: MouseEvent) => {
      // Not how in-page anchors are excluded, whatever it looks like: GovUKSkipLink and GovUKErrorSummary
      // both call preventDefault inside React handlers, which run long after this listener, so the flag is
      // false here for exactly those. It is only here for a future capture listener registered ahead of us.
      if (event.defaultPrevented) {
        return;
      }
      // The crown logo wraps an inline SVG, so the target is often an SVGPathElement — closest() is on
      // Element, not HTMLElement, and still finds the <a> ancestor.
      const target = event.target instanceof Element ? event.target.closest('a[href]') : null;
      if (!target) {
        return;
      }
      const destination = guardedDestination(
        {
          href: target.getAttribute('href'),
          linkTarget: target.getAttribute('target'),
          hasDownload: target.hasAttribute('download'),
          button: event.button,
          modified: event.metaKey || event.ctrlKey || event.shiftKey || event.altKey,
        },
        { href: window.location.href, basePath: import.meta.env.BASE_URL || '/' }
      );
      if (destination === null) {
        return;
      }
      // Both calls are load-bearing and neither is redundant. preventDefault is the only thing that stops a
      // plain <a href> loading a new document, and it is also what stops a <Link>: its handler ends with
      // `if (!event.defaultPrevented) internalOnClick(event)`, and React copies defaultPrevented from the
      // native event onto the synthetic one. stopPropagation then keeps any other onClick on the anchor from
      // firing side effects for a navigation that is not happening.
      event.preventDefault();
      event.stopPropagation();
      onBlockedExitRef.current(destination);
    };
    document.addEventListener('click', handleClick, CAPTURE);
    return () => document.removeEventListener('click', handleClick, CAPTURE);
  }, [enabled]);
};

export default useUnsavedChangesGuard;
