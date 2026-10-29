import React from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { ROUTES } from '../../../helpers/routes';

// Matches the renamed path segment on its own boundary, case-insensitively. React Router compiles route paths
// with the "i" flag unless caseSensitive is set, so /Key-Results reaches this component too — and a
// case-sensitive string replace would leave the path untouched, making Navigate target the URL it is already
// on. Navigate memoises its target, so that renders nothing at all: a blank page with no layout and no 404.
const LEGACY_SEGMENT = /\/key-results(?=\/|$)/i;

// Rewrites pre-BLUM-7563 bookmarks of the activity-results pages, whose path segment was /key-results before
// "key results" was removed from the user-facing language. The collection and activity ids sit either side of
// the segment, so replacing it in place preserves the deep link instead of dropping the user on the Reports
// landing the way the generic /reports/quarterly/* fallback would. A trailing splat matches zero segments in
// react-router v6, so this one route covers the picker and all three per-activity pages. Anything that reaches
// here without the segment cannot be rewritten — fall back to the landing rather than navigate to self. The
// fallback drops search/hash on purpose: they addressed a page the user is no longer being sent to, so a
// collection-scoped query string would be meaningless (and potentially misleading) on the Reports landing.
const KeyResultsLegacyRedirect = (): React.ReactElement => {
  const { pathname, search, hash } = useLocation();
  const target = pathname.replace(LEGACY_SEGMENT, '/results');
  return <Navigate to={target === pathname ? ROUTES.reports.list : `${target}${search}${hash}`} replace />;
};

export default KeyResultsLegacyRedirect;
