import axios from 'axios';

export type CollectionStatus = 'draft' | 'scheduled' | 'open' | 'closed';

export type CollectionType = 'quarterly_report' | 'maturity_assessment';

export type ReminderOffsetType = 'after_open' | 'before_deadline';

export interface CollectionFormDto {
  moduleId: string;
  code: string | null;
  name: string | null;
}

export interface CollectionLocalAuthorityDto {
  id: string;
  name: string | null;
}

export interface CollectionReminderDto {
  offsetType: ReminderOffsetType;
  days: number;
}

export interface CollectionSummaryDto {
  id: string;
  name: string;
  type: CollectionType | null;
  deadline: string | null;
  status: CollectionStatus;
}

// Setup questions other than name are nullable: a draft may have any of them unanswered, and null means
// exactly that ("not answered yet") — never render it as "No"/empty-string answers.
export interface CollectionDto {
  id: string;
  name: string;
  purpose: string | null;
  type: CollectionType | null;
  financialYear: string | null;
  quarter: number | null;
  reportingPeriodStart: string | null;
  reportingPeriodEnd: string | null;
  windowSameAsReportingPeriod: boolean | null;
  opensDate: string | null;
  deadline: string | null;
  isSubmittedByAllLas: boolean | null;
  forms: CollectionFormDto[];
  localAuthorities: CollectionLocalAuthorityDto[];
  reminders: CollectionReminderDto[];
  status: CollectionStatus;
  isDraft: boolean;
  createdAt: string;
  createdBy: string | null;
  updatedAt: string | null;
  updatedBy: string | null;
}

export interface CollectionFormOptionDto {
  moduleId: string;
  name: string;
  lastChangedAt: string | null;
}

export interface ReportingLaDto {
  id: string;
  name: string;
}

export const getCollections = () => axios.get<CollectionSummaryDto[]>('data-collections');

export const getDeletedCollections = () => axios.get<CollectionSummaryDto[]>('data-collections/deleted');

export const getCollection = (id: string) => axios.get<CollectionDto>(`data-collections/${id}`);

export const getCollectionFormOptions = () => axios.get<CollectionFormOptionDto[]>('data-collections/form-options');

export const getReportingLas = () => axios.get<ReportingLaDto[]>('data-collections/reporting-las');
