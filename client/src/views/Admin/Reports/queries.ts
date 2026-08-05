import axios from 'axios';
import type { CollectionStatus } from '../Collections/queries';

// DHSC submission visibility (BLUM-6095 / BLUM-6445). The reporting-windows list reuses the data-collections
// summary endpoint (see ../Collections/queries getCollections); these reads add the per-window organisation
// status list and a single organisation's submission detail.

// Derived per-organisation submission status (not stored — see the backend SubmissionStatusRules).
export type SubmissionStatus = 'not_started' | 'in_progress' | 'completed' | 'overdue';

export interface OrgSubmissionStatusDto {
  organisationId: string;
  organisationName: string;
  status: SubmissionStatus;
  submittedAt: string | null;
}

export interface CollectionSubmissionSummaryDto {
  collectionId: string;
  collectionName: string;
  collectionStatus: CollectionStatus;
  // startDate/deadline are the COLLECTION WINDOW (rendered as the "Start date"/"Deadline" rows); quarter and
  // reportingPeriodStart/End are the period being reported on, which names the consolidated export. See the
  // OrgSubmissionDetailDto comment below — never substitute one pair for the other.
  startDate: string | null;
  deadline: string | null;
  quarter: number | null;
  reportingPeriodStart: string | null;
  reportingPeriodEnd: string | null;
  organisations: OrgSubmissionStatusDto[];
}

export interface OrgSubmissionDetailDto {
  collectionId: string;
  collectionName: string;
  collectionStatus: CollectionStatus;
  organisationId: string;
  organisationName: string;
  status: SubmissionStatus;
  hasSubmission: boolean;
  submittedByName: string | null;
  submittedByEmail: string | null;
  submittedAt: string | null;
  referenceNumber: string | null;
  // Two distinct date ranges — see the OrgSubmissionDetailDto comment in Api/Dtos/DataCollectionsDtos.cs.
  // startDate/deadline are the COLLECTION WINDOW (when LAs may submit); reportingPeriodStart/End are the period
  // the report is ABOUT, derived from financial year + quarter. They coincide only when the collection was
  // published with "collection window same as reporting period" = Yes, so never substitute one for the other.
  startDate?: string | null;
  deadline?: string | null;
  quarter: number | null;
  reportingPeriodStart: string | null;
  reportingPeriodEnd: string | null;
}

export const getReportSubmissions = (collectionId: string) => axios.get<CollectionSubmissionSummaryDto>(`data-collections/${collectionId}/submissions`);

export const getReportSubmissionDetail = (collectionId: string, organisationId: string) =>
  axios.get<OrgSubmissionDetailDto>(`data-collections/${collectionId}/submissions/${organisationId}`);
