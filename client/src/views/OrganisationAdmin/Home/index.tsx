import React from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { GeneralLayout } from '../../../layouts';
import { H1, H2, Paragraph } from '../../../components/GovUKComponents';
import { useAuthProvider } from '../../../components/AuthProvider';
import { canManageUsers } from '../navigation';
import usePageTitle from '../../../hooks/usePageTitle';
import { ROUTES } from '../../../helpers/routes';
import './styles.css';

// The LA home, shared by LA Admin and LA Lead (BLUM-7038). Only the users card differs: a Lead may view
// its colleagues but not manage them, so the card is worded for what that role can actually do.
const organisationAdminTasks = (canManage: boolean) => [
  {
    title: 'Manage your list of activities',
    description: 'Create new gambling harm prevention activities and keep them up to date.',
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
    title: 'View your reports',
    description: 'Check your local authority’s past reporting periods, and download copies of the reports it submitted.',
    href: ROUTES.reports.submitted,
    testId: 'organisation-admin-home-link-submitted-reports',
  },
  {
    title: 'View your funding',
    description: 'Check your levy allocation, the amount spent so far and your remaining budget.',
    href: ROUTES.funding.dashboard,
    testId: 'organisation-admin-home-link-funding',
  },
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
  const { userRole } = useAuthProvider();
  const tasks = organisationAdminTasks(canManageUsers(userRole));

  return (
    <GeneralLayout>
      <div className="organisation-admin-home" data-testid="organisation-admin-home">
        <H1 className="organisation-admin-home__heading">Share progress preventing gambling harms</H1>

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
              <RouterLink to={ROUTES.organisationAdmin.guidance} className="govuk-link">
                Guidance
              </RouterLink>{' '}
              is available to help you use this service.
            </Paragraph>
            <Paragraph>You can also contact us for more help.</Paragraph>
          </div>

          <h3 className="organisation-admin-home__contact-heading">Phone</h3>
          <div className="organisation-admin-home__contact-body">
            <Paragraph>Phone: 020 7946 0101</Paragraph>
            <Paragraph>Monday to Friday, 9am to 5pm (except public holidays)</Paragraph>
            <Paragraph>
              <a href="https://www.gov.uk/call-charges" className="govuk-link">
                Find out about call charges
              </a>
            </Paragraph>
          </div>

          <h3 className="organisation-admin-home__contact-heading">Email</h3>
          <div className="organisation-admin-home__contact-body organisation-admin-home__contact-body--email">
            <Paragraph>
              <a href="mailto:gambling.levy@dhsc.gov.uk" className="govuk-link">
                gambling.levy@dhsc.gov.uk
              </a>
            </Paragraph>
            <Paragraph>We aim to respond within 2 working days</Paragraph>
          </div>
        </section>
      </div>
    </GeneralLayout>
  );
};

export default Home;
