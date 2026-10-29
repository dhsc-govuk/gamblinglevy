import axios from 'axios';
import { downloadFile } from '../../helpers/downloadFile';
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
  // Null until the task-list entry route creates the draft submission.
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
  // Question Protocol copy (BLUM-7620): the blank-on-submit message, the free-text character limit and the
  // over-limit message. Only the "Activity delivery" and strategic-feedback questions carry a limit.
  errorMessage: string | null;
  maxLength: number | null;
  maxLengthErrorMessage: string | null;
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
  // Accumulated actual spend from every earlier quarter this activity has been reported in, earlier financial
  // years included (the "Total spent so far" inset, BLUM-7703) — unlike the summary's year-scoped spendToDate.
  totalSpentSoFar: number;
  // Saved expected spend keyed by quarter number (JSON object keys are strings).
  forecast: Record<number, number | null>;
  // A Planned activity (BLUM-7672): it takes a forecast on "Forecast spend" but has no input on "Activity
  // spending", so actualThisQuarter is always null. Decided by the server — never re-derive it from a status.
  forecastOnly: boolean;
}

export interface FundingSpendSummaryDto {
  totalGrantAllocation: number;
  totalSpendThisQuarter: number;
  expectedSpendForYear: number;
  spendToDate: number;
  // spendToDate INCLUDING the current quarter's own actuals, unlike spendToDate itself.
  totalSpendToDate: number;
  // totalGrantAllocation - totalSpendToDate. Added to the API for the CSV exports (BLUM-7130) but always
  // on the wire, which is why declaring it here needs no backend change.
  remainingBudget: number;
  // The forecast for the quarters still ahead. Zero on a Q4 report — nothing is left to forecast.
  totalForecast: number;
}

export interface FundingSpendDto {
  submissionId: string;
  currentQuarter: number;
  financialYear: string;
  sectionStatus: ReportProgressStatus;
  remainingQuarters: FundingSpendQuarterDto[];
  activities: FundingSpendActivityDto[];
  otherActualThisQuarter: number | null;
  // Actual "Other" spend accumulated through the current reporting quarter.
  otherTotalActualToDate: number;
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
  // A maturity-assessment row (type 'maturity_assessment', quarter null) is named by its financial year (BLUM-7859).
  financialYear?: string | null;
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
  // The collection type routes the detail to the maturity read-only view (BLUM-7859); referenceNumber is the
  // finalised report's reference.
  type?: CollectionType | null;
  financialYear?: string | null;
  referenceNumber?: string | null;
  // The finalised ZIP's byte length for the Files caption (BLUM-9018). Null until submitted, or when the server could
  // not measure it — the caption then shows the format alone.
  exportSizeBytes?: number | null;
}

export const getMySubmissionDetail = (collectionId: string) => axios.get<MySubmissionDetailDto>(`quarterly-report/submissions/${collectionId}`);

// --- "Complete your report offline" (BLUM-8310, behind the BLUM_8310 flag) ---

// The route slug of each section's CSV template (QuarterlyReportOfflineController).
export const OFFLINE_TEMPLATE_SLUG: Record<ReportModuleCode, string> = {
  activity_key_results: 'activity-results',
  funding_spent: 'funding-spent',
  strategic_feedback: 'strategic-feedback',
};

// A section's template, pre-filled with what is saved, saved by the browser. Refused with the report's own write
// envelopes (REPORT_NOT_OPEN, ACTIVITIES_NOT_CONFIRMED, 409 once submitted); a bodiless 404 when the flag is off.
export const downloadOfflineTemplate = (collectionId: string, moduleCode: ReportModuleCode) =>
  downloadFile(`quarterly-report/${collectionId}/offline/${OFFLINE_TEMPLATE_SLUG[moduleCode]}/template`, `${OFFLINE_TEMPLATE_SLUG[moduleCode]}.csv`);

// A successful upload: the section it saved and completed, and the task list as it now stands.
export interface OfflineUploadResultDto {
  moduleCode: ReportModuleCode;
  taskList: SubmissionTaskListDto;
}

// One problem in an uploaded file, located as the spreadsheet shows it. column is a letter ("K"), question "Q7";
// both null for a problem with a whole row.
export interface OfflineUploadErrorDto {
  row: number;
  column: string | null;
  question: string | null;
  message: string;
}

export interface OfflineAnnotatedFileDto {
  fileName: string;
  contentBase64: string;
}

// The 422 OFFLINE_UPLOAD_INVALID body. errors is capped at 100; errorCount and the annotated file have them all.
export interface OfflineUploadInvalidDto {
  error: 'OFFLINE_UPLOAD_INVALID';
  message: string;
  moduleCode: ReportModuleCode;
  errorCount: number;
  errors: OfflineUploadErrorDto[];
  annotatedFile: OfflineAnnotatedFileDto;
}
