import React, { useEffect, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import {
  DeleteConfirmDialog,
  ErrorSummary,
  GovUKButton,
  GovUKInsetText,
  GovUKNotificationBanner,
  GovUKTable,
  GovUKTag,
  GovUKWarningText,
  H1,
  PageLoading,
  Paragraph,
  type Column,
} from '../../../components/GovUKComponents';
import { homeLinkForRole, useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { composeTestId } from '../../../helpers/testIds';
import { formatDateTime } from '../../../helpers/dateDisplay';
import { mySessionsKey } from '../../../helpers/queriesParams';
import { getMySessions, SessionDto } from './queries';
import { revokeSession } from './mutations';

const TEST_ID = 'account-sessions';

// A real DOM id, not just a testid: the error summary renders <a href="#…"> and moves focus to the element
// with this id, so it has to exist or the link is a dead fragment.
const LIST_ANCHOR_ID = 'account-sessions-list';

// Sessions created before BLUM-7241 added the columns carry an empty string rather than a value.
const NOT_RECORDED = 'Not recorded';
const orNotRecorded = (value: string) => (value.trim().length > 0 ? value : NOT_RECORDED);

// What a screen reader should hear a row's button as. Five "Sign out" buttons are indistinguishable
// otherwise, and "Sign out Not recorded" is worse than useless — fall back to the IP, which is always set
// for anything created since the migration.
const describeDevice = (session: SessionDto) => (session.deviceLabel.trim().length > 0 ? session.deviceLabel : orNotRecorded(session.ipAddress));

// Built per render rather than at module scope because the action column has to close over the revoke
// handler; useMemo in the component keeps it from being rebuilt on every render.
const buildColumns = (onRevoke: (session: SessionDto) => void): Column<SessionDto>[] => [
  {
    key: 'deviceLabel',
    header: 'Device',
    render: (session) => (
      <>
        {orNotRecorded(session.deviceLabel)}
        {session.isCurrent && (
          <>
            {' '}
            <GovUKTag colour="green" data-testid={composeTestId(TEST_ID, 'row', session.id, 'current')}>
              This device
            </GovUKTag>
          </>
        )}
      </>
    ),
  },
  {
    key: 'ipAddress',
    header: 'IP address',
    render: (session) => orNotRecorded(session.ipAddress),
  },
  {
    key: 'createdAt',
    header: 'Signed in',
    render: (session) => formatDateTime(session.createdAt),
  },
  {
    key: 'lastActivityAt',
    header: 'Last active',
    render: (session) => formatDateTime(session.lastActivityAt),
  },
  {
    key: 'actions',
    header: 'Action',
    align: 'right',
    // The caller's own row gets no button. Ending the session you are reading this in is just signing out,
    // and the header's Sign out button already does that — offering it here would mostly produce accidents.
    render: (session) =>
      session.isCurrent ? null : (
        <GovUKButton
          className="govuk-button govuk-button--secondary govuk-!-margin-bottom-0"
          onClick={() => onRevoke(session)}
          data-testid={composeTestId(TEST_ID, 'row', session.id, 'revoke')}>
          Sign out<span className="govuk-visually-hidden"> {describeDevice(session)}</span>
        </GovUKButton>
      ),
  },
];

// "Where you're signed in" (BLUM-7241 / CWE-613, AC2–AC4). The pen test's finding was that a user had no way
// to see — let alone end — a second session running alongside theirs, so an account compromise could go
// unnoticed indefinitely. This screen is the visibility half; the emailed new-sign-in alert is the other.
const AccountSessions = (): React.ReactElement => {
  const { userRole } = useAuthProvider();
  const queryClient = useQueryClient();
  const bannerRef = useRef<HTMLDivElement>(null);

  const [pendingRevoke, setPendingRevoke] = useState<SessionDto | null>(null);
  const [signedOutDevice, setSignedOutDevice] = useState<string | null>(null);
  const [revokeCount, setRevokeCount] = useState(0);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [errorAttempts, setErrorAttempts] = useState(0);

  usePageTitle(errorMessage ? 'Error: Where you’re signed in' : 'Where you’re signed in');

  // Deliberately not on defaultStaleTime: a stale session list is the one thing this screen must never show,
  // because the user acts on it to shut somebody out.
  const { data, isLoading, isError } = useQuery(mySessionsKey, getMySessions);
  const sessions = data?.data ?? [];

  const { mutate, isLoading: isRevoking } = useMutation(revokeSession, {
    onSuccess: (_response, sessionId) => {
      const revoked = sessions.find((session) => session.id === sessionId);
      setPendingRevoke(null);
      setErrorMessage(null);
      setSignedOutDevice(revoked ? describeDevice(revoked) : NOT_RECORDED);
      setRevokeCount((count) => count + 1);
      queryClient.invalidateQueries(mySessionsKey);
    },
    onError: () => {
      setPendingRevoke(null);
      setSignedOutDevice(null);
      setErrorMessage('Could not sign that device out. Try again.');
      setErrorAttempts((attempts) => attempts + 1);
    },
  });

  // The button that opened the dialog belongs to the row that has just been removed, so MUI's restore-focus
  // target is detached by the time the dialog closes and focus would silently fall back to <body>. Move it
  // onto the confirmation instead. Keyed on the counter, not the device name, so two revocations of
  // identically-named devices still each move focus.
  useEffect(() => {
    if (revokeCount > 0) {
      bannerRef.current?.focus();
    }
  }, [revokeCount]);

  const columns = useMemo(() => buildColumns(setPendingRevoke), []);

  const errors = errorMessage ? [{ targetName: LIST_ANCHOR_ID, text: errorMessage }] : [];

  return (
    <GeneralLayout breadcrumbs={[{ label: 'Home', link: homeLinkForRole(userRole) }]}>
      <div data-testid={`${TEST_ID}-page`}>
        <ErrorSummary errors={errors} focusTrigger={errorAttempts} testId={`${TEST_ID}-error-summary`} />

        {signedOutDevice && (
          <div ref={bannerRef} tabIndex={-1}>
            <GovUKNotificationBanner type="success" title="Device signed out" testId={`${TEST_ID}-banner`}>
              <Paragraph>{signedOutDevice} has been signed out of your account.</Paragraph>
            </GovUKNotificationBanner>
          </div>
        )}

        <div className="govuk-grid-row">
          <div className="govuk-grid-column-two-thirds">
            <H1>Where you&rsquo;re signed in</H1>
            <Paragraph>These are the devices currently signed in to your account. If you do not recognise one, sign it out and tell your administrator.</Paragraph>
            <Paragraph>Signing a device out takes effect immediately. Whoever is using it will have to sign in again to do anything.</Paragraph>
          </div>
        </div>

        <div id={LIST_ANCHOR_ID} tabIndex={-1}>
          {/* Shown alongside the list, never instead of it. React Query v3 keeps `data` when a refetch fails,
              and the refetch most likely to fail here is the one triggered by a successful revoke — so
              replacing the table would take away the remaining "Sign out" buttons at the exact moment
              somebody is working through an intruder's devices.

              The live region is mounted permanently and only its contents change, which is what makes it
              announce (WCAG 2.2 SC 4.1.3): a region and its text appearing in the same commit is the variant
              several screen readers drop. Polite, not assertive — a revoke that just succeeded has already
              moved focus to a role="alert" banner, and two assertive announcements would collide. Without
              this the user is told "Device signed out" and then left working a list we know may be stale. */}
          <div role="status" aria-live="polite" aria-atomic="true">
            {isError && sessions.length > 0 && (
              <GovUKWarningText data-testid={`${TEST_ID}-refresh-error`}>
                We could not check for changes just now, so this list may be out of date. Reload the page to see the latest.
              </GovUKWarningText>
            )}
          </div>

          {isLoading ? (
            <PageLoading testId={`${TEST_ID}-loading`} />
          ) : isError && sessions.length === 0 ? (
            // Never fall through to the empty state here. Someone who opened this page to look for an
            // intruder would be told there are no other devices — the most reassuring possible answer — when
            // the truth is we could not find out. Say we could not find out.
            <GovUKWarningText data-testid={`${TEST_ID}-load-error`}>
              We could not load your signed-in devices. This does not mean there are none. Reload the page, and if it keeps failing tell your administrator.
            </GovUKWarningText>
          ) : sessions.length === 0 ? (
            <GovUKInsetText data-testid={`${TEST_ID}-no-sessions`}>We have no record of any signed-in devices for your account.</GovUKInsetText>
          ) : (
            // searchable is on by default, and with no names to search it would paint a "Search by name"
            // box that filters on raw ids and timestamps.
            <GovUKTable<SessionDto> testId={TEST_ID} data={sessions} columns={columns} keyExtractor={(session) => session.id} searchable={false} />
          )}
        </div>

        {pendingRevoke && (
          <DeleteConfirmDialog
            open
            testId={`${TEST_ID}-revoke-dialog`}
            title="Sign this device out?"
            message="Whoever is using this device will be signed out straight away and will need to sign in again."
            itemName={`${orNotRecorded(pendingRevoke.deviceLabel)} — ${orNotRecorded(pendingRevoke.ipAddress)}`}
            confirmLabel="Sign out"
            isLoading={isRevoking}
            onCancel={() => setPendingRevoke(null)}
            onConfirm={() => mutate(pendingRevoke.id)}
          />
        )}
      </div>
    </GeneralLayout>
  );
};

export default AccountSessions;
