import React from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { GeneralLayout } from '../../../layouts';
import { GovUKInsetText, H1, H2, Paragraph, SummaryList } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import { canManageUsers } from '../navigation';
import usePageTitle from '../../../hooks/usePageTitle';
import { ROUTES } from '../../../helpers/routes';
import { GUIDANCE_SUPPORT_EMAIL, GUIDANCE_SUPPORT_MAILTO, GUIDANCE_SUPPORT_RESPONSE_TIME } from '../../../helpers/constants';
import './styles.css';

// The LA Admin and LA Lead share one task set; only the users card differs. A Lead may view its colleagues
// but not manage them, so its users card is worded for what that role can actually do.
const sharedTasks = [
  {
    title: 'Manage your list of activities',
    description: 'Create new gambling harms prevention activities and keep them up to date.',
    href: ROUTES.activities.list,
    testId: 'organisation-admin-home-link-activities',
  },
  {
    title: 'Submit a quarterly report',
    description: 'Provide updates on active, closed and finished activities for the current reporting period.',
    href: ROUTES.reports.list,
    testId: 'organisation-admin-home-link-report',
  },
  {
    title: 'View submitted reports',
    description: 'Access and download copies of reports your local authority has submitted in the past.',
    href: ROUTES.reports.submitted,
    testId: 'organisation-admin-home-link-submitted-reports',
  },
];

const organisationAdminTasks = (canManage: boolean) => [
  ...sharedTasks,
  canManage
    ? {
        title: 'Manage user accounts and access',
        description: 'Add new users, remove staff who have left and change permission levels for your local authority.',
        href: ROUTES.organisationAdmin.users.list,
        testId: 'organisation-admin-home-link-users',
      }
    : {
        title: 'View user accounts and access',
        description: 'See who has an account for your local authority and their level of access.',
        href: ROUTES.organisationAdmin.users.list,
        testId: 'organisation-admin-home-link-users',
      },
];

const Home = (): React.ReactElement => {
  usePageTitle('Home');
  const { organisationName, userRole } = useAuthProvider();
  const canManage = canManageUsers(userRole);
  const tasks = organisationAdminTasks(canManage);

  return (
    <GeneralLayout>
      <div className="organisation-admin-home" data-testid="organisation-admin-home">
        <H1 className="organisation-admin-home__heading">Share progress preventing gambling harms</H1>

        {canManage && (
          <GovUKInsetText className="organisation-admin-home__authority" data-testid="organisation-admin-home-authority">
            <SummaryList halfWidthColumns testId="organisation-admin-home-authority-details" items={[{ label: 'Local authority', value: organisationName ?? '' }]} />
          </GovUKInsetText>
        )}

        <section className="organisation-admin-home__tasks" aria-labelledby="organisation-admin-home-tasks-heading">
          <div className="organisation-admin-home__section-heading-wrap">
            <H2 id="organisation-admin-home-tasks-heading" className="organisation-admin-home__section-heading">
              Your tasks
            </H2>
          </div>

          <div className="organisation-admin-home__cards" data-testid="organisation-admin-home-actions">
            {tasks.map((task) => (
              <article className="organisation-admin-home-card" key={task.title}>
                <h3 className="organisation-admin-home-card__heading">
                  <RouterLink to={task.href} className="govuk-link" data-testid={task.testId}>
                    {task.title}
                  </RouterLink>
                </h3>
                <Paragraph className="organisation-admin-home-card__description">{task.description}</Paragraph>
              </article>
            ))}
          </div>
        </section>

        <section className="organisation-admin-home__support" aria-labelledby="organisation-admin-home-support-heading">
          <div className="organisation-admin-home__support-heading-wrap">
            <H2 id="organisation-admin-home-support-heading" className="organisation-admin-home__section-heading">
              Support and guidance
            </H2>
          </div>

          <div className="organisation-admin-home__support-intro">
            <Paragraph>
              <RouterLink to={ROUTES.organisationAdmin.guidance} className="govuk-link" data-testid="organisation-admin-home-link-guidance">
                Guidance
              </RouterLink>{' '}
              is available to help you use this service.
            </Paragraph>
            <Paragraph>You can also contact us for more help.</Paragraph>
          </div>

          <h3 className="organisation-admin-home__contact-heading">Email</h3>
          <div className="organisation-admin-home__contact-body">
            <Paragraph>
              <a href={GUIDANCE_SUPPORT_MAILTO} className="govuk-link" data-testid="organisation-admin-home-support-email">
                {GUIDANCE_SUPPORT_EMAIL}
              </a>
            </Paragraph>
            <Paragraph>{GUIDANCE_SUPPORT_RESPONSE_TIME}</Paragraph>
          </div>
        </section>
      </div>
    </GeneralLayout>
  );
};

export default Home;
