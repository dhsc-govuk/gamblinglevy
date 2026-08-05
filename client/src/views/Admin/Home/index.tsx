import React from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { GeneralLayout } from '../../../layouts';
import { H1, H2, Paragraph } from '../../../components/GovUKComponents';
import usePageTitle from '../../../hooks/usePageTitle';
import { ROUTES } from '../../../helpers/routes';
import './styles.css';

const adminTasks = [
  {
    title: 'Review submitted reports',
    description: 'Track submissions for each data collection and download reports.',
    href: ROUTES.admin.reports.list,
    testId: 'admin-home-link-reports',
  },
  {
    title: 'Run data collections',
    description: 'Set up reporting periods and send reminders to local authority users.',
    href: ROUTES.admin.collections.list,
    testId: 'admin-home-link-collections',
  },
  {
    title: 'Control user access',
    description: 'Add new users, set access levels, reset multi-factor authentication (MFA) and deactivate or delete accounts.',
    href: ROUTES.admin.users.list,
    testId: 'admin-home-link-users',
  },
  {
    title: 'Set up organisations',
    description: 'Add local authorities to the service, edit details and assign contacts to receive reporting notifications.',
    href: ROUTES.admin.organisations.list,
    testId: 'admin-home-link-local-authorities',
  },
  {
    title: 'Update form questions',
    description: 'Change the questions asked in the reporting form and update the list of key results that local authorities select from.',
    href: ROUTES.admin.forms.list,
    testId: 'admin-home-link-forms',
  },
];

const Home = (): React.ReactElement => {
  usePageTitle('Home');
  return (
    <GeneralLayout>
      <div className="admin-home" data-testid="admin-home">
        <H1 className="admin-home__heading">Share progress preventing gambling harms</H1>

        <section aria-labelledby="admin-home-tasks-heading" className="admin-home__tasks">
          <H2 id="admin-home-tasks-heading" className="admin-home__section-heading">
            Your tasks
          </H2>

          <div className="admin-home__cards" data-testid="admin-home-actions">
            {adminTasks.map((task) => (
              <article className="admin-home-card" key={task.href}>
                <h3 className="admin-home-card__heading">
                  <RouterLink to={task.href} className="govuk-link" data-testid={task.testId}>
                    {task.title}
                  </RouterLink>
                </h3>
                <Paragraph className="admin-home-card__description">{task.description}</Paragraph>
              </article>
            ))}
          </div>
        </section>
      </div>
    </GeneralLayout>
  );
};

export default Home;
