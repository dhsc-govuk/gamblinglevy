import { useLayoutEffect } from 'react';
import { useLocation } from 'react-router-dom';
import scrollToTop from '../helpers/scrollToTop';

// Resets window scroll to the top on every route change. Depends on pathname only (not search/hash) so
// query-param updates and in-page hash anchors don't fight the scroll. useLayoutEffect runs before paint
// to avoid a flash of the new page at the previous scroll position.
const useScrollToTop = (): void => {
  const { pathname } = useLocation();

  useLayoutEffect(() => {
    scrollToTop();
  }, [pathname]);
};

export default useScrollToTop;
