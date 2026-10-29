import axios from 'axios';
import { encodedJsonBody } from '../../helpers/encodedJsonBody';
import type { OfflineUploadResultDto, ReportProgressStatus, SubmissionTaskListDto } from './queries';

export interface ReportAnswerInput {
  questionCode: string;
  value: string;
}

export interface ActivityReportStateDto {
  activityId: string;
  reportStatus: ReportProgressStatus;
}

// Creates the draft submission as the task-list page opens. 409 when a submission already exists; 422
// REPORT_NOT_OPEN when the collection is closed/scheduled/draft.
export const startReport = (collectionId: string) => axios.post<SubmissionTaskListDto>(`quarterly-report/${collectionId}/submission`);

// Marks the "Manage your list of activities" step completed and clears any review flags.
export const confirmActivities = (collectionId: string) => axios.post<SubmissionTaskListDto>(`quarterly-report/${collectionId}/confirm-activities`);

export interface SaveActivityReportInput {
  collectionId: string;
  activityId: string;
  // True = "Save and come back later" (no required enforcement server-side; type/range errors still 422).
  // A save never completes the activity's report — that is completeActivityReport.
  saveAsDraft: boolean;
  // The current page's answers (including fields cleared to empty). The backend replaces only these
  // question codes, so saving one page never wipes the other page's saved answers.
  answers: ReportAnswerInput[];
}

export const saveActivityReport = ({ collectionId, activityId, saveAsDraft, answers }: SaveActivityReportInput) =>
  axios.put<ActivityReportStateDto>(
    `quarterly-report/${collectionId}/activities/${activityId}/answers`,
    ...encodedJsonBody({
      saveAsDraft,
      answers,
    })
  );

// "Check your answers" Continue for one activity — no body; the saved draft answers are validated server-side
// and the report marked completed (a 422 SUBMISSION_ANSWERS_INVALID envelope when answers are missing).
export const completeActivityReport = ({ collectionId, activityId }: { collectionId: string; activityId: string }) =>
  axios.post<ActivityReportStateDto>(`quarterly-report/${collectionId}/activities/${activityId}/complete`);

// --- "Record funding spent" section (BLUM-6612) ---

export interface FundingSpendStateDto {
  sectionStatus: ReportProgressStatus;
}

// Amounts are sent as numbers (the £ is a UI prefix, never part of the value). Null = not entered.
export interface FundingActivityActualInput {
  activityId: string;
  amount: number | null;
}

export interface SaveFundingActualsInput {
  collectionId: string;
  saveAsDraft: boolean;
  otherAmount: number | null;
  activities: FundingActivityActualInput[];
}

export const saveFundingActuals = ({ collectionId, saveAsDraft, otherAmount, activities }: SaveFundingActualsInput) =>
  axios.put<FundingSpendStateDto>(
    `quarterly-report/${collectionId}/funding-spend/actuals`,
    ...encodedJsonBody({
      saveAsDraft,
      otherAmount,
      activities,
    })
  );

export interface FundingQuarterAmountInput {
  quarter: number;
  amount: number | null;
}

export interface FundingActivityForecastInput {
  activityId: string;
  quarters: FundingQuarterAmountInput[];
}

export interface SaveFundingForecastInput {
  collectionId: string;
  saveAsDraft: boolean;
  activities: FundingActivityForecastInput[];
  other: FundingQuarterAmountInput[];
}

export const saveFundingForecast = ({ collectionId, saveAsDraft, activities, other }: SaveFundingForecastInput) =>
  axios.put<FundingSpendStateDto>(
    `quarterly-report/${collectionId}/funding-spend/forecast`,
    ...encodedJsonBody({
      saveAsDraft,
      activities,
      other,
    })
  );

// "Check your spending totals" Continue — validates the whole section and marks it completed (a 422
// SUBMISSION_ANSWERS_INVALID envelope when amounts are missing/invalid).
export const completeFundingSpend = (collectionId: string) => axios.post<FundingSpendStateDto>(`quarterly-report/${collectionId}/funding-spend/complete`);

// --- "Provide strategic feedback" section (BLUM-6613) ---

export interface StrategicFeedbackStateDto {
  sectionStatus: ReportProgressStatus;
}

export interface SaveStrategicFeedbackInput {
  collectionId: string;
  // True = "Save and come back later" (no required enforcement server-side); false validates every required question.
  saveAsDraft: boolean;
  // The full answer set (both pages) — replaced wholesale on each save.
  answers: ReportAnswerInput[];
}

export const saveStrategicFeedback = ({ collectionId, saveAsDraft, answers }: SaveStrategicFeedbackInput) =>
  axios.put<StrategicFeedbackStateDto>(
    `quarterly-report/${collectionId}/strategic-feedback`,
    ...encodedJsonBody({
      saveAsDraft,
      answers,
    })
  );

// "Check your answers" Continue — validates every required answer and marks the section completed (a 422
// SUBMISSION_ANSWERS_INVALID envelope when answers are missing).
export const completeStrategicFeedback = (collectionId: string) => axios.post<StrategicFeedbackStateDto>(`quarterly-report/${collectionId}/strategic-feedback/complete`);

// --- Report finalisation ---

export interface SubmitReportResultDto {
  referenceNumber: string;
  submittedAt: string;
}

// "Submit your quarterly report" — finalises the report (irreversible). 422 REPORT_INCOMPLETE when a section is
// still incomplete, 422 ACTIVITIES_NOT_CONFIRMED when the list is unconfirmed, 409 when already submitted.
export const submitReport = (collectionId: string) => axios.post<SubmitReportResultDto>(`quarterly-report/${collectionId}/submission/submit`);

// Downloads the submitted report as a ZIP of CSVs and triggers a browser save. The filename comes from the
// response's Content-Disposition header (the backend names it quarterly-report-{reference}.zip).
export const downloadReportExport = async (collectionId: string): Promise<void> => {
  const response = await axios.get<Blob>(`quarterly-report/${collectionId}/export`, { responseType: 'blob' });

  const disposition = response.headers['content-disposition'] as string | undefined;
  const match = disposition?.match(/filename\*?=(?:UTF-8'')?"?([^";]+)"?/i);
  const fileName = match ? decodeURIComponent(match[1]) : 'quarterly-report.zip';

  const url = window.URL.createObjectURL(response.data);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
};

// --- "Complete your report offline" (BLUM-8310, behind the BLUM_8310 flag) ---

// Uploads a filled-in offline template; the file says which section it is for. 200 saves and completes that
// section; 422 OFFLINE_UPLOAD_INVALID lists the file's errors (nothing saved); 400 OFFLINE_* refuses the file as a
// whole. See mapOfflineUploadError.
export const uploadOfflineReport = ({ collectionId, file }: { collectionId: string; file: File }) => {
  const form = new FormData();
  form.append('file', file);
  return axios.post<OfflineUploadResultDto>(`quarterly-report/${collectionId}/offline/upload`, form, { headers: { 'Content-Type': 'multipart/form-data' } });
};
