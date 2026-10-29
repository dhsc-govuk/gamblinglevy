import React from 'react';
import { PageLoading } from '../../../../../components/GovUKComponents';
import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { UserView } from '../../../../../components/Global/OrganisationUsers';
import { localAuthorityKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { pageTitleWithName } from '../../../../../hooks/usePageTitle';
import { getLocalAuthority } from '../../queries';

const LocalAuthorityUsersView = (): React.ReactElement => {
  const { organisationId = '', userId = '' } = useParams<{
    organisationId: string;
    userId: string;
  }>();

  const { data, isLoading } = useQuery({ queryKey: localAuthorityKey(organisationId), queryFn: () => getLocalAuthority(organisationId), enabled: !!organisationId });
  const org = data?.data;

  if (isLoading || !org) {
    return <PageLoading />;
  }

  const usersBase = ROUTES.admin.organisations.users(organisationId);

  return (
    <UserView
      organisationId={organisationId}
      userId={userId}
      pageTitle={pageTitleWithName('User', org.name)}
      breadcrumbs={[
        { label: 'Home', link: ROUTES.admin.home },
        { label: 'Management', link: ROUTES.admin.management },
        { label: 'Organisations', link: ROUTES.admin.organisations.list },
        { label: org.name, link: ROUTES.admin.organisations.detail(organisationId) },
        { label: 'Users', link: usersBase },
      ]}
      editHref={ROUTES.admin.organisations.editUser(organisationId, userId)}
      deleteSuccessNavigateHref={usersBase}
      testIdPrefix="admin-la-users"
    />
  );
};

export default LocalAuthorityUsersView;
