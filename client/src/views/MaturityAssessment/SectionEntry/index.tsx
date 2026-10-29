import React from 'react';
import { Navigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { GeneralLayout } from '../../../layouts';
import { PageLoading } from '../../../components/GovUKComponents';
import { maturitySectionKey } from '../../../helpers/queriesParams';
import { ROUTES } from '../../../helpers/routes';
import { getMaturitySection } from '../queries';
import { maturityAnswersToRecord } from '../display';
import { firstPageCode } from '../pages';
import { useMaturityContextFor } from '../context';

// The task-list link for a section lands here and is forwarded on: a completed section opens on its
// check-your-answers page, anything else on its first VISIBLE page (a pre-filled "No" can hide the follow-on pages
// before the user has seen them). The section has no page of its own.
const SectionEntry = (): React.ReactElement => {
  const { collectionId = '', sectionCode = '' } = useParams<{ collectionId: string; sectionCode: string }>();
  const { context, isLoading: contextLoading, isError: contextError } = useMaturityContextFor(collectionId);

  const sectionQuery = useQuery({
    queryKey: maturitySectionKey(collectionId, sectionCode),
    queryFn: () => getMaturitySection(collectionId, sectionCode),
    enabled: !!collectionId && !!sectionCode && !!context?.assessmentId,
  });
  const section = sectionQuery.data?.data;

  if (contextError || sectionQuery.isError) {
    return <Navigate to={ROUTES.reports.maturity(collectionId)} replace />;
  }

  if (!contextLoading && (!context || !context.assessmentId)) {
    return <Navigate to={ROUTES.reports.maturity(collectionId)} replace />;
  }

  if (section) {
    if (section.status === 'completed') {
      return <Navigate to={ROUTES.reports.maturitySectionCheck(collectionId, sectionCode)} replace />;
    }
    const page = firstPageCode(section.pages, maturityAnswersToRecord(section.answers));
    return <Navigate to={page ? ROUTES.reports.maturitySectionPage(collectionId, sectionCode, page) : ROUTES.reports.maturitySectionCheck(collectionId, sectionCode)} replace />;
  }

  return (
    <GeneralLayout>
      <PageLoading />
    </GeneralLayout>
  );
};

export default SectionEntry;
