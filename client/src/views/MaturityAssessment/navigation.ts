import { EnumUserRole, homeLinkForRole } from '../../components/AuthProvider';
import { ROUTES } from '../../helpers/routes';
import { canEditReport } from '../Reports/navigation';
import { maturityAssessmentName } from './names';

// Both LA roles complete the assessment, exactly as they do the quarterly report.
export const canEditMaturityAssessment = canEditReport;

// Home / Reports / Maturity assessment for {year} / …extra. The assessment crumb is omitted while the context has
// not resolved yet.
export const maturityBreadcrumbs = (role: EnumUserRole | null, collectionId: string, financialYearEndYear?: number | null, extra: { label: string; link: string }[] = []) => [
  { label: 'Home', link: homeLinkForRole(role) },
  { label: 'Reports', link: ROUTES.reports.list },
  ...(financialYearEndYear ? [{ label: maturityAssessmentName(financialYearEndYear), link: ROUTES.reports.maturity(collectionId) }] : []),
  ...extra,
];
