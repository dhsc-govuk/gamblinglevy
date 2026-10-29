import React, { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { composeTestId } from '../../../../helpers/testIds';
import { httpDataOf, httpMessageOf, httpStatusOf } from '../../../../helpers/httpErrors';
import { ErrorSummary, GovUKButton, GovUKSummaryCard, H1, SummaryList } from '../../../../components/GovUKComponents';
import { ManagementLayout } from '../../../../layouts';
import usePageTitle from '../../../../hooks/usePageTitle';
import { localAuthoritiesListKey, localAuthorityKey, organisationUsersListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { formatGBP } from '../../../../helpers/currency';
import { financialYearLabel, nextFinancialYear } from '../../../../helpers/financialYear';
import { createOrganisationUser } from '../../Users/mutations';
import { createLocalAuthority } from '../mutations';
import { organisationServerErrors } from '../errors';
import { displayContactJobRole, roleForContact, splitFullName } from '../contactJobRole';
import { type CreateOrganisationContactsState } from './Contacts';
import { ACTIVATE_IMMEDIATELY_LABELS } from './index';
import './styles.css';

interface ReviewErrors {
  submit?: string;
}

// The error summary's only key is 'submit', and a GDS summary link must resolve to a real element — an
// href="#submit" with no matching id left keyboard and screen-reader users with focus going nowhere.
// Server errors raised here belong to earlier steps, so the submit control is the closest real target.
const SUBMIT_BUTTON_ID = 'admin-organisations-create-review-submit';

const errorsAsSubmitError = (errors: Record<string, string | undefined>, fallbackMessage: string): ReviewErrors => {
  const message = Object.values(errors).find(Boolean);
  return { submit: message ?? fallbackMessage };
};

const LocalAuthoritiesCreateReview = (): React.ReactElement => {
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const state = location.state as CreateOrganisationContactsState | null;

  const [errors, setErrors] = useState<ReviewErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}Check your answers`);

  useEffect(() => {
    if (!state?.organisation?.name) {
      navigate(ROUTES.admin.organisations.create, { replace: true });
    }
  }, [state, navigate]);

  const { mutate, isPending } = useMutation({
    mutationFn: async () => {
      if (!state) throw new Error('Organisation details are missing.');

      const organisationResponse = await createLocalAuthority({
        name: state.organisation.name,
        onsCode: state.organisation.onsCode || null,
        region: state.organisation.region,
        type: state.organisation.type,
        eCode: state.organisation.eCode,
        allocatedAmount: Number(state.organisation.allocatedAmount),
        financialYear: state.organisation.financialYear,
        nextYearAllocatedAmount: state.organisation.nextYearAllocatedAmount ? Number(state.organisation.nextYearAllocatedAmount) : null,
        // '' (not answered) is sent as null so the API rejects it, rather than as false - which would create the
        // organisation deactivated without anyone having chosen that.
        activateImmediately: state.organisation.activateImmediately === '' ? null : state.organisation.activateImmediately === 'now',
      });

      // Contacts follow the status the API actually gave the organisation, not the radio: "Leave it deactivated"
      // (BLUM-7945) creates it suspended and its users must be deactivated too, as deactivating an organisation
      // does (BLUM-7667). Their invitations are held by the API until each is reactivated.
      const contactsActive = organisationResponse.data.onboardingStatus !== 'suspended';

      for (const contact of state.contacts) {
        const { firstName, lastName } = splitFullName(contact.fullName);
        await createOrganisationUser({
          organisationId: organisationResponse.data.id,
          firstName,
          lastName,
          email: contact.email.trim().toLowerCase(),
          role: roleForContact(contact),
          isActive: contactsActive,
        });
      }

      return organisationResponse;
    },
    onSuccess: (response) => {
      queryClient.invalidateQueries({ queryKey: localAuthoritiesListKey() });
      queryClient.invalidateQueries({ queryKey: localAuthorityKey(response.data.id) });
      queryClient.invalidateQueries({ queryKey: organisationUsersListKey(response.data.id) });
      navigate(ROUTES.admin.organisations.detail(response.data.id), {
        replace: true,
        state: {
          banner: {
            title: 'Organisation',
            body: `'${state?.organisation.name ?? 'The organisation'}' has been added to your list of organisations. ${
              response.data.onboardingStatus === 'suspended'
                ? 'The account and its users are deactivated. You can reactivate the account at any time. Each user will be sent their invitation when you reactivate them.'
                : 'You can view, edit, or delete the organisation account at any time.'
            }`,
          },
        },
      });
    },
    onError: (error) => {
      const status = httpStatusOf(error);
      const message = httpMessageOf(error) ?? 'Could not create the organisation. Try again.';
      setSubmitAttempts((n) => n + 1);

      if (status === 409) {
        if (message.toLowerCase().includes('email')) {
          setErrors({ submit: 'A user with this email address already exists.' });
        } else if (message.startsWith('Organisation with ONS code')) {
          setErrors({ submit: 'An organisation with this ONS code already exists.' });
        } else if (message.startsWith('Organisation with name')) {
          setErrors({ submit: 'An organisation with this name already exists.' });
        } else {
          setErrors({ submit: 'Could not create the organisation. Try again.' });
        }
        return;
      }

      setErrors(errorsAsSubmitError(organisationServerErrors(httpDataOf(error), 'Could not create the organisation. Try again.'), 'Could not create the organisation. Try again.'));
    },
  });

  if (!state?.organisation?.name) return <></>;

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: key === 'submit' ? SUBMIT_BUTTON_ID : key, text: msg ?? '' }));

  const goBackToContacts = () => navigate(ROUTES.admin.organisations.createContacts, { state });

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.organisations.createContacts, onClick: goBackToContacts }}>
      <div className="organisations-create-page organisations-create-page--review">
        <ErrorSummary heading="There is a problem" errors={errorList} testId="admin-organisations-create-review-error-summary" focusTrigger={submitAttempts} />

        <div className="organisations-create-page__heading organisations-create-page__heading--review">
          <span className="govuk-caption-l">Add organisation</span>
          <H1>Check your answers</H1>
        </div>

        <GovUKSummaryCard
          title="Organisation details"
          actions={[
            {
              label: 'Edit',
              onClick: () => navigate(ROUTES.admin.organisations.create, { state }),
              visuallyHiddenText: 'organisation details',
              testId: 'admin-organisations-create-review-edit-organisation',
            },
          ]}
          testId="admin-organisations-create-review-organisation">
          <SummaryList
            noOuterBorder
            halfWidthColumns
            items={[
              { label: 'Name', value: state.organisation.name },
              { label: 'ONS code', value: state.organisation.onsCode || '—' },
              { label: 'Region', value: state.organisation.region },
              { label: 'E-code', value: state.organisation.eCode || '—' },
              // Static labels, matching the organisation detail page. SummaryList derives its row testids from
              // the label, so putting the year there would move every selector each April.
              {
                label: 'Annual allocation',
                value: `${formatGBP(state.organisation.allocatedAmount ? Number(state.organisation.allocatedAmount) : null)} (${financialYearLabel(
                  state.organisation.financialYear
                )})`,
              },
              {
                label: 'Annual allocation (upcoming year)',
                value: `${formatGBP(state.organisation.nextYearAllocatedAmount ? Number(state.organisation.nextYearAllocatedAmount) : null)} (${financialYearLabel(
                  nextFinancialYear(state.organisation.financialYear)
                )})`,
              },
              {
                label: 'When do you want to activate this account?',
                value: state.organisation.activateImmediately === 'later' ? ACTIVATE_IMMEDIATELY_LABELS.later : ACTIVATE_IMMEDIATELY_LABELS.now,
              },
            ]}
            testId="admin-organisations-create-review-organisation-details"
          />
        </GovUKSummaryCard>

        <GovUKSummaryCard
          title="Contacts"
          actions={[
            {
              label: 'Edit',
              onClick: () => navigate(ROUTES.admin.organisations.createContacts, { state }),
              visuallyHiddenText: 'contacts',
              testId: 'admin-organisations-create-review-edit-contacts',
            },
          ]}
          testId="admin-organisations-create-review-contacts">
          <SummaryList
            noOuterBorder
            halfWidthColumns
            items={state.contacts.map((contact, index) => ({
              label: `Contact person ${index + 1}`,
              value: (
                <span className="organisations-create-page__review-contact">
                  <span>{contact.fullName}</span>
                  <span className="organisations-create-page__review-muted">{displayContactJobRole(contact)}</span>
                  <a href={`mailto:${contact.email}`} className="govuk-link" data-testid={composeTestId('admin-organisations-create-review', 'contact', index + 1, 'email')}>
                    {contact.email}
                  </a>
                </span>
              ),
            }))}
            testId="admin-organisations-create-review-contacts-list"
          />
        </GovUKSummaryCard>

        <GovUKButton type="button" onClick={() => mutate()} id={SUBMIT_BUTTON_ID} isLoading={isPending} disabled={isPending} data-testid="admin-organisations-create-review-submit">
          Save organisation and finish
        </GovUKButton>
      </div>
    </ManagementLayout>
  );
};

export default LocalAuthoritiesCreateReview;
