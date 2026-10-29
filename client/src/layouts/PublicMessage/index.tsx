import React, { useEffect, useRef, useState } from 'react';
import usePageTitle from '../../hooks/usePageTitle';
import { useAuthProvider } from '../../components/AuthProvider';
import { GovUKButton, GovUKHeader, GovUKSkipLink, H1, Paragraph } from '../../components/GovUKComponents';
import { composeTestId } from '../../helpers/testIds';
import LayoutFooter from '../General/LayoutFooter';

interface Props {
  /** Doubles as the page title and the H1 — on a dead-end page the two are always the same sentence. */
  title: string;
  /** One entry per paragraph of body copy. */
  paragraphs: string[];
  testId: string;
  /**
   * The page's one control, in place of "Sign out", for a page whose way forward is something else. The layout
   * cannot give it a testid, so the caller must: composeTestId(testId, …) on a GovUKButton.
   */
  action?: React.ReactNode;
  /**
   * Move focus to the content when the page appears. For a page that replaces one already on screen with no
   * route change: without it a screen reader says nothing and a keyboard user's next Tab starts in a page whose
   * landmarks have all been swapped (the NotFound / NotAuthorised / UserLoadFailed convention). The two pages
   * that are only ever reached by a full page load leave it off.
   */
  focusOnMount?: boolean;
}

// The default control. Its own component so that only a page which shows it reads the auth context: the layout is
// also the page an error boundary falls back to (layouts/PageLoadError), a throw inside a boundary's own fallback
// is not caught by that boundary, and useAuthProvider throws outside its provider — a blank page where the error
// page should be.
const SignOutButton = ({ testId }: { testId: string | undefined }): React.ReactElement => {
  const { signOut } = useAuthProvider();
  const [signingOut, setSigningOut] = useState(false);

  const handleSignOut = () => {
    setSigningOut(true);
    // signOut ends in logoutRedirect, which rejects if the redirect never leaves the page. Without this the
    // rejection is unhandled and signingOut latches on, disabling the only control the page has.
    signOut().catch(() => setSigningOut(false));
  };

  return (
    <GovUKButton onClick={handleSignOut} isLoading={signingOut} data-testid={testId}>
      Sign out
    </GovUKButton>
  );
};

/**
 * The shared chrome for a dead-end page that cannot use GeneralLayout: a heading, some copy and one control,
 * between the GOV.UK header and the footer. For views/Public/AccountDeactivated and
 * views/Public/OrganisationSuspended the user is authenticated with Microsoft but the service has refused them,
 * there is nothing to retry, and the control is "Sign out" — the default. layouts/PageLoadError passes its own
 * (BLUM-8759).
 *
 * Extracted because the two were line-for-line copies that had already drifted (BLUM-6386): only one of them
 * got the govuk-frontend v6 header fix, and only one handled a rejected signOut. Both defects were invisible
 * precisely because each page looked self-contained. A third page with this shape takes the props above rather
 * than a third copy of the markup, for the same reason.
 *
 * Deliberately not GeneralLayout: that one renders role-driven navigation and breadcrumbs, and the caller
 * here has no role claim to drive them with.
 */
const PublicMessageLayout = ({ title, paragraphs, testId, action, focusOnMount = false }: Props): React.ReactElement => {
  usePageTitle(title);
  const mainRef = useRef<HTMLElement>(null);

  useEffect(() => {
    if (focusOnMount) mainRef.current?.focus();
  }, [focusOnMount]);

  return (
    <div data-testid={testId}>
      <GovUKSkipLink />

      <GovUKHeader testId={composeTestId(testId, 'header')} />

      <main ref={mainRef} tabIndex={focusOnMount ? -1 : undefined} className="govuk-width-container" id="main-content" role="main">
        <div className="govuk-main-wrapper">
          <div className="govuk-grid-row">
            <div className="govuk-grid-column-two-thirds">
              <H1 data-testid={composeTestId(testId, 'heading')}>{title}</H1>
              {/* Index key, not the copy string: the array is a static caller-supplied literal that never
                  reorders or filters, and two identical paragraphs would collide on a text key. */}
              {paragraphs.map((paragraph, index) => (
                <Paragraph key={index}>{paragraph}</Paragraph>
              ))}
              {action !== undefined ? action : <SignOutButton testId={composeTestId(testId, 'sign-out')} />}
            </div>
          </div>
        </div>
      </main>

      <LayoutFooter />
    </div>
  );
};

export default PublicMessageLayout;
