import React from 'react';
import { ChunkLoadError } from '../../../helpers/chunkReload';

interface ChunkErrorBoundaryProps {
  // Changes on every navigation (location.key). A failed load is not a property of the app, only of the page
  // that needed it, so moving to a page that is already loaded must clear the error.
  resetKey: string;
  // The page to show in place of everything inside the boundary. Supplied by the caller rather than imported
  // here because it needs a header and footer, and those live in layouts/, which imports this folder.
  fallback: React.ReactNode;
  // Called just before the error is cleared, so the caller can discard whatever made the children fail.
  onReset?: () => void;
  children: React.ReactNode;
}

interface ChunkErrorBoundaryState {
  hasError: boolean;
  error: unknown;
}

// The only error boundary in the app, and deliberately a narrow one (BLUM-8759). React.lazy rethrows a failed
// load on every render, and with no boundary that unmounts the whole tree: a white page. This catches that one
// case and shows the fallback page.
//
// Everything else is thrown again. An error a boundary handles never reaches window.onerror, which is where
// Sentry and App Insights pick up render errors today, so swallowing them here would silently stop them being
// reported. Widening this into a general boundary is a decision about telemetry, not a tidy-up.
class ChunkErrorBoundary extends React.Component<ChunkErrorBoundaryProps, ChunkErrorBoundaryState> {
  state: ChunkErrorBoundaryState = { hasError: false, error: null };

  static getDerivedStateFromError(error: unknown): ChunkErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidUpdate(previous: ChunkErrorBoundaryProps): void {
    if (this.state.hasError && previous.resetKey !== this.props.resetKey) {
      this.props.onReset?.();
      this.setState({ hasError: false, error: null });
    }
  }

  render(): React.ReactNode {
    if (!this.state.hasError) return this.props.children;
    if (!(this.state.error instanceof ChunkLoadError)) throw this.state.error;
    return this.props.fallback;
  }
}

export default ChunkErrorBoundary;
