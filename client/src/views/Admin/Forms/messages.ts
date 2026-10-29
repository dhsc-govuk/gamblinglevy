import { httpDataOf, httpMessageOf } from '../../../helpers/httpErrors';

// Reads the structured error code returned by the forms-builder error factory (DataCollectionFormErrorFactory).
export const formsErrorCode = (error: unknown): string | undefined => {
  const data = httpDataOf(error);
  return data && typeof data === 'object' && 'error' in data && typeof data.error === 'string' ? data.error : undefined;
};

// The question-form field a refused question save belongs on, so its error-summary link reaches it. A
// validation_failed refusal names the C# property it rejected; anything that is not about one field (the form was
// published meanwhile, a conflict) goes to the label, the first field an admin edits.
const QUESTION_FIELD_IDS: Record<string, string> = {
  Code: 'admin-forms-question-code',
  Label: 'admin-forms-question-label',
  QuestionType: 'admin-forms-question-type',
  SectionId: 'admin-forms-question-section',
  Hint: 'admin-forms-question-hint',
  HelpText: 'admin-forms-question-help-text',
  Options: 'admin-forms-question-options',
};

export const questionErrorTarget = (error: unknown): string => {
  const code = formsErrorCode(error);
  if (code === 'DUPLICATE_QUESTION_CODE' || code === 'QUESTION_CODE_RETIRED') {
    return QUESTION_FIELD_IDS.Code;
  }
  if (code === 'OPTION_VALUE_FIXED') {
    return QUESTION_FIELD_IDS.Options;
  }
  const data = httpDataOf(error);
  const errors = data && typeof data === 'object' && 'errors' in data && Array.isArray(data.errors) ? data.errors : [];
  const property: unknown = errors[0]?.property;
  const field = typeof property === 'string' ? property.split('.')[0].split('[')[0] : undefined;
  return (field && QUESTION_FIELD_IDS[field]) ?? QUESTION_FIELD_IDS.Label;
};

// Maps a forms-builder API error to a single user-facing message. The codes are the ones
// DataCollectionFormErrorFactory.ErrorCode writes, so a rename there must be copied here.
export const mapFormsError = (error: unknown): string => {
  switch (formsErrorCode(error)) {
    case 'DUPLICATE_MODULE_CODE':
      return 'A form with this code already exists. Choose a different code.';
    case 'DUPLICATE_QUESTION_CODE':
      return 'A question with this code already exists in this form. Choose a different code.';
    // BLUM-9105: answers are matched across versions by code and option value, so both are fixed once published.
    case 'QUESTION_CODE_RETIRED':
      return 'An earlier version of this form used this code for another question. Choose a different code.';
    case 'OPTION_VALUE_FIXED':
      return 'An answer option that has been published cannot have its value changed or be removed. You can change its label.';
    case 'MODULE_NOT_EDITABLE':
      return 'This form is published and cannot be changed. Start a new draft to make changes.';
    case 'MODULE_EMPTY_ON_PUBLISH':
      return 'Add at least one question before you publish this form.';
    case 'MODULE_NOT_PUBLISHED':
      return 'This form is already a draft.';
    case 'MODULE_VERSION_CONFLICT':
      return 'Someone else published this form at the same time. Reload the page and try again.';
    case 'VERSION_NOT_FOUND':
      return 'This version of the form could not be found.';
    case 'MODULE_ARCHIVED':
      return 'This form is archived, so a previous version cannot be restored into it.';
    case 'SECTION_NOT_EMPTY':
      return 'Move or delete this section’s questions before deleting the section.';
    case 'MODULE_NOT_FOUND':
    case 'SECTION_NOT_FOUND':
    case 'QUESTION_NOT_FOUND':
      return 'This form has changed since you opened it. Reload the page and try again.';
    default:
      return httpMessageOf(error) ?? 'Something went wrong. Try again.';
  }
};
