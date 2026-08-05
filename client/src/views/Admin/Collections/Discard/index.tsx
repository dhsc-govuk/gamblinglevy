import React, { useEffect } from 'react';
import { Link as RouterLink, useLocation, useNavigate, useParams } from 'react-router-dom';
import { useQuery } from 'react-query';
import { GeneralLayout } from '../../../../layouts';
import { ConfirmActionPanel, GovUKButton, LoadingBox, LeadParagraph } from '../../../../components/GovUKComponents';
import usePageTitle from '../../../../hooks/usePageTitle';
import { collectionKey } from '../../../../helpers/queriesParams';
import { getCollection } from '../queries';
import type { CollectionWizardDiscardState } from '../../../../components/Global/Collections/CollectionWizard';
import './styles.css';

const CollectionsDiscard = (): React.ReactElement => {
  usePageTitle('Discard edits');
  const { collectionId = '' } = useParams<{ collectionId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const wizardState = location.state as CollectionWizardDiscardState | null;

  const { data, isLoading } = useQuery(collectionKey(collectionId), () => getCollection(collectionId), {
    enabled: !!collectionId,
  });
  const name = data?.data?.name ?? wizardState?.formState.name ?? 'this data collection';

  useEffect(() => {
    if (!wizardState?.formState) {
      navigate(`/admin/collections/${collectionId}/edit`, { replace: true });
    }
  }, [wizardState, collectionId, navigate]);

  if (!wizardState?.formState) {
    return <></>;
  }

  const goBackToEdit = () => navigate(`/admin/collections/${collectionId}/edit`, { state: wizardState });
  const confirmDiscard = () =>
    navigate(`/admin/collections/${collectionId}`, {
      replace: true,
      state: {
        banner: {
          title: 'Edits discarded',
          body: `The updates you made to the data collection ‘${name}’ were not saved.`,
        },
      },
    });

  if (isLoading) {
    return (
      <GeneralLayout backLink={{ href: `/admin/collections/${collectionId}/edit`, onClick: goBackToEdit }}>
        <LoadingBox loading>
          <div style={{ minHeight: '200px' }} />
        </LoadingBox>
      </GeneralLayout>
    );
  }

  return (
    <GeneralLayout
      backLink={{
        href: `/admin/collections/${collectionId}/edit`,
        onClick: goBackToEdit,
      }}>
      <div className="collections-discard-page">
        <ConfirmActionPanel testId="admin-collections-edit-discard-panel">
          <h1 className="govuk-heading-l">Are you sure you want to discard edits?</h1>
          <LeadParagraph>If you discard your edits, you&apos;ll lose any new information you have added to the data collection &lsquo;{name}&rsquo;.</LeadParagraph>
          <div className="govuk-button-group">
            <GovUKButton className="govuk-button govuk-button--inverse" onClick={confirmDiscard} data-testid="admin-collections-edit-discard-confirm">
              Discard edits
            </GovUKButton>
            <RouterLink
              to={`/admin/collections/${collectionId}/edit`}
              state={wizardState}
              className="govuk-link govuk-link--inverse"
              data-testid="admin-collections-edit-discard-cancel">
              Go back to data collection
            </RouterLink>
          </div>
        </ConfirmActionPanel>
      </div>
    </GeneralLayout>
  );
};

export default CollectionsDiscard;
