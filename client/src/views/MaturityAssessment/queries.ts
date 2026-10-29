import axios from 'axios';
import type { SubmissionStatus } from '../Admin/Reports/queries';
import type { CollectionStatus } from '../Admin/Collections/queries';
import type { ReportProgressStatus, ReportQuestionOptionDto } from '../Reports/queries';

// Typed contract for the LA-facing annual maturity assessment (BLUM-7855 / 7856 / 7858 / 7859) — mirrors
// api/src/Api/Dtos/MaturityAssessmentDtos.cs (camelCase JSON).

// Where a saved answer came from (BLUM-7856 AC4): the three pre-fill sources, or the LA's own edit.
export type MaturityAnswerSource = 'quarterly' | 'prior_assessment' | 'baseline_2025' | 'user';

export interface MaturityAssessmentContextDto {
  collectionId: string;
  collectionName: string;
  financialYear: string;
  // The year the financial year ends in — the assessment's display name is "Maturity assessment for {endYear}".
  financialYearEndYear: number;
  reportingPeriodStart: string;
  reportingPeriodEnd: string;
  submissionDeadline: string;
  collectionStatus: CollectionStatus;
  isOpen: boolean;
  localAuthorityName: string;
  // Null until the task-list entry route starts the assessment.
  assessmentId: string | null;
  status: SubmissionStatus;
  // Null until the assessment is finalised (submitted).
  submittedAt: string | null;
  referenceNumber: string | null;
}

export interface MaturitySectionSummaryDto {
  code: string;
  title: string;
  status: ReportProgressStatus;
}

export interface MaturityTaskListDto {
  assessmentId: string;
  sections: MaturitySectionSummaryDto[];
  // Every section completed and not yet submitted.
  canSubmit: boolean;
  submitted: boolean;
  referenceNumber: string | null;
  submittedAt: string | null;
  // True while any saved answer still carries a non-user source — drives the "pre-filled" banner.
  isPrefilled: boolean;
  prefillSources: MaturityAnswerSource[];
}

// ReportQuestionDto's shape plus the annual assessment's extras: the page slug within the section, the matrix
// heading consecutive items share, the funded-roles time bands and the checkbox "None" exclusive option.
export interface MaturityQuestionDto {
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
  secondaryOptions: ReportQuestionOptionDto[];
  section: string;
  page: string;
  groupLabel: string | null;
  exclusiveOptionValue: string | null;
  errorMessage: string | null;
  maxLength: number | null;
  maxLengthErrorMessage: string | null;
}

// A whole page is hidden unless the named question holds one of the listed values (e.g. the needs-assessment
// follow-on pages behind "Has your local authority published a needs assessment?" = Yes).
export interface MaturityPageVisibilityDto {
  questionCode: string;
  values: string[];
}

export interface MaturityPageDto {
  code: string;
  title: string;
  visibleWhen: MaturityPageVisibilityDto | null;
  questions: MaturityQuestionDto[];
}

export interface MaturityAnswerDto {
  questionCode: string;
  value: string | null;
  source: MaturityAnswerSource;
}

export interface MaturitySectionDto {
  assessmentId: string;
  code: string;
  title: string;
  status: ReportProgressStatus;
  pages: MaturityPageDto[];
  answers: MaturityAnswerDto[];
}

// --- The read-only submitted copy (BLUM-7859) ---

export interface MaturitySnapshotRowDto {
  code: string;
  question: string;
  // Already rendered: option labels, the repeater's "Role 1: …" line, free text verbatim.
  answer: string;
  value: string | null;
  groupLabel: string | null;
}

export interface MaturitySnapshotPageDto {
  code: string;
  title: string;
  rows: MaturitySnapshotRowDto[];
}

export interface MaturitySnapshotSectionDto {
  code: string;
  title: string;
  pages: MaturitySnapshotPageDto[];
}

export interface SubmittedMaturityAssessmentDto {
  collectionId: string;
  collectionName: string;
  financialYear: string;
  financialYearEndYear: number;
  localAuthorityName: string;
  referenceNumber: string;
  submittedAt: string;
  submittedByName: string | null;
  submittedByEmail: string | null;
  sections: MaturitySnapshotSectionDto[];
}

// 404 when the collection is not an assigned, published maturity-assessment collection for the caller's
// organisation — the same anti-probing rule as the quarterly context. A closed window still answers.
export const getMaturityContext = (collectionId: string) => axios.get<MaturityAssessmentContextDto>(`maturity-assessment/${collectionId}/context`);

export const getMaturityTaskList = (collectionId: string) => axios.get<MaturityTaskListDto>(`maturity-assessment/${collectionId}/task-list`);

export const getMaturitySection = (collectionId: string, sectionCode: string) =>
  axios.get<MaturitySectionDto>(`maturity-assessment/${collectionId}/sections/${encodeURIComponent(sectionCode)}`);

// 404 until submitted.
export const getSubmittedMaturityAssessment = (collectionId: string) => axios.get<SubmittedMaturityAssessmentDto>(`maturity-assessment/${collectionId}/submitted`);
