import React from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { Link as RouterLink } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { GovUKTaskList, GovUKInsetText, Paragraph, type TaskListItem } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { composeTestId } from '../../../helpers/testIds';
import { myReportCollectionsKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { formatGovUkDate } from '../../Admin/Collections/display';
import { getMyReportCollections } from '../queries';
import { collectionAuthoringHref, collectionDisplayName, overdueCollections, submissionTaskStatus } from '../display';
import { reportsLandingBreadcrumbs } from '../navigation';
import './styles.css';

// LA "Reports" landing (BLUM-6097). Lists the organisation's currently-open quarterly data collections with their
// reporting status — each links into the authoring flow, or into the read-only record once submitted — then any
// overdue periods (BLUM-7077), plus a link to the full report history. Org-scoped server-side.
const ReportsLanding = (): React.ReactElement => {
  usePageTitle('Reports');
  const { userRole } = useAuthProvider();

  const { data, isLoading } = useQuery({ queryKey: myReportCollectionsKey, queryFn: getMyReportCollections });
  // "Open data collections" = the organisation's currently-open windows, whether or not it has reported yet — each
  // row carries its own derived status (Not started / In progress / Submitted), so a submitted report is shown as
  // Submitted rather than silently dropped (BLUM-6952). It also stays under the report history, and leaves this list
  // once its window closes — from then on it is either history or, if nothing was submitted, overdue below.
  // Both submission flows are listed (quarterly reports and the annual maturity assessment, BLUM-7859).
  const openCollections = (data?.data ?? []).filter((collection) => collection.collectionStatus === 'open');

  const items: TaskListItem[] = openCollections.map((collection) => {
    // One predicate drives both the label and the destination, so a row can never read "Submitted" while still
    // linking into the authoring flow.
    const isSubmitted = collection.submissionStatus === 'completed';

    return {
      id: collection.collectionId,
      title: collectionDisplayName(collection),
      hint: `Deadline is ${formatGovUkDate(collection.submissionDeadline)}`,
      // Collection-scoped: the row opens its own collection. An organisation can have two quarterly windows open at
      // once, and a collection-agnostic link opened whichever one the server resolved instead (BLUM-6950). A submitted
      // report is immutable, so its row goes to the read-only record rather than the authoring flow.
      href: isSubmitted ? ROUTES.reports.submittedDetail(collection.collectionId) : collectionAuthoringHref(collection),
      status: submissionTaskStatus(collection.submissionStatus),
      // Stable testId from the collection id (not the date-derived title slug GovUKTaskList would otherwise fall back to).
      testId: composeTestId('la-reports-landing-collections', 'item', collection.collectionId),
    };
  });

  // Reporting periods whose deadline passed with nothing submitted. They are no longer "open", so they fell off this
  // list, and having never been submitted they were absent from the report history too — a missed deadline was
  // invisible everywhere (BLUM-7077). The window is shut (every write path is gated on SubmissionEligibility.IsOpen),
  // so each row goes to the read-only record rather than the authoring flow.
  const overdueItems: TaskListItem[] = overdueCollections(data?.data ?? []).map((collection) => ({
    id: collection.collectionId,
    title: collectionDisplayName(collection),
    hint: `Deadline was ${formatGovUkDate(collection.submissionDeadline)}`,
    href: ROUTES.reports.submittedDetail(collection.collectionId),
    status: submissionTaskStatus(collection.submissionStatus),
    testId: composeTestId('la-reports-landing-overdue', 'item', collection.collectionId),
  }));

  return (
    // The design (Figma 420:80824) shows heading sizes one step down from the app defaults (36px H1, 24px H2 —
    // govuk-heading-l/-m, not -xl/-l) and a 630px intro column. It also omits the beta phase banner, but that is
    // now global chrome on every page per BLUM-7060 AC1 (see BLUM-7102).
    <GeneralLayout breadcrumbs={reportsLandingBreadcrumbs(userRole)}>
      <div className="reports-landing" data-testid="la-reports-landing">
        <div className="reports-landing__overview">
          <h1 className="govuk-heading-l">Reports</h1>
          <div className="reports-landing__intro">
            <Paragraph>Select a data collection to create or continue working on your report.</Paragraph>
            <Paragraph>To view reports for past data collections, select ‘View your reports’.</Paragraph>
          </div>
        </div>

        <h2 className="govuk-heading-m reports-landing__heading">Open data collections</h2>
        <div className="reports-landing__collections">
          {isLoading ? (
            <PageLoading />
          ) : items.length === 0 ? (
            <GovUKInsetText data-testid="la-reports-landing-empty">
              {/* The reassurance is only true when nothing has been missed. Rendering "we'll email you when your next
                  report is due" directly above an "Overdue reports" list would contradict it, in the more prominent
                  of the two styles, on the screen BLUM-7077 exists to make the missed deadline visible on. */}
              {overdueItems.length > 0
                ? 'There are no open data collections right now. Reports you have missed are listed below.'
                : 'There are no open data collections right now. We’ll email you when your next report is due.'}
            </GovUKInsetText>
          ) : (
            <GovUKTaskList testId="la-reports-landing-collections" idPrefix="la-reports-landing-collections" items={items} />
          )}
        </div>

        {/* Only rendered when something is actually overdue — an LA that is up to date never sees the section. */}
        {!isLoading && overdueItems.length > 0 && (
          <>
            <h2 className="govuk-heading-m reports-landing__heading">Overdue reports</h2>
            <div className="reports-landing__collections">
              <Paragraph>The deadline for these data collections has passed. You can no longer submit these reports.</Paragraph>
              {/* GovUKTaskList derives its aria-describedby target ids from idPrefix, which defaults to "task-list" —
                  with two lists on the page both must namespace their own or they emit duplicate DOM ids and an
                  ambiguous description target (WCAG 4.1.1). Same idPrefix-per-list pattern as views/Reports/Tasks. */}
              <GovUKTaskList testId="la-reports-landing-overdue" idPrefix="la-reports-landing-overdue" items={overdueItems} />
            </div>
          </>
        )}

        <Paragraph className="reports-landing__submitted-link">
          <RouterLink to={ROUTES.reports.submitted} className="govuk-link" data-testid="la-reports-landing-view-submitted">
            View your reports
          </RouterLink>
        </Paragraph>
      </div>
    </GeneralLayout>
  );
};

export default ReportsLanding;
