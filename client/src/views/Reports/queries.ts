import axios from 'axios';
import type { SubmissionStatus } from '../Admin/Reports/queries';
import type { CollectionStatus, CollectionType } from '../Admin/Collections/queries';

// Typed contract for the LA-facing quarterly-report surface (BLUM-6611) — mirrors
// api/src/Api/Dtos/QuarterlyReportDtos.cs (camelCase JSON).

export type ReportProgressStatus = 'not_started' | 'in_progress' | 'completed';
export type ReportModuleCode = 'activity_key_results' | 'funding_spent' | 'strategic_feedback';
export type ReportSection = 'results' | 'delivery';

export interface QuarterlyReportContextDto {
  collectionId: string;
  collectionName: string;
  quarter: number;
  financialYear: string;
  reportingPeriodStart: string;
  reportingPeriodEnd: string;
  submissionDeadline: string;
  localAuthorityName: string;
  // Null until the LA clicks "Create report".
  submissionId: string | null;
  // Null until the report is finalised (submitted).
  submittedAt: string | null;
  referenceNumber: string | null;
}

export interface SubmissionSectionDto {
  moduleCode: ReportModuleCode;
  moduleName: string;
  status: ReportProgressStatus;
  // The activity list changed after this started section was confirmed — it must be revisited.
  reviewRequired: boolean;
  // False until the activities list has been confirmed (rendered "Cannot start yet").
  isAvailable: boolean;
}

export interface SubmissionTaskListDto {
  submissionId: string;
  activitiesConfirmed: boolean;
  activitiesReviewRequired: boolean;
  sections: SubmissionSectionDto[];
  // Report-level submit gate: activities confirmed + every section completed + nothing to review + not submitted.
  canSubmit: boolean;
  submitted: boolean;
  referenceNumber: string | null;
}

export interface ReportableActivityDto {
  activityId: string;
  title: string;
  taskName: string | null;
  activityStatus: 'active' | 'closed' | 'finished';
  reportStatus: ReportProgressStatus;
}

export interface ReportQuestionOptionDto {
  value: string;
  label: string;
}

export interface ReportQuestionDto {
  id: string;
  code: string;
  label: string;
  hint: string | null;
  questionType: string;
  isRequired: boolean;
  displayOrder: number;
  conditionalQuestionCode: string | null;
  conditionalValue: string | null;
  options: ReportQuestionOptionDto[];
  minValue: number | null;
  maxValue: number | null;
  // "%" for percentages, else null.
  suffix: string | null;
  // 'results' | 'delivery' for activity key results; 'wider-progress' | 'working-together' for strategic feedback.
  section: string;
}

export interface ReportAnswerDto {
  questionCode: string;
  value: string | null;
}

export interface ActivityReportQuestionsDto {
  activityId: string;
  activityTitle: string;
  activityStatus: string;
  reportStatus: ReportProgressStatus;
  questions: ReportQuestionDto[];
  // Saved report answers merged over prefill from the activity record.
  answers: ReportAnswerDto[];
}

// The report context for the collection the user opened. Collection-scoped rather than "the organisation's current
// window" — with two windows open at once the implicit pick opened the wrong quarter (BLUM-6950). 404 when the
// collection is not an assigned, published quarterly collection for the caller's organisation.
export const getReportContext = (collectionId: string) => axios.get<QuarterlyReportContextDto>(`quarterly-report/${collectionId}/context`);

export const getReportTaskList = (collectionId: string) => axios.get<SubmissionTaskListDto>(`quarterly-report/${collectionId}/task-list`);

export const getReportActivities = (collectionId: string) => axios.get<ReportableActivityDto[]>(`quarterly-report/${collectionId}/activities`);

export const getReportActivityForm = (collectionId: string, activityId: string) =>
  axios.get<ActivityReportQuestionsDto>(`quarterly-report/${collectionId}/activities/${activityId}/questions`);

// --- "Record funding spent" section (BLUM-6612) — mirrors the FundingSpend* DTOs in QuarterlyReportDtos.cs ---

export interface FundingSpendQuarterDto {
  quarter: number;
  start: string;
  end: string;
}

export interface FundingSpendActivityDto {
  activityId: string;
  title: string;
  taskName: string | null;
  // Saved "levy funding spent this quarter"; null until answered.
  actualThisQuarter: number | null;
  // Accumulated actual spend from earlier quarters of the same financial year (the "Total spent so far" inset).
  totalSpentSoFar: number;
  // Saved expected spend keyed by quarter number (JSON object keys are strings).
  forecast: Record<number, number | null>;
}

export interface FundingSpendSummaryDto {
  totalGrantAllocation: number;
  totalSpendThisQuarter: number;
  expectedSpendForYear: number;
  spendToDate: number;
}

export interface FundingSpendDto {
  submissionId: string;
  currentQuarter: number;
  financialYear: string;
  sectionStatus: ReportProgressStatus;
  remainingQuarters: FundingSpendQuarterDto[];
  activities: FundingSpendActivityDto[];
  otherActualThisQuarter: number | null;
  otherForecast: Record<number, number | null>;
  summary: FundingSpendSummaryDto;
}

export const getFundingSpend = (collectionId: string) => axios.get<FundingSpendDto>(`quarterly-report/${collectionId}/funding-spend`);

// --- "Provide strategic feedback" section (BLUM-6613) — mirrors the StrategicFeedback* DTOs in QuarterlyReportDtos.cs ---

// One page-group of the feedback form. `key` is the stable page slug ('wider-progress' | 'working-together') also
// carried on each question's `section` field, so the page/check filters route questions by it.
export interface StrategicFeedbackPageDto {
  key: string;
  title: string;
  questions: ReportQuestionDto[];
}

export interface StrategicFeedbackDto {
  submissionId: string;
  currentQuarter: number;
  financialYear: string;
  sectionStatus: ReportProgressStatus;
  pages: StrategicFeedbackPageDto[];
  answers: ReportAnswerDto[];
}

export const getStrategicFeedback = (collectionId: string) => axios.get<StrategicFeedbackDto>(`quarterly-report/${collectionId}/strategic-feedback`);

// --- LA submission history + detail (BLUM-6097 / BLUM-6098) — mirrors the My* DTOs in QuarterlyReportDtos.cs ---

// One of the caller's organisation's quarterly collections. collectionStatus is the collection's own lifecycle
// status (the landing surfaces 'open' rows) and submissionStatus is the derived per-org status; together they split
// the rows across the two LA screens — see reportHistoryRows / overdueCollections in display.tsx. Org-scoped
// server-side to the signed-in user.
export interface MyCollectionSummaryDto {
  collectionId: string;
  type: CollectionType | null;
  quarter: number | null;
  reportingPeriodStart: string | null;
  reportingPeriodEnd: string | null;
  submissionDeadline: string | null;
  collectionStatus: CollectionStatus;
  submissionStatus: SubmissionStatus;
  submittedAt: string | null;
}

export const getMyReportCollections = () => axios.get<MyCollectionSummaryDto[]>('quarterly-report/collections');

// The caller's own submission detail for one collection (read-only — no DHSC verification controls). hasSubmission
// drives the empty state; submittedBy*/submittedAt are populated only once the report is finalised.
export interface MySubmissionDetailDto {
  collectionId: string;
  quarter: number | null;
  reportingPeriodStart: string | null;
  reportingPeriodEnd: string | null;
  localAuthorityName: string;
  status: SubmissionStatus;
  hasSubmission: boolean;
  submittedByName: string | null;
  submittedByEmail: string | null;
  submittedAt: string | null;
}

export const getMySubmissionDetail = (collectionId: string) => axios.get<MySubmissionDetailDto>(`quarterly-report/submissions/${collectionId}`);
