import React, { useEffect, useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { useQuery } from 'react-query';
import { PageLoading } from '../../../../components/GovUKComponents';
import { ManagementLayout } from '../../../../layouts';
import { GovUKButton, GovUKFieldset, ErrorSummary, H1, H2, Paragraph } from '../../../../components/GovUKComponents';
import { EnumUserRole, ROLE_LABELS } from '../../../../components/AuthProvider';
import useErrorSummaryFocus from '../../../../hooks/useErrorSummaryFocus';
import usePageTitle from '../../../../hooks/usePageTitle';
import { organisationUsersListKey } from '../../../../helpers/queriesParams';
import { ROUTES } from '../../../../helpers/routes';
import { validateEmailAddress } from '../../../../helpers/validators';
import { getOrganisationUserList, type OrganisationUserDto } from '../../../../components/Global/OrganisationUsers/api';
import { JOB_ROLE_OPTIONS, JOB_ROLE_OTHER, splitFullName } from '../contactJobRole';
import type { EditOrganisationDetailsState } from './index';
import './styles.css';

interface ContactErrors {
  [key: string]: string | undefined;
}

export interface ContactForm {
  id?: string;
  originalRole?: string;
  originalStatus?: OrganisationUserDto['status'];
  fullName: string;
  jobRole: string;
  jobRoleTitle: string;
  email: string;
}

export interface EditOrganisationContactsState {
  organisation: EditOrganisationDetailsState;
  contacts: ContactForm[];
  removedContactIds: string[];
}

const contactFromUser = (user: OrganisationUserDto): ContactForm => ({
  id: user.id,
  originalRole: user.role,
  originalStatus: user.status,
  fullName: `${user.firstName} ${user.lastName}`.trim(),
  jobRole: user.role === EnumUserRole.ORGANISATION_ADMIN ? EnumUserRole.ORGANISATION_ADMIN : JOB_ROLE_OTHER,
  jobRoleTitle: user.role === EnumUserRole.ORGANISATION_ADMIN ? '' : ROLE_LABELS[user.role] ?? user.role,
  email: user.email,
});

const LocalAuthoritiesEditContacts = (): React.ReactElement => {
  const { organisationId = '' } = useParams<{ organisationId: string }>();
  const navigate = useNavigate();
  const location = useLocation();

  const state = location.state as EditOrganisationDetailsState | EditOrganisationContactsState | null;
  const organisationState = state && 'organisation' in state ? state.organisation : state;
  const incomingContacts = state && 'contacts' in state ? state.contacts : undefined;
  const incomingRemovedIds = state && 'removedContactIds' in state ? state.removedContactIds : undefined;

  const { data: usersData, isLoading: loadingUsers } = useQuery(organisationUsersListKey(organisationId), () => getOrganisationUserList(organisationId), {
    enabled: !!organisationId && !incomingContacts,
  });

  const [contacts, setContacts] = useState<ContactForm[]>(incomingContacts ?? []);
  const [removedContactIds, setRemovedContactIds] = useState<string[]>(incomingRemovedIds ?? []);
  const [errors, setErrors] = useState<ContactErrors>({});
  const [submitAttempts, setSubmitAttempts] = useState(0);

  usePageTitle(`${Object.keys(errors).length > 0 ? 'Error: ' : ''}Contacts`);
  useErrorSummaryFocus(submitAttempts, Object.keys(errors).length > 0);

  useEffect(() => {
    if (!incomingContacts && usersData?.data) {
      setContacts(usersData.data.map(contactFromUser));
    }
    // Only seed from the fetched users list when the user hasn't already brought edits back from Review.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [usersData]);

  useEffect(() => {
    if (!organisationState?.organisationId) {
      navigate(ROUTES.admin.organisations.edit(organisationId), { replace: true });
    }
  }, [organisationState, organisationId, navigate]);

  if (!organisationState?.organisationId) return <></>;

  if (!incomingContacts && loadingUsers) {
    return (
      <ManagementLayout>
        <PageLoading />
      </ManagementLayout>
    );
  }

  const validate = (): ContactErrors => {
    const next: ContactErrors = {};

    contacts.forEach((contact, index) => {
      const { firstName, lastName } = splitFullName(contact.fullName);
      if (!contact.fullName.trim()) {
        next[`contacts-${index}-fullName`] = "Enter the contact person's full name";
      } else if (!firstName || !lastName) {
        next[`contacts-${index}-fullName`] = "Enter the contact person's first and last name";
      }
      if (!contact.jobRole) next[`contacts-${index}-jobRole`] = 'Select a job role';
      if (contact.jobRole === JOB_ROLE_OTHER && !contact.jobRoleTitle.trim()) next[`contacts-${index}-jobRoleTitle`] = 'Enter the job role title';
      if (!contact.email.trim()) {
        next[`contacts-${index}-email`] = "Enter the contact person's email address";
      } else if (!validateEmailAddress(contact.email.trim())) {
        next[`contacts-${index}-email`] = 'Enter a valid email address';
      }
    });

    return next;
  };

  const updateContact = (index: number, field: keyof ContactForm, value: string) => {
    setContacts((current) => current.map((contact, contactIndex) => (contactIndex === index ? { ...contact, [field]: value } : contact)));
  };

  const normaliseContactEmail = (index: number) => {
    setContacts((current) => current.map((contact, contactIndex) => (contactIndex === index ? { ...contact, email: contact.email.trim().toLowerCase() } : contact)));
  };

  const addContact = () => {
    setContacts((current) => [...current, { fullName: '', jobRole: '', jobRoleTitle: '', email: '' }]);
  };

  const removeContact = (index: number) => {
    const removed = contacts[index];
    if (removed?.id) setRemovedContactIds((current) => [...current, removed.id as string]);
    setContacts((current) => current.filter((_, contactIndex) => contactIndex !== index));
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitAttempts((n) => n + 1);
    const next = validate();
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    navigate(ROUTES.admin.organisations.editReview(organisationId), {
      state: { organisation: organisationState, contacts, removedContactIds } satisfies EditOrganisationContactsState,
    });
  };

  const errorList = Object.entries(errors)
    .filter(([, msg]) => Boolean(msg))
    .map(([key, msg]) => ({ targetName: key, text: msg ?? '' }));

  const goBackToDetails = () => navigate(ROUTES.admin.organisations.edit(organisationId), { state: organisationState });

  return (
    <ManagementLayout backLink={{ href: ROUTES.admin.organisations.edit(organisationId), onClick: goBackToDetails }}>
      <div className="organisations-edit-page organisations-edit-page--contacts">
        {errorList.length > 0 && <ErrorSummary heading="There is a problem" errors={errorList} />}

        <div className="organisations-edit-page__heading">
          <span className="govuk-caption-l">Edit organisation</span>
          <H1>Contacts</H1>
        </div>

        <form onSubmit={handleSubmit} noValidate>
          <div className="organisations-edit-page__fields organisations-edit-page__fields--contacts">
            {contacts.map((contact, index) => (
              <section className="organisations-edit-page__contact-person" key={contact.id ?? `new-${index}`}>
                <div className="organisations-edit-page__contact-header">
                  <H2>Contact person {index + 1}</H2>
                  {contacts.length > 1 && (
                    <GovUKButton
                      type="button"
                      className="govuk-button govuk-button--secondary organisations-edit-page__remove-button"
                      onClick={() => removeContact(index)}
                      data-testid={`admin-local-authorities-edit-contact-${index}-remove`}>
                      Remove
                    </GovUKButton>
                  )}
                </div>
                {index === 0 && (
                  <Paragraph className="organisations-edit-page__contact-description">
                    This is the person we&rsquo;ll send all the organisation&rsquo;s notifications and reminders to, including when reports are due.
                  </Paragraph>
                )}
                <GovUKFieldset.Input
                  id={`contacts-${index}-fullName`}
                  name={`contacts-${index}-fullName`}
                  label="Full name"
                  value={contact.fullName}
                  error={errors[`contacts-${index}-fullName`]}
                  onChange={(e) => updateContact(index, 'fullName', e.target.value)}
                  autoComplete="name"
                  required
                  testId={`admin-local-authorities-edit-contact-${index}-full-name`}
                />
                <GovUKFieldset.Select
                  id={`contacts-${index}-jobRole`}
                  name={`contacts-${index}-jobRole`}
                  label="Job role"
                  value={contact.jobRole}
                  options={JOB_ROLE_OPTIONS}
                  error={errors[`contacts-${index}-jobRole`]}
                  onChange={(e) => updateContact(index, 'jobRole', e.target.value)}
                  required
                  testId={`admin-local-authorities-edit-contact-${index}-job-role`}
                />
                {contact.jobRole === JOB_ROLE_OTHER && (
                  <div className="organisations-edit-page__conditional-field">
                    <GovUKFieldset.Input
                      id={`contacts-${index}-jobRoleTitle`}
                      name={`contacts-${index}-jobRoleTitle`}
                      label="Job role title"
                      value={contact.jobRoleTitle}
                      error={errors[`contacts-${index}-jobRoleTitle`]}
                      onChange={(e) => updateContact(index, 'jobRoleTitle', e.target.value)}
                      required
                      testId={`admin-local-authorities-edit-contact-${index}-job-role-title`}
                    />
                  </div>
                )}
                <GovUKFieldset.Input
                  id={`contacts-${index}-email`}
                  name={`contacts-${index}-email`}
                  label="Email address"
                  hint="Enter their work email address, not a group or distribution email."
                  type="email"
                  value={contact.email}
                  error={errors[`contacts-${index}-email`]}
                  onChange={(e) => updateContact(index, 'email', e.target.value)}
                  onBlur={() => normaliseContactEmail(index)}
                  autoComplete="email"
                  required
                  testId={`admin-local-authorities-edit-contact-${index}-email`}
                />
              </section>
            ))}
          </div>

          <GovUKButton
            type="button"
            className="govuk-button govuk-button--secondary organisations-edit-page__secondary-button"
            onClick={addContact}
            data-testid="admin-local-authorities-edit-add-contact">
            Add another contact person
          </GovUKButton>

          <GovUKButton type="submit" data-testid="admin-local-authorities-edit-contacts-submit">
            Continue
          </GovUKButton>
        </form>
      </div>
    </ManagementLayout>
  );
};

export default LocalAuthoritiesEditContacts;
