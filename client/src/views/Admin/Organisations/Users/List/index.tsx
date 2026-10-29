import React from 'react';
import { PageLoading } from '../../../../../components/GovUKComponents';
import { useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { UsersListView } from '../../../../../components/Global/OrganisationUsers';
import { localAuthorityKey } from '../../../../../helpers/queriesParams';
import { ROUTES } from '../../../../../helpers/routes';
import { pageTitleWithName } from '../../../../../hooks/usePageTitle';
import { getLocalAuthority } from '../../queries';

const LocalAuthorityUsersList = (): React.ReactElement => {
  const { organisationId = '' } = useParams<{ organisationId: string }>();

  const { data, isLoading } = useQuery({ queryKey: localAuthorityKey(organisationId), queryFn: () => getLocalAuthority(organisationId), enabled: !!organisationId });
  const org = data?.data;

  if (isLoading || !org) {
    return <PageLoading />;
  }

  return (
    <UsersListView
      organisationId={organisationId}
      pageTitle={pageTitleWithName('Users', org.name)}
      tableTitle="Users"
      breadcrumbs={[
        { label: 'Home', link: ROUTES.admin.home },
        { label: 'Local Authorities', link: ROUTES.admin.organisations.list },
        { label: org.name, link: ROUTES.admin.organisations.detail(organisationId) },
      ]}
      createHref={ROUTES.admin.organisations.createUser(organisationId)}
      rowHrefFn={(userId) => ROUTES.admin.organisations.user(organisationId, userId)}
      testIdPrefix="admin-la-users"
      emptyMessage="No users for this Local Authority yet."
    />
  );
};

export default LocalAuthorityUsersList;
