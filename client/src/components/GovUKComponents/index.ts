// Re-export govuk-react's LoadingBox (no govuk-frontend equivalent). Other govuk-react primitives are deliberately not
// re-exported — they don't pick up govuk-frontend's CSS; use the GovUK* wrappers below instead.
export { LoadingBox } from 'govuk-react';

// Typography components — native govuk-frontend classes, GDS Transport font (replaces govuk-react H1–H4 etc. which inject the legacy "nta" font)
export { H1, H2, H3, H4, Paragraph, LeadParagraph, UnorderedList, OrderedList, ListItem } from './GovUKTypography';

// Custom components (not available in govuk-react or with extended functionality)
// ErrorSummary is GovUKErrorSummary under its older name — the same component, exported twice below.
export { default as ErrorSummary } from './GovUKErrorSummary';
export { default as PageLoading } from './PageLoading';
export { default as ConfirmActionPanel } from './ConfirmActionPanel';
export { default as GovUKFieldset } from './GovUKFieldset';
export type { RadioOption, CheckboxOption } from './GovUKFieldset';
export { default as GovUKRadio } from './GovUKRadio';
export { default as GovUKCheckbox } from './GovUKCheckbox';
export { default as GovUKDateField } from './GovUKDateField';
export type { DatePart, DateParts } from './GovUKDateField';
export { default as GovUKBreadcrumbs } from './GovUKBreadcrumbs';
export { default as GovUKNotificationBanner } from './GovUKNotificationBanner';
export { default as GovUKBackLink } from './GovUKBackLink';
export { default as GovUKButton } from './GovUKButton';
export { default as ProtectedRoleRoute } from './ProtectedRoleRoute';
export { default as FeatureRoute } from './FeatureRoute';
export { default as NotAuthorised } from './NotAuthorised';
export { default as NotFound } from './NotFound';
export { default as ChunkErrorBoundary } from './ChunkErrorBoundary';
export { default as DeleteConfirmDialog } from './DeleteConfirmDialog';
export { default as SummaryList } from './SummaryList';
export { default as GovUKSummaryCard } from './GovUKSummaryCard';
export type { SummaryCardAction } from './GovUKSummaryCard';
export { default as GovUKTable, NameLink, StatusCell } from './GovUKTable';
export type { Column, GovUKTableProps, FilterOption } from './GovUKTable';
export { default as GovUKTableScroll } from './GovUKTableScroll';
export type { GovUKTableScrollProps } from './GovUKTableScroll';

// GOV.UK Design System compliant components using official HTML/classes
export { default as GovUKTag } from './GovUKTag';
export type { TagColour } from './GovUKTag';
export { default as GovUKHeader } from './GovUKHeader';
export { default as GovUKInsetText } from './GovUKInsetText';
export { default as GovUKErrorSummary } from './GovUKErrorSummary';
export type { GovUKErrorSummaryItem } from './GovUKErrorSummary';
export { default as GovUKPanel } from './GovUKPanel';
export { default as GovUKPhaseBanner } from './GovUKPhaseBanner';
export { default as GovUKWarningText } from './GovUKWarningText';
export { default as GovUKDetails } from './GovUKDetails';
export { default as GovUKTaskList } from './GovUKTaskList';
export type { TaskListItem, TaskListItemStatus } from './GovUKTaskList';
export { default as GovUKPagination } from './GovUKPagination';
export type { PaginationLink } from './GovUKPagination';
export { default as GovUKActionMenu } from './GovUKActionMenu';
export type { ActionMenuItem, GovUKActionMenuProps } from './GovUKActionMenu';
export { default as GovUKSideNavigation } from './GovUKSideNavigation';
export type { SideNavigationItem, SideNavigationSection } from './GovUKSideNavigation';
export { default as GovUKSearchInput } from './GovUKSearchInput';
export type { GovUKSearchInputProps } from './GovUKSearchInput';
export { default as GovUKInputWithSuffix } from './GovUKInputWithSuffix';
export { default as GovUKDynamicQuestionRenderer, isQuestionVisible } from './GovUKDynamicQuestionRenderer';
export type {
  FormQuestion,
  FormQuestionOption,
  FormState,
  ValidationErrors as DynamicQuestionValidationErrors,
  GovUKDynamicQuestionRendererProps,
} from './GovUKDynamicQuestionRenderer';
export { default as GovUKGroupedInputs } from './GovUKGroupedInputs';
export { GovUKNotificationProvider, useGovUKNotification, GovUKNotificationDisplay } from './GovUKNotificationProvider';
export type { GovUKNotification } from './GovUKNotificationProvider';
export { default as GovUKSkipLink } from './GovUKSkipLink';
