import axios from 'axios';
import { encodedJsonBody } from '../../helpers/encodedJsonBody';
import { downloadFile } from '../../helpers/downloadFile';
import type { ReportAnswerInput } from '../Reports/mutations';
import type { MaturityTaskListDto } from './queries';
import type { ReportProgressStatus } from '../Reports/queries';

// Starts the assessment (and pre-fills it) as the task-list page opens. 409 when already started; 422
// REPORT_NOT_OPEN outside the window.
export const startMaturityAssessment = (collectionId: string) => axios.post<MaturityTaskListDto>(`maturity-assessment/${collectionId}/assessment`);

export interface MaturitySectionStateDto {
  sectionCode: string;
  status: ReportProgressStatus;
}

export interface SaveMaturityPageInput {
  collectionId: string;
  sectionCode: string;
  pageCode: string;
  // True = "Save and come back later" (no required enforcement server-side; option/format errors still 422).
  saveAsDraft: boolean;
  // The page's answers (including fields cleared to empty). The backend replaces only these question codes, so
  // saving one page never wipes another page's saved answers — and never touches a pre-filled answer the page
  // did not show.
  answers: ReportAnswerInput[];
}

export const saveMaturityPage = ({ collectionId, sectionCode, pageCode, saveAsDraft, answers }: SaveMaturityPageInput) =>
  axios.put<MaturitySectionStateDto>(
    `maturity-assessment/${collectionId}/sections/${encodeURIComponent(sectionCode)}/answers`,
    ...encodedJsonBody({
      pageCode,
      saveAsDraft,
      answers,
    })
  );

// The section's check-your-answers Continue — no body; the saved answers are validated server-side and the section
// marked completed (a 422 SUBMISSION_ANSWERS_INVALID envelope when answers are missing).
export const completeMaturitySection = ({ collectionId, sectionCode }: { collectionId: string; sectionCode: string }) =>
  axios.post<MaturitySectionStateDto>(`maturity-assessment/${collectionId}/sections/${encodeURIComponent(sectionCode)}/complete`);

export interface SubmitMaturityAssessmentResultDto {
  referenceNumber: string;
  submittedAt: string;
}

// "Submit your assessment" — irreversible. 422 REPORT_INCOMPLETE when a section is still incomplete, 409 when
// already submitted.
export const submitMaturityAssessment = (collectionId: string) => axios.post<SubmitMaturityAssessmentResultDto>(`maturity-assessment/${collectionId}/submit`);

// Downloads the submitted assessment as a ZIP of CSVs. The filename comes from the response's Content-Disposition
// header ({local-authority}-maturity-assessment-{financial-year}.zip).
export const downloadMaturityExport = (collectionId: string): Promise<void> => downloadFile(`maturity-assessment/${collectionId}/export`, 'maturity-assessment.zip');
