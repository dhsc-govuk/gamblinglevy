import { lazy, type ComponentType } from 'react';
import { ChunkLoadError, clearChunkReloadMarker, recoverFromChunkFailure } from './chunkReload';

// Route-level code splitting (BLUM-8759). A "chunk" here is one dynamic import whose module is a set of view
// barrels — views/la.ts, views/dhsc.ts — so one audience's whole surface arrives in one request, rather than a
// request per route. Layouts render inside each view, so every chunk boundary replaces the entire page with a
// spinner; the grain is chosen so a signed-in user meets exactly one, and so a multi-step form never has a
// boundary (and its failure recovery, a reload) between two of its steps.

// `any` because the views of one barrel take different props; the barrel's own type is what callers see.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type ViewNamespace = Record<string, ComponentType<any>>;

export interface LazyChunk<TModule> {
  // Starts the download without rendering anything. Safe to call repeatedly, and silent on failure.
  preload: () => void;
  // The views of one barrel inside the chunk, as components that can be rendered before the chunk has loaded.
  views: <TViews extends ViewNamespace>(pick: (module: TModule) => TViews) => TViews;
}

// React.lazy asks its factory for a thenable and calls `then` straight away. One that calls back before
// returning leaves the component resolved by the time React looks, so it renders in the same pass. A real
// promise — even an already-resolved one — calls back a microtask later, which is a suspend: React would commit
// the fallback and the user would see a spinner flash the first time they visit each route, chunk loaded or not.
const resolved = <T>(value: T): Promise<T> => ({ then: (onFulfilled: (result: T) => unknown) => onFulfilled(value) } as unknown as Promise<T>);

// React.lazy keeps a failed load for good: the component rethrows the same error on every later render, even
// once the chunk has loaded for some other view. So a view whose load failed needs a new component — but not
// before the failure has reached the boundary. Dropping it as it fails would hand React a fresh component on
// its very next attempt to render the route, which would fetch, fail and be dropped again: a request loop that
// never shows the error page. They are remembered here instead and dropped when the boundary is reset, which
// is one new attempt per navigation.
const failedViews: Array<() => void> = [];

// For ChunkErrorBoundary's onReset.
export const forgetFailedViews = (): void => {
  failedViews.splice(0).forEach((forget) => forget());
};

export const lazyChunk = <TModule>(load: () => Promise<TModule>): LazyChunk<TModule> => {
  let loaded: TModule | undefined;
  let pending: Promise<TModule> | undefined;

  // One request however many views ask. A rejection is forgotten so the next caller asks again: a warm-up that
  // failed must not decide the outcome for the render that follows it.
  const fetchChunk = (): Promise<TModule> => {
    if (!pending) {
      pending = load()
        .then((module) => {
          // Vite's preload helper resolves undefined, rather than rejecting, if a vite:preloadError listener
          // calls preventDefault. There is no such listener today; this keeps one from becoming a blank page.
          if (!module) throw new ChunkLoadError();
          loaded = module;
          clearChunkReloadMarker();
          return module;
        })
        .catch((error: unknown) => {
          pending = undefined;
          throw error;
        });
    }
    return pending;
  };

  const preload = (): void => {
    fetchChunk().catch(() => undefined);
  };

  const views = <TViews extends ViewNamespace>(pick: (module: TModule) => TViews): TViews => {
    const components = new Map<string, ComponentType>();

    const lazyView = (name: string): ComponentType =>
      lazy(() => {
        if (loaded) return resolved({ default: pick(loaded)[name] });
        return fetchChunk().then(
          (module) => ({ default: pick(module)[name] }),
          (reason: unknown) =>
            recoverFromChunkFailure(reason).catch((error: unknown) => {
              failedViews.push(() => components.delete(name));
              throw error;
            })
        );
      });

    // A Proxy rather than a list of names: the barrel is the only place a view is declared, and a second list
    // here would be one more thing to forget. The type is the barrel's own, so `<Reports.Tasks />` in Routes.tsx
    // is checked by tsc exactly as it was when the barrel was imported statically. Each name keeps one component
    // — a new one per render would remount the route — unless its load fails (see failedViews).
    return new Proxy({} as TViews, {
      get: (_target, name) => {
        if (typeof name !== 'string') return undefined;
        let component = components.get(name);
        if (!component) {
          component = lazyView(name);
          components.set(name, component);
        }
        return component;
      },
    });
  };

  return { preload, views };
};
