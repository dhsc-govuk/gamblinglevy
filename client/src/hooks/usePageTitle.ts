import { useEffect } from 'react';

const SERVICE_NAME = 'Share progress preventing gambling harms';

// A detail page's title must name the page, not just the record shown on it: "Local Authority" alone tells a
// screen-reader user nothing about where they are (WCAG 2.2 SC 2.4.2, BLUM-7275). Centralised so the shape is
// decided once: the Admin > Organisations > Users wrappers used an en dash until BLUM-7275 routed them through
// here too, so the hyphen this hook appends is now the only separator in play. Keep it that way.
// `name` accepts null because several DTOs in this area declare it nullable (Admin/Collections/queries.ts) — the
// falsy branch already handles it, so the next caller has no reason to reach for `?? ''`.
export const pageTitleWithName = (context: string, name: string | null | undefined): string => (name ? `${context} - ${name}` : context);

const usePageTitle = (pageTitle?: string): void => {
  useEffect(() => {
    if (!pageTitle) return;
    document.title = `${pageTitle} - ${SERVICE_NAME} - GOV.UK`;
  }, [pageTitle]);
};

export default usePageTitle;
