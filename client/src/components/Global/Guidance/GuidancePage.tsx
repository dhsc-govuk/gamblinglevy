import React, { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { H1, H2 } from '../../GovUKComponents';
import GuidanceContents from './GuidanceContents';
import { GuidanceSection } from './types';
import { composeTestId } from '../../../helpers/testIds';

interface Props {
  sections: GuidanceSection[];
  testId: string;
}

// Shared shell for the two audience-specific guidance pages (BLUM-7633). Both pages are the same shape —
// an H1, a contents list, then the sections — and differ only in the content they pass in, which is what
// keeps a DHSC user from ever seeing LA copy and the reverse.
const GuidancePage = ({ sections, testId }: Props): React.ReactElement => {
  const { hash } = useLocation();

  // Land a deep link on the section it names. Two separate things stop that happening on its own: the
  // browser resolves the fragment before React has rendered the section, so the target does not exist yet,
  // and useScrollToTop's mount-time layout effect then scrolls to the top regardless. A plain effect runs
  // after every layout effect, so this gets the last word. It also fires on the in-page contents links —
  // clicking one pushes a history entry, which updates the hash — which makes their focus move deterministic
  // rather than leaving it to the browser's fragment handling.
  useEffect(() => {
    if (!hash) return;
    const target = document.getElementById(hash.slice(1));
    if (!target) return;
    target.focus();
    target.scrollIntoView();
  }, [hash]);

  return (
    <div data-testid={testId}>
      <H1>Guidance</H1>
      <GuidanceContents sections={sections} testId={composeTestId(testId, 'contents')} />
      {sections.map((section) => (
        // tabIndex={-1} so a contents link moves keyboard focus into the section it jumps to rather than only
        // scrolling to it — the same idiom as the #main-content target in layouts/Management. Deliberately
        // *not* aria-labelledby: naming a <section> promotes it to a `region` landmark, and nine extra
        // landmarks inside <main> would bury the real ones in a screen reader's landmark list for no gain
        // the following H2 does not already provide.
        <section key={section.id} id={section.id} tabIndex={-1} className="govuk-!-margin-bottom-4" data-testid={composeTestId(testId, 'section', section.id)}>
          <H2>{section.title}</H2>
          {section.content}
        </section>
      ))}
    </div>
  );
};

export default GuidancePage;
