import React, { useEffect, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQueryClient } from 'react-query';
import { ErrorSummary, GovUKButton, GovUKSummaryCard, H1, SummaryList } from '../../../../components/GovUKComponents';
import { ManagementLayout } from '../../../../layouts';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import usePageTitle from '../../../../hooks/usePageTitle';
import { localAuthoritiesListKey, localAuthorityKey, organisationUsersListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { updateLocalAuthority } from '../mutations';
import { organisationServerErrors } from '../errors';
import { createOrganisationUser, deleteOrganisationUser, updateOrganisationUser } from '../../../../components/Global/OrganisationUsers/api';
import { displayContactJobRole, roleForContact, splitFullName } from '../contactJobRole';
import { type EditOrganisationContactsState } from './Contacts';

interface ReviewErrors {
  submit?: string;
}

// The error summary's only key is 'submit', and a GDS summary link must resolve to a real element — an
// href="#submit" with no matching id left keyboard and screen-reader users with focus going nowhere.
// Server errors raised here belong to earlier steps, so the submit control is the closest real target.
const SUBMIT_BUTTON_ID = 'admin-local-authorities-edit-review-save';

const errorsAsSubmitError = (errors: Record<string, string | undefined>, fallbackMessage: string): ReviewErrors => {
  const message = Object.values(errors).find(Boolean);
  return { submit: message ?? fallbackMessage };
};

const LocalAuthoritiesEditReview = (): React.ReactElement => {
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const queryClient = useQueryClient();
  const state = location.state as EditOrganisationContactsState | null;

  const [errors, setErrors] = useState<ReviewErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}Check your answers`);
  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  useEffect(() => {
    if (!state?.organisation?.organisationId) {
      navigate(ROUTES.admin.organisations.edit(organisationId), { replace: true });
    }
  }, [state, organisationId, navigate]);

  const { mutate, isLoading } = useMutation(
    async (confirmOnsCodeChange: boolean) => {
      if (!state) throw new Error('Organisation details are missing.');

      await updateLocalAuthority(organisationId, {
        name: state.organisation.name,
        onsCode: state.organisation.onsCode || null,
        region: state.organisation.region,
        type: state.organisation.type,
        eCode: state.organisation.eCode,
        confirmOnsCodeChange,
      });

      for (const removedId of state.removedContactIds) {
        await deleteOrganisationUser(organisationId, removedId);
      }

      for (const contact of state.contacts) {
        const { firstName, lastName } = splitFullName(contact.fullName);
        const role = roleForContact(contact);
        const email = contact.email.trim().toLowerCase();

        if (contact.id) {
          // This form doesn't manage account status — preserve it, or editing an org would reactivate
          // a deactivated contact. Activation/deactivation lives on the organisation's Users screen.
          await updateOrganisationUser(organisationId, { id: contact.id, firstName, lastName, email, role, status: contact.originalStatus ?? 'active' });
        } else {
          await createOrganisationUser(organisationId, { firstName, lastName, email, role, status: 'active' });
        }
      }
    },
    {
      onSuccess: () => {
        queryClient.invalidateQueries(localAuthoritiesListKey());
        queryClient.invalidateQueries(localAuthorityKey(organisationId));
        queryClient.invalidateQueries(organisationUsersListKey(organisationId));
        navigate(ROUTES.admin.organisations.detail(organisationId), {
          replace: true,
          state: {
            banner: {
              title: 'Edits saved',
              body: `'${state?.organisation.name ?? 'The organisation'}' is now up to date.`,
            },
          },
        });
      },
      onError: (error: any) => {
        const status = error?.response?.status;
        const message = error?.response?.data?.toString() ?? 'Could not save the changes. Try again.';
        setSubmitAttempts((n) => n + 1);

        if (status === 409) {
          if (message.toLowerCase().includes('email')) {
            setErrors({ submit: 'A user with this email address already exists.' });
          } else if (message.startsWith('Organisation with ONS code')) {
            setErrors({ submit: 'An organisation with this ONS code already exists.' });
          } else if (message.startsWith('Organisation with name')) {
            setErrors({ submit: 'An organisation with this name already exists.' });
          } else {
            setErrors({ submit: 'Could not save the changes. Try again.' });
          }
          return;
        }

        setErrors(errorsAsSubmitError(organisationServerErrors(error?.response?.data, 'Could not save the changes. Try again.'), 'Could not save the changes. Try again.'));
      },
    }
  );

  if (!state?.organisation?.organisationId) return <></>;

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: key === 'submit' ? SUBMIT_BUTTON_ID : key, text: msg ?? '' }));

  const onsCodeChanged = state.organisation.onsCode !== state.organisation.originalOnsCode;

  const handleSave = () => {
    mutate(onsCodeChanged);
  };

  const goBackToDetails = () => navigate(ROUTES.admin.organisations.edit(organisationId), { state: state.organisation });

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.organisations.edit(organisationId), onClick: goBackToDetails }}>
      <div className="organisations-edit-page">
        {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} testId="admin-local-authorities-edit-review-error-summary" />}

        <div className="organisations-edit-page__heading">
          <span className="govuk-caption-l">Edit organisation</span>
          <H1>Check your answers</H1>
        </div>

        <GovUKSummaryCard
          title="Organisation details"
          actions={[
            {
              label: 'Edit',
              onClick: () => navigate(ROUTES.admin.organisations.edit(organisationId), { state: state.organisation }),
              visuallyHiddenText: 'organisation details',
              testId: 'admin-local-authorities-edit-review-edit-organisation',
            },
          ]}
          testId="admin-local-authorities-edit-review-organisation">
          <SummaryList
            noOuterBorder
            halfWidthColumns
            items={[
              { label: 'Name', value: state.organisation.name },
              { label: 'ONS code', value: state.organisation.onsCode || '—' },
              { label: 'Region', value: state.organisation.region },
              { label: 'Type', value: state.organisation.type },
              { label: 'E-code', value: state.organisation.eCode || '—' },
            ]}
            testId="admin-local-authorities-edit-review-organisation-details"
          />
        </GovUKSummaryCard>

        <GovUKSummaryCard
          title="Contacts"
          actions={[
            {
              label: 'Edit',
              onClick: () => navigate(ROUTES.admin.organisations.editContacts(organisationId), { state }),
              visuallyHiddenText: 'contacts',
              testId: 'admin-local-authorities-edit-review-edit-contacts',
            },
          ]}
          testId="admin-local-authorities-edit-review-contacts">
          <SummaryList
            noOuterBorder
            halfWidthColumns
            items={
              state.contacts.length > 0
                ? state.contacts.map((contact, index) => ({
                    label: `Contact person ${index + 1}`,
                    value: (
                      <span className="organisations-edit-page__review-contact">
                        <span>{contact.fullName}</span>
                        <span className="organisations-edit-page__review-muted">{displayContactJobRole(contact)}</span>
                        <a href={`mailto:${contact.email}`} className="govuk-link">
                          {contact.email}
                        </a>
                      </span>
                    ),
                  }))
                : [{ label: 'Contact person 1', value: 'No contact people have been added.' }]
            }
            testId="admin-local-authorities-edit-review-contacts-list"
          />
        </GovUKSummaryCard>

        <div className="govuk-button-group">
          <GovUKButton onClick={handleSave} id={SUBMIT_BUTTON_ID} isLoading={isLoading} disabled={isLoading} data-testid="admin-local-authorities-edit-review-save">
            Save
          </GovUKButton>
          <GovUKButton
            className="govuk-button govuk-button--secondary"
            onClick={() => navigate(ROUTES.admin.organisations.editDiscard(organisationId), { state })}
            disabled={isLoading}
            data-testid="admin-local-authorities-edit-review-discard">
            Discard edits
          </GovUKButton>
        </div>
      </div>
    </ManagementLayout>
  );
};

export default LocalAuthoritiesEditReview;
