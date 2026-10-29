import React from 'react';
import { PageLoading } from '../../../../../components/GovUKComponents';
import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { UserCreateView } from '../../../../../components/Global/OrganisationUsers';
import { localAuthorityKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { pageTitleWithName } from '../../../../../hooks/usePageTitle';
import { getLocalAuthority } from '../../queries';

const LocalAuthorityUsersCreate = (): React.ReactElement => {
  const { organisationId = '' } = useParams<{ organisationId: string }>();

  const { data, isLoading } = useQuery({ queryKey: localAuthorityKey(organisationId), queryFn: () => getLocalAuthority(organisationId), enabled: !!organisationId });
  const org = data?.data;

  if (isLoading || !org) {
    return <PageLoading />;
  }

  const usersBase = ROUTES.admin.organisations.users(organisationId);

  return (
    <UserCreateView
      organisationId={organisationId}
      pageTitle={pageTitleWithName('Create user', org.name)}
      heading={`Create a user for ${org.name}`}
      breadcrumbs={[
        { label: 'Home', link: ROUTES.admin.home },
        { label: 'Local Authorities', link: ROUTES.admin.organisations.list },
        { label: org.name, link: ROUTES.admin.organisations.detail(organisationId) },
        { label: 'Users', link: usersBase },
      ]}
      backLinkHref={usersBase}
      successNavigateFn={(newUserId) => ROUTES.admin.organisations.user(organisationId, newUserId)}
      testIdPrefix="admin-la-users"
    />
  );
};

export default LocalAuthorityUsersCreate;
