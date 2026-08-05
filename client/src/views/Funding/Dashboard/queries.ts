import axios from 'axios';

export interface FundingDashboardDto {
  allocatedAmount: number;
  cumulativeSpend: number;
  remainingBudget: number;
  financialYear: string | null;
  hasAllocation: boolean;
}

export const getFundingDashboard = () => axios.get<FundingDashboardDto>('funding/dashboard');
