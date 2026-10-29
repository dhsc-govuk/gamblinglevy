import React from 'react';
import { GovUKButton } from '../../components/GovUKComponents';
import { SERVICE_PROBLEM_HEADING } from '../../helpers/constants';
import { composeTestId } from '../../helpers/testIds';
import PublicMessageLayout from '../PublicMessage';

const TEST_ID = 'page-load-error';

/**
 * Shown when the code for a page could not be downloaded and reloading has not fixed it, or could not be tried
 * (BLUM-8759; see helpers/chunkReload). ChunkErrorBoundary renders it in place of the whole route table, above
 * every view, so no view's layout is there to supply a header or footer. It uses PublicMessageLayout's
 * role-free chrome rather than GeneralLayout's because that navigation is role-driven and may be part of what
 * failed to load.
 *
 * Two things differ from the other pages on that layout. It replaces a page that was already on screen with no
 * route change, so it asks for focus to be moved. And its control reloads, rather than signing out or retrying
 * in place: React.lazy remembers a failed load for the life of the document, and after a deploy the file this
 * document is asking for no longer exists at any URL it knows; only a new index.html does.
 */
const PageLoadError = (): React.ReactElement => (
  <PublicMessageLayout
    title={SERVICE_PROBLEM_HEADING}
    paragraphs={['This page could not be loaded.', 'Check your internet connection and try again. If the problem continues, try again later.']}
    testId={TEST_ID}
    focusOnMount
    action={
      <GovUKButton onClick={() => window.location.reload()} data-testid={composeTestId(TEST_ID, 'retry')}>
        Try again
      </GovUKButton>
    }
  />
);

export default PageLoadError;
