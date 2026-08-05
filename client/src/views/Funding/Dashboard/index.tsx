import React from 'react';
import { PageLoading } from '../../../components/GovUKComponents';
import { useQuery } from 'react-query';
import { GeneralLayout } from '../../../layouts';
import { H1, Paragraph, GovUKSummaryCard, SummaryList, GovUKInsetText } from '../../../components/GovUKComponents';
import { homeLinkForRole, useAuthProvider } from '../../../components/AuthProvider';
import usePageTitle from '../../../hooks/usePageTitle';
import { laFundingDashboardKey } from '../../../helpers/queriesParams';
import { formatGBP } from '../../../helpers/currency';
import { getFundingDashboard } from './queries';

const Dashboard = (): React.ReactElement => {
  usePageTitle('Funding');
  const { userRole } = useAuthProvider();

  const { data, isLoading } = useQuery(laFundingDashboardKey, getFundingDashboard);
  const funding = data?.data;

  const cardTitle = funding?.financialYear ? `Annual funding ${funding.financialYear}` : 'Annual funding';

  return (
    <GeneralLayout breadcrumbs={[{ label: 'Home', link: homeLinkForRole(userRole) }]}>
      <div style={{ paddingTop: '20px' }} data-testid="funding-dashboard">
        <H1>Funding</H1>
        <Paragraph>View your organisation&rsquo;s annual levy allocation, spend to date, and the budget you have remaining for the current financial year.</Paragraph>

        {isLoading || !funding ? (
          <PageLoading />
        ) : (
          <>
            {!funding.hasAllocation && (
              <GovUKInsetText data-testid="funding-dashboard-no-allocation">
                No levy allocation has been set for your organisation for the current financial year. Contact DHSC if you think this is incorrect.
              </GovUKInsetText>
            )}

            <GovUKSummaryCard title={cardTitle} testId="funding-dashboard-summary">
              <SummaryList
                testId="funding-dashboard-figures"
                items={[
                  { label: 'Annual allocation', value: formatGBP(funding.allocatedAmount) },
                  { label: 'Spent to date', value: formatGBP(funding.cumulativeSpend) },
                  { label: 'Remaining budget', value: formatGBP(funding.remainingBudget) },
                ]}
              />
            </GovUKSummaryCard>
          </>
        )}
      </div>
    </GeneralLayout>
  );
};

export default Dashboard;
